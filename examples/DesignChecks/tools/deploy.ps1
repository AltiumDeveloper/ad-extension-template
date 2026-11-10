<#
.SYNOPSIS
  Builds DesignChecks and installs it into Altium Designer's extension folder.

.DESCRIPTION
  Debug (default): installs the shim with the hot-reload host compiled in. The path of
  DesignChecks.Impl\bin\Debug is baked into it at build time; the shim watches it and serves the
  dev pipe. After that, only tools\dev.ps1 is needed: no redeploy, no AD restart.

  Release: installs the shim with DesignChecks.Impl next to it. No hot reload, no pipe.

  Re-run after changing the shim (DesignChecks\*) or Installation\*, or to switch configuration.
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
$shimProject = Join-Path $root 'DesignChecks\DesignChecks.csproj'
$implProject = Join-Path $root 'DesignChecks.Impl\DesignChecks.Impl.csproj'

$props = (& dotnet msbuild $shimProject -nologo "-p:Configuration=$Configuration" `
    -getProperty:AltiumSystemDir -getProperty:AltiumExtensionsDir -getProperty:ExtensionFolderName `
    -getProperty:ExtensionGuid -getProperty:TargetFramework -getProperty:OutDir | Out-String | ConvertFrom-Json).Properties
if ($LASTEXITCODE -ne 0 -or -not $props.ExtensionGuid) { throw 'Could not read the build properties of DesignChecks.csproj' }

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
$installDir = Join-Path $extensionsDir $props.ExtensionFolderName
$extensionGuid = $props.ExtensionGuid
Write-Host "Target: $installDir ($($props.TargetFramework))"

Write-Host "Building ($Configuration)..."
foreach ($project in $shimProject, $implProject) {
    & dotnet build $project -c $Configuration -nologo -v q -clp:NoSummary
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}

$shimOut = Join-Path (Split-Path -Parent $shimProject) $props.OutDir
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

# Replace (or remove) a file AD may have loaded: Windows allows renaming a mapped image, not overwriting it.
$stamp = Get-Date -Format 'yyyyMMddHHmmss'
function Move-Aside($path) {
    try { Remove-Item $path -Force }
    catch { Rename-Item $path ((Split-Path -Leaf $path) + ".old-$stamp") }
}

# Clean up files renamed aside by previous deployments (succeeds once AD released them).
Get-ChildItem $installDir -Filter '*.old-*' | ForEach-Object { try { Remove-Item $_.FullName -Force } catch { } }

# The install folder mirrors the shim output: drop what this configuration does not ship.
$shipped = Get-ChildItem $shimOut -File | ForEach-Object Name
Get-ChildItem $installDir -File | Where-Object { $_.Name -notlike '*.old-*' -and $shipped -notcontains $_.Name } |
    ForEach-Object { Move-Aside $_.FullName; Write-Host "  removed $($_.Name)" }

foreach ($file in Get-ChildItem $shimOut -File) {
    $target = Join-Path $installDir $file.Name
    if (Test-Path $target) { Move-Aside $target }
    Copy-Item $file.FullName $target
    Write-Host "  installed $($file.Name)"
}

# AD loads only the extensions listed in Extensions\ExtensionsRegistry.xml; a folder alone is ignored.
# Add the entry the Altium Developer wizard writes, once. The rest of the file is left byte-identical,
# and a backup is kept next to it. The Guid is the project's ExtensionGuid, so reinstalling on any
# machine gives the same entry. PlatformVersions are the wizard's: the lowest platform versions an
# extension needs (Max* 0.0.0.0 = no upper limit); AD's own extensions list higher ones.
$registry = Join-Path (Split-Path -Parent $installDir) 'ExtensionsRegistry.xml'
$name = Split-Path -Leaf $installDir
if (-not (Test-Path $registry)) { throw "$registry not found: start Altium Designer once, or check AltiumExtensionsDir." }
$text = [IO.File]::ReadAllText($registry)
if (-not ([xml]$text).SelectSingleNode("/Extensions/Item[@HRID='$name']")) {
    $entry = @"
  <Item HRID="$name" Guid="$extensionGuid">
    <Path>$([Security.SecurityElement]::Escape($installDir))</Path>
    <Status>0</Status>
    <VaultGuid></VaultGuid>
    <CreatedBy>Altium Limited</CreatedBy>
    <CategoryGuid>793A1F67-0B22-4E01-A5DE-3176A1E8C60D</CategoryGuid>
    <CategoryName></CategoryName>
    <ReadMe></ReadMe>
    <Help></Help>
    <Requirements></Requirements>
    <Title>$name</Title>
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
    $text = $text.Insert($text.LastIndexOf('</Extensions>'), $entry)
    [xml]$text | Out-Null  # never write a file AD cannot parse
    Copy-Item $registry "$registry.bak-$stamp"
    [IO.File]::WriteAllText($registry, $text, (New-Object Text.UTF8Encoding $false))
    Write-Host "  registered $name in $registry"
}

if (Get-Process X2 -ErrorAction SilentlyContinue) {
    Write-Host ''
    Write-Host 'Altium Designer is running: the new shim takes effect on its next restart.' -ForegroundColor Yellow
}
