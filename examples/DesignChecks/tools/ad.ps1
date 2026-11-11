<#
.SYNOPSIS
  Talks to the running Altium Designer (Debug DesignChecks shim) through the hot reload pipe.
  Thin wrapper over tools\hotreload.ps1 (from the Altium.Designer.HotReload package) with -Name DesignChecks.

.EXAMPLE
  .\tools\ad.ps1 status -gc
  .\tools\ad.ps1 run Hello
  .\tools\ad.ps1 -Pid 12345 reload      # when several AD instances run

  Verbs: ping | status [-gc] | reload | list | run <Command> [Key=Value|Key=Value]
  Exit code: 0 ok, 1 command failed, 2 usage / ambiguous instance, 3 AD not reachable.
#>
try {
    $global:LASTEXITCODE = 0
    & (Join-Path $PSScriptRoot 'hotreload.ps1') -Name DesignChecks @args
    exit $LASTEXITCODE
}
catch {
    # Parameter errors (e.g. -Pid abc) end up here rather than in an exit code.
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 2
}
