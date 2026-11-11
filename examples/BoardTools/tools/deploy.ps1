<#
.SYNOPSIS
  Builds BoardTools and installs it into Altium Designer's extension folder.

.DESCRIPTION
  Debug (default): installs the shim with the hot-reload host compiled in. The path of
  BoardTools.Impl\bin\Debug is baked into it at build time; the shim watches it and serves the
  dev pipe. After that, only tools\dev.ps1 is needed: no redeploy, no AD restart.

  Release: installs the shim with BoardTools.Impl next to it. No hot reload, no pipe.

  Re-run after changing the shim (BoardTools\*) or Installation\*, or to switch configuration.
  Works while AD is running: locked files are renamed aside. AD picks the new shim up on restart.

.EXAMPLE
  .\tools\deploy.ps1
  .\tools\deploy.ps1 -Configuration Release
#>
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$shimProject = Join-Path $root 'BoardTools\BoardTools.csproj'
$implProject = Join-Path $root 'BoardTools.Impl\BoardTools.Impl.csproj'

$props = (& dotnet msbuild $shimProject -nologo "-p:Configuration=$Configuration" `
    -getProperty:AltiumSystemDir -getProperty:AltiumExtensionsDir -getProperty:ExtensionFolderName `
    -getProperty:ExtensionGuid -getProperty:TargetFramework -getProperty:OutDir | Out-String | ConvertFrom-Json).Properties
if ($LASTEXITCODE -ne 0 -or -not $props.ExtensionGuid) { throw 'Could not read the build properties of BoardTools.csproj' }

# The Extensions folder: AltiumExtensionsDir if set, else that of the installation AltiumSystemDir belongs to.
# Each installation is a key under HKLM\SOFTWARE\Altium\Builds: ProgramsInstallPath is its program folder,
# and Application its data folder under %ProgramData% (Altium\Altium Designer {<installation GUID>}).
function Find-ExtensionsDir([string] $systemDir) {
    $programDir = [IO.Path]::GetFullPath((Join-Path $systemDir '..')).TrimEnd('\')
    $builds = Get-ChildItem 'HKLM:\SOFTWARE\Altium\Builds' -ErrorAction SilentlyContinue | Get-ItemProperty
    $build = $builds | Where-Object { $_.ProgramsInstallPath -and $_.ProgramsInstallPath.TrimEnd('\') -ieq $programDir } | Select-Object -First 1
    if (-not $build) {
        $known = ($builds | Where-Object ProgramsInstallPath | ForEach-Object { "  $($_.ProgramsInstallPath)\System  ($($_.Version))" }) -join "`n"
        throw "No Altium Designer installation is registered at $programDir.`nInstallations found:`n$known`n" +
              "Set AltiumSystemDir in Directory.Build.props (or AltiumExtensionsDir to the Extensions folder directly)."
    }
    return Join-Path $env:ProgramData "$($build.Application)\Extensions"
}

$extensionsDir = if ($props.AltiumExtensionsDir) { $props.AltiumExtensionsDir } else { Find-ExtensionsDir $props.AltiumSystemDir }
$extensionsDir = [IO.Path]::GetFullPath($extensionsDir).TrimEnd('\')
# The folder is mirrored (files it doesn't ship are removed): it must be a plain child of the Extensions folder,
# never the Extensions folder itself, which holds ExtensionsRegistry.xml.
$name = $props.ExtensionFolderName
if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]*$') { throw "ExtensionFolderName '$name' in BoardTools.csproj is not a plain folder name." }
$installDir = Join-Path $extensionsDir $name
$registry = Join-Path $extensionsDir 'ExtensionsRegistry.xml'
if (-not (Test-Path $registry)) { throw "$registry not found: start Altium Designer once, or check AltiumExtensionsDir." }
$extensionGuid = $props.ExtensionGuid
Write-Host "Target: $installDir ($($props.TargetFramework))"

# Builds quietly: prints only the errors and warnings (each once), everything when a build fails without them.
function Invoke-Build([string] $project, [string] $configuration) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # Windows PowerShell turns native stderr into errors
    try { $output = @(& dotnet build $project -c $configuration -nologo -v m 2>&1 | ForEach-Object { "$_" }) }
    finally { $ErrorActionPreference = $previous }
    $failed = $LASTEXITCODE -ne 0
    $issues = $output | Where-Object { $_ -match ':\s+(error|warning)\s+[A-Z]+\d+' } |
        ForEach-Object { ($_ -replace '\s+\[[^\]]+\]\s*$', '').Trim() } | Select-Object -Unique
    foreach ($line in $issues) { Write-Host $line -ForegroundColor $(if ($line -match ':\s+error\s') { 'Red' } else { 'Yellow' }) }
    if ($failed -and -not $issues) { $output | ForEach-Object { Write-Host $_ } }
    if ($failed) { throw "Build failed: $project" }
}

Write-Host "Building ($Configuration)..."
foreach ($project in $shimProject, $implProject) { Invoke-Build $project $Configuration }

$shimOut = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $shimProject) $props.OutDir)).TrimEnd('\')
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

# Replace (or remove) a file AD may have loaded: Windows allows renaming a mapped image, not overwriting it.
$stamp = Get-Date -Format 'yyyyMMddHHmmss'
function Move-Aside($path) {
    try { Remove-Item $path -Force }
    catch { Rename-Item $path ((Split-Path -Leaf $path) + ".old-$stamp") }
}
function Get-RelativeFiles([string] $dir) {
    Get-ChildItem $dir -File -Recurse | ForEach-Object { $_.FullName.Substring($dir.Length + 1) }
}

# Clean up files renamed aside by previous deployments (succeeds once AD released them).
Get-ChildItem $installDir -Filter '*.old-*' -File -Recurse | ForEach-Object { try { Remove-Item $_.FullName -Force } catch { } }

# The install folder mirrors the shim output, subfolders included (runtimes\, satellite assemblies):
# drop what this configuration does not ship.
$shipped = @(Get-RelativeFiles $shimOut)
Get-RelativeFiles $installDir | Where-Object { $_ -notlike '*.old-*' -and $shipped -notcontains $_ } |
    ForEach-Object { Move-Aside (Join-Path $installDir $_); Write-Host "  removed $_" }
Get-ChildItem $installDir -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending |
    Where-Object { -not (Get-ChildItem $_.FullName -Force) } | ForEach-Object { Remove-Item $_.FullName -Force }

foreach ($relative in $shipped) {
    $target = Join-Path $installDir $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    if (Test-Path $target) { Move-Aside $target }
    Copy-Item (Join-Path $shimOut $relative) $target
    Write-Host "  installed $relative"
}

# AD loads only the extensions listed in Extensions\ExtensionsRegistry.xml; a folder alone is ignored.
# Add the entry the Altium Developer wizard writes, once. The rest of the file is left byte-identical,
# and a backup is kept next to it. The Guid is the project's ExtensionGuid, so reinstalling on any
# machine gives the same entry. PlatformVersions are the wizard's: the lowest platform versions an
# extension needs (Max* 0.0.0.0 = no upper limit); AD's own extensions list higher ones.
$text = [IO.File]::ReadAllText($registry)
$items = @(([xml]$text).SelectNodes('/Extensions/Item'))
$existing = $items | Where-Object { $_.GetAttribute('HRID') -ieq $name } | Select-Object -First 1
$sameGuid = $items | Where-Object { $_.GetAttribute('Guid') -ieq $extensionGuid -and $_.GetAttribute('HRID') -ine $name }
foreach ($other in $sameGuid) {
    Write-Host "  warning: '$($other.GetAttribute('HRID'))' in $registry has the same Guid ($extensionGuid); remove that entry if it is an old name of this extension." -ForegroundColor Yellow
}
if ($existing) {
    $path = $existing.SelectSingleNode('Path')
    if ($path -and $path.InnerText.TrimEnd('\') -ine $installDir) {
        Write-Host "  warning: $registry lists $name at $($path.InnerText), not $installDir; AD loads it from there." -ForegroundColor Yellow
    }
}
else {
    $escapedName = [Security.SecurityElement]::Escape($name)
    $entry = @"
  <Item HRID="$escapedName" Guid="$extensionGuid">
    <Path>$([Security.SecurityElement]::Escape($installDir))</Path>
    <Status>0</Status>
    <VaultGuid></VaultGuid>
    <CreatedBy>Altium Limited</CreatedBy>
    <CategoryGuid>793A1F67-0B22-4E01-A5DE-3176A1E8C60D</CategoryGuid>
    <CategoryName></CategoryName>
    <ReadMe></ReadMe>
    <Help></Help>
    <Requirements></Requirements>
    <Title>$escapedName</Title>
    <ShortDescription></ShortDescription>
    <LongDescription></LongDescription>
    <SmallImage></SmallImage>
    <LargeImage></LargeImage>
    <Version></Version>
    <VersionGuid></VersionGuid>
    <ReleasedDate>0</ReleasedDate>
    <ReleaseNotes></ReleaseNotes>
    <DateInstalled>0</DateInstalled>
    <PlatformVersions>
      <DXP BuildNumber="1.0.6.0"/>
      <EDP BuildNumber="10.0.6.0"/>
      <MaxDXP BuildNumber="0.0.0.0"/>
      <MaxEDP BuildNumber="0.0.0.0"/>
    </PlatformVersions>
  </Item>

"@ -replace "`r?`n", "`r`n"
    # An empty registry may be written as <Extensions/>: open it up first.
    $text = [regex]::Replace($text, '<Extensions\s*/>', "<Extensions>`r`n</Extensions>")
    $end = $text.LastIndexOf('</Extensions>')
    if ($end -lt 0) { throw "$registry has no </Extensions> element: not changed." }
    $text = $text.Insert($end, $entry)
    [xml]$text | Out-Null  # never write a file AD cannot parse
    Copy-Item $registry "$registry.bak-$stamp"
    [IO.File]::WriteAllText($registry, $text, (New-Object Text.UTF8Encoding $false))
    Write-Host "  registered $name in $registry"
}

if (Get-Process X2 -ErrorAction SilentlyContinue) {
    Write-Host ''
    Write-Host 'Altium Designer is running: the new shim takes effect on its next restart.' -ForegroundColor Yellow
}
