Write-Host "[WARN] run.ps1 is deprecated; use install.ps1 instead."

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& "$scriptDir\install.ps1" @args
