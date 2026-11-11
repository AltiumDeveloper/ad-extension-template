<#
.SYNOPSIS
  The inner loop: build the implementation, let the running AD reload it, run a command.
  Needs the Debug shim deployed once (tools\deploy.ps1) and AD restarted after that.

.EXAMPLE
  .\tools\dev.ps1                  # build + reload + run Hello
  .\tools\dev.ps1 Hello "A=1|B=2"  # with parameters
  .\tools\dev.ps1 -Watch           # rebuild on every save (AD reloads automatically)
  .\tools\dev.ps1 Hello -Pid 12345 # when several AD instances run (tools\ad.ps1 status lists them)
#>
param(
    [Parameter(Position = 0)] [string] $Command = 'Hello',
    [Parameter(Position = 1)] [string] $Parameters = '',
    [switch] $Watch,
    [Alias('Pid')] [int] $ProcessId = 0
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$implProject = Join-Path $root 'BoardTools.Impl\BoardTools.Impl.csproj'
$client = Join-Path $PSScriptRoot 'hotreload.ps1'

if ($Watch) {
    # Each successful build rewrites the DLL; the shim's file watcher swaps it in.
    # Run from the project folder: 'dotnet watch --project' is forwarded to MSBuild, which rejects it (SDK 9).
    Push-Location (Split-Path -Parent $implProject)
    try { & dotnet watch build -c Debug --non-interactive }
    finally { Pop-Location }
    exit $LASTEXITCODE
}

# Builds quietly: prints only the errors and warnings (each once), and returns the built assembly's path.
function Invoke-Build([string] $project, [string] $configuration) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # Windows PowerShell turns native stderr into errors
    try { $output = @(& dotnet build $project -c $configuration -nologo -v m 2>&1 | ForEach-Object { "$_" }) }
    finally { $ErrorActionPreference = $previous }
    $failed = $LASTEXITCODE -ne 0
    $issues = $output | Where-Object { $_ -match ':\s+(error|warning)\s+[A-Z]+\d+' } |
        ForEach-Object { ($_ -replace '\s+\[[^\]]+\]\s*$', '').Trim() } | Select-Object -Unique
    foreach ($line in $issues) { Write-Host $line -ForegroundColor $(if ($line -match ':\s+error\s') { 'Red' } else { 'Yellow' }) }
    if ($failed -and -not $issues) { $output | ForEach-Object { Write-Host $_ } }   # an unexpected failure: show everything
    if ($failed) { return $null }
    $built = $output | Where-Object { $_ -match '^\s*\S+\s+->\s+(.+\.dll)\s*$' } | ForEach-Object { $Matches[1] } | Select-Object -Last 1
    if ($built) { return $built } else { return '' }
}

$sw = [Diagnostics.Stopwatch]::StartNew()
$built = Invoke-Build $implProject 'Debug'
if ($null -eq $built) { Write-Host 'Build failed - AD keeps running the previous generation.' -ForegroundColor Red; exit 1 }
Write-Host ("build: {0:n1}s" -f $sw.Elapsed.TotalSeconds) -ForegroundColor DarkGray

# The deployed shim watches the path it was built with (see its log). A build for another .NET version, e.g.
# after AltiumSystemDir changed, lands elsewhere and is never loaded: say so instead of running stale code.
$log = Join-Path $env:LOCALAPPDATA 'Altium.Designer.HotReload\BoardTools.log'
if ($built -and (Test-Path $log)) {
    $watched = Select-String -Path $log -Pattern '--- BoardTools hot reload host .*, module (.+), pipe ' |
        Select-Object -Last 1 | ForEach-Object { $_.Matches[0].Groups[1].Value }
    if ($watched -and -not [string]::Equals([IO.Path]::GetFullPath($watched), [IO.Path]::GetFullPath($built), 'OrdinalIgnoreCase')) {
        Write-Host "Warning: AD watches $watched, but this build wrote $built." -ForegroundColor Yellow
        Write-Host '         Check AltiumSystemDir in Directory.Build.props, then redeploy (tools\deploy.ps1) and restart AD.' -ForegroundColor Yellow
    }
}

# 'run' reloads first if the DLL changed, so no separate reload step is needed. In-process, with named parameters:
# a nested 'powershell -File' would mangle quotes and trailing backslashes in the parameters.
& $client -Name BoardTools -ProcessId $ProcessId run $Command $Parameters
exit $LASTEXITCODE
