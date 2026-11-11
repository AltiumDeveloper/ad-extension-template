<#
.SYNOPSIS
  Client for Altium.Designer.HotReload: drives a running Altium Designer (Debug shim) over its dev pipe.

.DESCRIPTION
  Each Debug shim listens on \\.\pipe\<Name>.HotReload.<pid> (current user only). With one running
  instance the script picks it automatically; with several it lists them and asks for -Name / -Pid.

  -TimeoutMs limits connecting. -ReadTimeoutMs limits waiting for each line of the answer (AD's UI
  thread may be busy, e.g. with a modal dialog); it applies to every verb except 'run', which may
  legitimately take long and waits indefinitely unless -ReadTimeoutMs is given.

.EXAMPLE
  hotreload.ps1 ping
  hotreload.ps1 status -gc
  hotreload.ps1 reload
  hotreload.ps1 list
  hotreload.ps1 run MyCommand "Key=Value|Other=1"
  hotreload.ps1 -Name BoardTools -Pid 12345 status

  Verbs: ping | status [-gc] | reload | list | run <Command> [Key=Value|Key=Value] | help
  Exit code: 0 ok, 1 command failed, 2 usage / ambiguous instance, 3 AD not reachable (or no answer in time).
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $Request,
    [string] $Name = '*',
    [Alias('Pid')] [int] $ProcessId = 0,
    [int] $TimeoutMs = 3000,
    [int] $ReadTimeoutMs = 30000
)

$ErrorActionPreference = 'Stop'
$usage = 'Verbs: ping | status [-gc] | reload | list | run <command> [Key=Value|Key=Value] | help'
$line = ($Request -join ' ').Trim()
$verb = ($line -split '\s+', 2)[0].ToLowerInvariant()

# Usage errors are reported without looking for an instance.
if (-not $line -or $verb -eq 'help' -or $verb -eq '-?' -or $verb -eq '/?') {
    Write-Output $usage
    exit 0
}
if (@('ping', 'status', 'reload', 'list', 'run') -notcontains $verb) {
    [Console]::Error.WriteLine("Unknown verb '$verb'. $usage")
    exit 2
}
if ($verb -eq 'run' -and $line -notmatch '^\S+\s+\S') {
    [Console]::Error.WriteLine('usage: run <command> [Key=Value|Key=Value]')
    exit 2
}

# 'run' executes extension code, which may take long: no read timeout unless asked for.
$readTimeout = $ReadTimeoutMs
if ($verb -eq 'run' -and -not $PSBoundParameters.ContainsKey('ReadTimeoutMs')) { $readTimeout = -1 }
if ($readTimeout -eq 0) { $readTimeout = -1 }

# Discover instances: one pipe per extension per AD process. The search pattern helps on Windows
# PowerShell 5.1, where .NET Framework throws for the whole listing if any pipe name has characters
# illegal in paths; but some Windows versions (Server) don't filter the pipe namespace by pattern and
# return nothing, so fall back to the full listing (and its filtering below).
function Get-PipeNames {
    $names = @()
    try { $names = @([IO.Directory]::GetFiles('\\.\pipe\', '*.HotReload.*')) } catch { }
    if ($names.Count -eq 0) {
        try { $names = @([IO.Directory]::GetFiles('\\.\pipe\')) } catch { }
    }
    return $names
}
$instances = @(
    Get-PipeNames | ForEach-Object {
        if ($_ -match '\\pipe\\(?<name>.+)\.HotReload\.(?<pid>\d+)$') {
            [pscustomobject]@{ Name = $Matches.name; Pid = [int]$Matches.pid; Pipe = $_.Substring(9) }
        }
    } | Where-Object { $_.Name -like $Name -and ($ProcessId -eq 0 -or $_.Pid -eq $ProcessId) }
)

if ($instances.Count -eq 0) {
    $filter = "name '$Name'"
    if ($ProcessId) { $filter += ", pid $ProcessId" }
    [Console]::Error.WriteLine("No hot reload instance found ($filter). AD starts extensions on first use: run any of its commands once in this AD session. Otherwise, is AD running a Debug build of the extension? It may not be deployed yet: run tools\deploy.ps1, then restart AD.")
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

$pipe = $instances[0].Pipe
$client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipe, [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
try {
    $client.Connect($TimeoutMs)
} catch {
    [Console]::Error.WriteLine("Cannot connect to \\.\pipe\$($pipe): $($_.Exception.Message)")
    $client.Dispose()
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
    while ($true) {
        $pending = $reader.ReadLineAsync()
        if (-not $pending.Wait($readTimeout)) {
            [Console]::Error.WriteLine("No response from \\.\pipe\$pipe within $readTimeout ms: AD's UI thread is busy (a modal dialog or a long command?). Try again, or raise -ReadTimeoutMs.")
            exit 3
        }
        $out = $pending.Result
        if ($null -eq $out) { break }
        if ($out.Length -gt 0 -and $out[0] -eq [char]4) {
            $code = [int]$out.Substring(1)
            break
        }
        Write-Output $out
    }
    exit $code
} catch {
    [Console]::Error.WriteLine("Lost the connection to \\.\pipe\$($pipe): $($_.Exception.GetBaseException().Message)")
    exit 3
} finally {
    $client.Dispose()
}
