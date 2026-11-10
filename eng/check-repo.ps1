<#
.SYNOPSIS
  Repository consistency checks: copies that must stay identical, versions that must match, and nothing
  machine-specific or unpublishable. Used by CI; runs locally too.

.EXAMPLE
  ./eng/check-repo.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [Collections.Generic.List[string]]::new()
function Check([bool] $condition, [string] $message) {
    if ($condition) { Write-Host "  ok   $message" } else { Write-Host "  FAIL $message" -ForegroundColor Red; $failures.Add($message) }
}
function Read-Text([string] $path) { ([IO.File]::ReadAllText($path)) -replace "`r`n", "`n" }

$hotReload = Join-Path $root 'source\Altium.Designer.HotReload'
$template = Join-Path $root 'source\Altium.Designer.Extension.Templates\content\ad-extension'
$examples = Get-ChildItem (Join-Path $root 'examples') -Directory

Write-Host '== Copies of hotreload.ps1'
$client = Read-Text (Join-Path $hotReload 'tools\hotreload.ps1')
Check ((Read-Text (Join-Path $template 'tools\hotreload.ps1')) -eq $client) "template tools/hotreload.ps1 = source/Altium.Designer.HotReload/tools/hotreload.ps1"
foreach ($example in $examples) {
    # Generated copies: the template engine replaces the name in the usage examples too.
    $copy = Join-Path $example.FullName 'tools\hotreload.ps1'
    Check ((Read-Text $copy) -eq ($client -replace 'MyExtension', $example.Name)) "examples/$($example.Name)/tools/hotreload.ps1 = the package's (name substituted)"
}

Write-Host '== Template files in the examples'
# Files the examples take unchanged from the template (with the extension name substituted).
$shared = 'Directory.Build.props', 'tools\ad.ps1', 'tools\deploy.ps1', 'tools\dev.ps1',
          'MyExtension\Commands.cs', 'MyExtension\Implementation.cs',
          'MyExtension.Impl\Framework\CommandContext.cs', 'MyExtension.Impl\Framework\CommandRegistry.cs',
          'MyExtension.Impl\Framework\UserException.cs'
foreach ($example in $examples) {
    $name = $example.Name
    foreach ($file in $shared) {
        $expected = (Read-Text (Join-Path $template $file)) -replace 'MyExtension', $name -replace 'ALTIUM_EXTENSIONS_DIR', '' -replace '(?m)^//[-+]:cnd:noEmit\n', ''
        $path = Join-Path $example.FullName ($file -replace 'MyExtension', $name)
        Check ((Test-Path $path) -and (Read-Text $path) -eq $expected) "examples/$name/$($file -replace 'MyExtension', $name) matches the template"
    }
}

Write-Host '== Versions'
$version = ([xml](Get-Content (Join-Path $hotReload 'Altium.Designer.HotReload.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$shims = @(Join-Path $template 'MyExtension\MyExtension.csproj') + ($examples | ForEach-Object { Join-Path $_.FullName "$($_.Name)\$($_.Name).csproj" })
foreach ($shim in $shims) {
    $ref = [regex]::Match((Read-Text $shim), '<PackageReference Include="Altium.Designer.HotReload" Version="([^"]+)"').Groups[1].Value
    Check ($ref -eq $version) "$($shim.Substring($root.Length + 1)) references Altium.Designer.HotReload $version (has '$ref')"
}

Write-Host '== Nothing machine-specific or unpublishable'
$textFiles = Get-ChildItem $root -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj|artifacts|TestResults)\\' -and $_.Extension -notin '.png', '.ico', '.bmp', '.dll', '.nupkg' }
$patterns = [ordered]@{
    'a user profile path (C:\Users\<name>)'        = '(?i)C:\\Users\\(?!Public\\)'
    'a local checkout path'                         = '(?i)C:\\(Dev|work|AltiumDesignerExtensionTemplate)\\'
    'an installation GUID of a specific machine'    = '(?i)\{(4945A7E3|C824F698|D4EAF49C)-'
    'a TODO left for publishing'                    = '(?i)TODO'
    'a local package feed'                          = '(?i)hot-?reload-?(local|feed)'
}
foreach ($p in $patterns.GetEnumerator()) {
    $hits = $textFiles | Select-String -Pattern $p.Value -List | Where-Object { $_.Path -ne $PSCommandPath }
    Check (-not $hits) "no $($p.Key)$(if ($hits) { ': ' + (($hits | ForEach-Object { $_.Path.Substring($root.Length + 1) + ':' + $_.LineNumber }) -join ', ') })"
}
$binaries = Get-ChildItem $root -Recurse -File -Include *.nupkg, *.snapshot.json, *.PrjPcb, *.SchDoc, *.PcbDoc |
    Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj|artifacts)\\' }
Check (-not $binaries) "no packages or design files in the tree$(if ($binaries) { ': ' + ($binaries.Name -join ', ') })"

if ($failures.Count) {
    Write-Host "`n$($failures.Count) check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host "`nAll repository checks passed." -ForegroundColor Green
