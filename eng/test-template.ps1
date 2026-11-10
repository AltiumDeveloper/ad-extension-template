<#
.SYNOPSIS
  Checks the template package: generates extensions with each option and checks the output, without
  Altium Designer. Used by CI; runs locally too.

.DESCRIPTION
  Needs both packages in -Artifacts (dotnet pack ... -o artifacts). For each option set it checks the
  generated files, that no template markers or placeholders are left, that the projects restore against
  the Altium.Designer.HotReload package in -Artifacts, and that the target framework follows the
  installation's X2.runtimeconfig.json. Code that references the Altium SDK is not compiled: that needs
  an Altium Designer installation.

.EXAMPLE
  ./eng/test-template.ps1 -Artifacts artifacts
#>
param(
    [Parameter(Mandatory)] [string] $Artifacts,
    [string] $WorkDir = (Join-Path ([IO.Path]::GetTempPath()) "ad-extension-template-test")
)

$ErrorActionPreference = 'Stop'
$Artifacts = (Resolve-Path $Artifacts).Path
$failures = [Collections.Generic.List[string]]::new()
function Check([bool] $condition, [string] $message) {
    if ($condition) { Write-Host "  ok   $message" } else { Write-Host "  FAIL $message" -ForegroundColor Red; $failures.Add($message) }
}
function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed ($LASTEXITCODE)" }
}

$templatePackage = Get-ChildItem $Artifacts -Filter 'Altium.Designer.Extension.Templates.*.nupkg' | Select-Object -First 1
$hotReloadPackage = Get-ChildItem $Artifacts -Filter 'Altium.Designer.HotReload.*.nupkg' | Select-Object -First 1
if (-not $templatePackage -or -not $hotReloadPackage) { throw "Both packages must be in $Artifacts" }

if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
$hive = Join-Path $WorkDir 'hive'   # a private template cache: nothing is installed for the user
$out = Join-Path $WorkDir 'out'
New-Item -ItemType Directory -Force -Path $hive, $out | Out-Null
Invoke-Dotnet new install $templatePackage.FullName --debug:custom-hive $hive

# A restore that resolves Altium.Designer.HotReload from the artifacts only (plus nuget.org for the test packages).
$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="artifacts" value="$([Security.SecurityElement]::Escape($Artifacts))" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="artifacts"><package pattern="Altium.Designer.HotReload" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@

$cases = @(
    @{ Name = 'Default'; Args = @();                         Tests = $true;  Panel = $false; Outputer = $false },
    @{ Name = 'Minimal'; Args = @('--tests', 'false');       Tests = $false; Panel = $false; Outputer = $false },
    @{ Name = 'Full';    Args = @('--panel', '--outputer');  Tests = $true;  Panel = $true;  Outputer = $true }
)

foreach ($case in $cases) {
    $name = "Gen$($case.Name)"
    $dir = Join-Path $out $name
    Write-Host "`n== dotnet new ad-extension -n $name $($case.Args -join ' ')"
    Invoke-Dotnet new ad-extension -n $name -o $dir @($case.Args) --debug:custom-hive $hive

    $files = Get-ChildItem $dir -Recurse -File | ForEach-Object { $_.FullName.Substring($dir.Length + 1) }
    $has = { param($path) $files -contains $path }

    foreach ($f in "$name.sln", 'Directory.Build.props', 'README.md', '.gitignore',
                   "$name\$name.csproj", "$name\PluginServerModule.cs", "$name\Commands.cs", "$name\Implementation.cs",
                   "$name\Installation\$name.ins", "$name\Installation\$name.rcs",
                   "$name.Impl\$name.Impl.csproj", "$name.Impl\Framework\HotEntry.cs", "$name.Impl\Commands\HelloCommand.cs",
                   'tools\deploy.ps1', 'tools\dev.ps1', 'tools\ad.ps1', 'tools\hotreload.ps1') {
        Check (& $has $f) "$($case.Name): $f exists"
    }
    Check (-not (& $has 'nuget.config')) "$($case.Name): no nuget.config (HotReload comes from nuget.org)"
    Check ((& $has "$name.Tests\$name.Tests.csproj") -eq $case.Tests) "$($case.Name): test project present = $($case.Tests)"
    Check ((& $has "$name\Panel.cs") -eq $case.Panel) "$($case.Name): Panel.cs present = $($case.Panel)"
    Check ((& $has "$name.Impl\Ui\PanelContent.cs") -eq $case.Panel) "$($case.Name): panel content present = $($case.Panel)"
    Check ((& $has "$name\ReportOutput.cs") -eq $case.Outputer) "$($case.Name): ReportOutput.cs present = $($case.Outputer)"
    Check ((& $has "$name\Installation\$name.out") -eq $case.Outputer) "$($case.Name): .out present = $($case.Outputer)"

    $all = Get-ChildItem $dir -Recurse -File | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }
    $text = $all -join "`n"
    Check ($text -notmatch 'MyExtension') "$($case.Name): no 'MyExtension' left"
    Check ($text -notmatch '(?m)^\s*(//)?#(if|else|elif|endif)\s*\(') "$($case.Name): no template conditionals left"
    Check ($text -notmatch '(?m)^\s*//[-+]:cnd') "$($case.Name): no //-:cnd markers left"
    Check ($text -notmatch 'ALTIUM_EXTENSIONS_DIR') "$($case.Name): ExtensionsDir placeholder replaced"

    $ins = [IO.File]::ReadAllText((Join-Path $dir "$name\Installation\$name.ins"))
    $rcs = [IO.File]::ReadAllText((Join-Path $dir "$name\Installation\$name.rcs"))
    Check (($ins -match 'PanelInfo') -eq $case.Panel) "$($case.Name): PanelInfo in .ins = $($case.Panel)"
    Check (($rcs -match 'Cmd=Panel\.Show') -eq $case.Panel) "$($case.Name): Show Panel menu item = $($case.Panel)"
    Check ($ins -notmatch '(?m)^[^/\r\n]*"') "$($case.Name): .ins has no stray text"
    $balanced = { param($s) ([regex]::Matches($s, '(?m)^#if DEBUG\r?$').Count) -eq ([regex]::Matches($s, '(?m)^#endif\r?$').Count) }
    Check ((& $balanced $ins) -and (& $balanced $rcs)) "$($case.Name): #if DEBUG blocks balanced in .ins/.rcs"
    Check (([regex]::Matches($rcs, '(?m)^(PL|Tree|Insertion)\b').Count) -eq ([regex]::Matches($rcs, '(?m)^End\r?$').Count)) "$($case.Name): .rcs blocks closed"

    $sln = [IO.File]::ReadAllText((Join-Path $dir "$name.sln"))
    Check (($sln -match "$name\.Tests\.csproj") -eq $case.Tests) "$($case.Name): .sln lists the test project = $($case.Tests)"
    foreach ($xml in Get-ChildItem $dir -Recurse -Include *.csproj, *.props) {
        $ok = try { [xml][IO.File]::ReadAllText($xml.FullName) | Out-Null; $true } catch { $false }
        Check $ok "$($case.Name): $($xml.Name) is well-formed XML"
    }

    # Restore resolves the HotReload version the shim references from the artifacts.
    Set-Content -Path (Join-Path $dir 'nuget.config') -Value $nugetConfig -Encoding UTF8
    & dotnet restore (Join-Path $dir "$name.sln") -nologo -v q
    Check ($LASTEXITCODE -eq 0) "$($case.Name): restore (Altium.Designer.HotReload $($hotReloadPackage.BaseName.Split('.', 4)[3]) from artifacts)"
}

# Target framework: from X2.runtimeconfig.json of the installation, net8.0-windows without one.
Write-Host "`n== Target framework detection"
$project = Join-Path $out "GenDefault\GenDefault.Impl\GenDefault.Impl.csproj"
function Get-Tfm([string] $systemDir) {
    (& dotnet msbuild $project -nologo "-p:AltiumSystemDir=$systemDir" -getProperty:TargetFramework).Trim()
}
foreach ($tfm in 'net8.0', 'net10.0') {
    $install = Join-Path $WorkDir "FakeAD-$tfm"
    New-Item -ItemType Directory -Force -Path (Join-Path $install 'System') | Out-Null
    Set-Content -Path (Join-Path $install 'X2.runtimeconfig.json') -Encoding UTF8 -Value @"
{
  "runtimeOptions": {
    "tfm": "$tfm",
    "rollForward": "Disable"
  }
}
"@
    Check ((Get-Tfm (Join-Path $install 'System')) -eq "$tfm-windows") "installation hosting $tfm -> $tfm-windows"
}
Check ((Get-Tfm (Join-Path $WorkDir 'NoSuchInstall\System')) -eq 'net8.0-windows') "no X2.runtimeconfig.json -> net8.0-windows"
$override = (& dotnet msbuild $project -nologo "-p:AltiumSystemDir=$(Join-Path $WorkDir 'FakeAD-net10.0\System')" -p:AltiumTargetFramework=net8.0-windows -getProperty:TargetFramework).Trim()
Check ($override -eq 'net8.0-windows') "AltiumTargetFramework overrides the detection"

if ($failures.Count) {
    Write-Host "`n$($failures.Count) check(s) failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nAll template checks passed." -ForegroundColor Green
