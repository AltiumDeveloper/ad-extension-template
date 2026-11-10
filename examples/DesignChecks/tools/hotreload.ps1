<#
.SYNOPSIS
  Client for Altium.Designer.HotReload: drives a running Altium Designer (Debug shim) over its dev pipe.

.DESCRIPTION
  Each Debug shim listens on \\.\pipe\<Name>.HotReload.<pid> (current user only). With one running
  instance the script picks it automatically; with several it lists them and asks for -Name / -Pid.

.EXAMPLE
  hotreload.ps1 ping
  hotreload.ps1 status -gc
  hotreload.ps1 reload
  hotreload.ps1 list
  hotreload.ps1 run MyCommand "Key=Value|Other=1"
  hotreload.ps1 -Name DesignChecks -Pid 12345 status

  Verbs: ping | status [-gc] | reload | list | run <Command> [Key=Value|Key=Value]
  Exit code: 0 ok, 1 command failed, 2 usage / ambiguous instance, 3 no instance reachable.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $Request,
    [string] $Name = '*',
    [Alias('Pid')] [int] $ProcessId = 0,
    [int] $TimeoutMs = 3000
)

$ErrorActionPreference = 'Stop'
$line = ($Request -join ' ').Trim()
if (-not $line) { $line = 'help' }

# Discover instances: one pipe per extension per AD process.
$instances = @(
    [IO.Directory]::GetFiles('\\.\pipe\') | ForEach-Object {
        if ($_ -match '\\pipe\\(?<name>.+)\.HotReload\.(?<pid>\d+)$') {
            [pscustomobject]@{ Name = $Matches.name; Pid = [int]$Matches.pid; Pipe = $_.Substring(9) }
        }
    } | Where-Object { $_.Name -like $Name -and ($ProcessId -eq 0 -or $_.Pid -eq $ProcessId) }
)

if ($instances.Count -eq 0) {
    [Console]::Error.WriteLine("No hot reload instance found (name '$Name'$(if ($ProcessId) { ", pid $ProcessId" })). AD starts extensions on first use: run any of its commands once in this AD session. Otherwise, is AD running a Debug build of the extension?")
    exit 3
}
if ($instances.Count -gt 1) {
    [Console]::Error.WriteLine('Several instances are running; choose one with -Name and/or -Pid:')
    $instances | ForEach-Object {
        $process = Get-Process -Id $_.Pid -ErrorAction SilentlyContinue
        $info = if ($process) { "  ($($process.ProcessName), started $($process.StartTime))" } else { '' }
        [Console]::Error.WriteLine("  -Name $($_.Name) -Pid $($_.Pid)$info")
    }
    exit 2
}

$client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $instances[0].Pipe, [System.IO.Pipes.PipeDirection]::InOut)
try {
    $client.Connect($TimeoutMs)
} catch {
    [Console]::Error.WriteLine("Cannot connect to \\.\pipe\$($instances[0].Pipe): $($_.Exception.Message)")
    exit 3
}

try {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $writer = New-Object System.IO.StreamWriter($client, $utf8)
    $writer.NewLine = "`n"
    $writer.AutoFlush = $true
    $reader = New-Object System.IO.StreamReader($client, $utf8)

    $writer.WriteLine($line)

    $code = 1
    while ($null -ne ($out = $reader.ReadLine())) {
        if ($out.Length -gt 0 -and $out[0] -eq [char]4) {
            $code = [int]$out.Substring(1)
            break
        }
        Write-Output $out
    }
    exit $code
} finally {
    $client.Dispose()
}
