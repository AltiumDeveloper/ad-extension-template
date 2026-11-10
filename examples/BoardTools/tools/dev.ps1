<#
.SYNOPSIS
  The inner loop: build the implementation, let the running AD reload it, run a command.
  Needs the Debug shim deployed once (tools\deploy.ps1) and AD restarted after that.

.EXAMPLE
  .\tools\dev.ps1                  # build + reload + run Hello
  .\tools\dev.ps1 Hello "A=1|B=2"  # with parameters
  .\tools\dev.ps1 -Watch           # rebuild on every save (AD reloads automatically)
#>
param(
    [Parameter(Position = 0)] [string] $Command = 'Hello',
    [Parameter(Position = 1)] [string] $Parameters = '',
    [switch] $Watch
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$implProject = Join-Path $root 'BoardTools.Impl\BoardTools.Impl.csproj'
$ad = Join-Path $PSScriptRoot 'ad.ps1'

if ($Watch) {
    # Each successful build rewrites the DLL; the shim's file watcher swaps it in.
    # Run from the project folder: 'dotnet watch --project' is forwarded to MSBuild, which rejects it (SDK 9).
    Push-Location (Split-Path -Parent $implProject)
    try { & dotnet watch build -c Debug --non-interactive }
    finally { Pop-Location }
    exit $LASTEXITCODE
}

$sw = [Diagnostics.Stopwatch]::StartNew()
& dotnet build $implProject -c Debug -nologo -v q -clp:NoSummary
if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed - AD keeps running the previous generation.' -ForegroundColor Red; exit 1 }
Write-Host ("build: {0:n1}s" -f $sw.Elapsed.TotalSeconds) -ForegroundColor DarkGray

# 'run' reloads first if the DLL changed, so no separate reload step is needed.
& powershell -NoProfile -ExecutionPolicy Bypass -File $ad run $Command $Parameters
exit $LASTEXITCODE
