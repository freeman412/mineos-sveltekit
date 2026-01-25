# MineOS Setup & Management Script for Windows
# Interactive setup and management using PowerShell

param(
    [switch]$Dev
)

$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $global:PSNativeCommandUseErrorActionPreference = $false
}

# Colors
function Write-Info { Write-Host "[INFO] $($args -join ' ')" -ForegroundColor Cyan }
function Write-Success { Write-Host "[OK] $($args -join ' ')" -ForegroundColor Green }
function Write-Warn { Write-Host "[WARN] $($args -join ' ')" -ForegroundColor Yellow }
function Write-Error-Custom { Write-Host "[ERR] $($args -join ' ')" -ForegroundColor Red }

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "================================" -ForegroundColor Green
    Write-Host $Message -ForegroundColor Green
    Write-Host "================================" -ForegroundColor Green
    Write-Host ""
}

function Test-CommandExists {
    param([string]$Command)
    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

function Invoke-DockerInfo {
    param([switch]$Plain, [int]$TimeoutSec = 3)
    $args = @("info")
    if (-not $Plain) { $args += @("--format", "{{.OSType}}|{{.ServerVersion}}") }

    $argString = ($args | ForEach-Object {
        if ($_ -match '\s') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '

    $outFile = New-TemporaryFile
    $errFile = New-TemporaryFile

    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $proc = Start-Process -FilePath "docker" `
            -ArgumentList $argString `
            -NoNewWindow `
            -PassThru `
            -RedirectStandardOutput $outFile `
            -RedirectStandardError $errFile

        $exited = $proc.WaitForExit($TimeoutSec * 1000)
        if (-not $exited) {
            try { $proc.Kill() } catch { }
            $outText = "Docker command timed out after ${TimeoutSec}s."
            return [pscustomobject]@{ Output = $outText; ExitCode = 124; TimedOut = $true }
        }

        $out = ""
        if (Test-Path $outFile) { $out = Get-Content $outFile -Raw }
        $err = ""
        if (Test-Path $errFile) { $err = Get-Content $errFile -Raw }

        return [pscustomobject]@{ Output = (($out + $err).Trim()); ExitCode = $proc.ExitCode; TimedOut = $false }
    } finally {
        $ErrorActionPreference = $oldEap
        Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
    }
}

function Get-DockerHostPipeName {
    if ([string]::IsNullOrWhiteSpace($env:DOCKER_HOST)) { return $null }
    if ($env:DOCKER_HOST -match '^npipe:////./pipe/([^/]+)') { return $Matches[1] }
    return $null
}

function Test-DockerPipe {
    param([string]$PipeName)
    if ([string]::IsNullOrWhiteSpace($PipeName)) { return $false }
    return Test-Path ("\\.\pipe\" + $PipeName)
}

function Set-DockerHostPipe {
    param([string]$PipeName, [switch]$Force)
    if (-not (Test-DockerPipe $PipeName)) { return $false }
    if (-not $Force -and -not [string]::IsNullOrWhiteSpace($env:DOCKER_HOST)) { return $false }

    $env:DOCKER_HOST = "npipe:////./pipe/$PipeName"
    Remove-Item Env:DOCKER_CONTEXT -ErrorAction SilentlyContinue
    $script:dockerPipe = $PipeName
    return $true
}

function Use-AvailableDockerHost {
    param([string[]]$PipePreference, [switch]$Force)
    foreach ($pipe in $PipePreference) {
        if (Set-DockerHostPipe -PipeName $pipe -Force:$Force) { return $pipe }
    }
    return $null
}

function Get-CurrentDockerEndpoint {
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $outLines = & docker context inspect --format "{{.Endpoints.docker.Host}}" 2>&1
        $outText = ($outLines | Out-String).Trim()
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldEap
    }

    if ($code -ne 0 -or [string]::IsNullOrWhiteSpace($outText)) { return $null }
    return $outText
}

function Get-DockerPipeFromEndpoint {
    param([string]$Endpoint)
    if ([string]::IsNullOrWhiteSpace($Endpoint)) { return $null }
    if ($Endpoint -match '^npipe:////./pipe/([^/]+)$') { return $Matches[1] }
    return $null
}

function Start-DockerDesktop {
    $started = $false

    if (Test-DockerDesktopRunning) { return $false }

    foreach ($svcName in @("com.docker.service", "docker")) {
        $svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue
        if ($svc) {
            if ($svc.Status -ne "Running") {
                try {
                    Start-Service -Name $svcName -ErrorAction SilentlyContinue
                    $started = $true
                } catch {
                }
            }
        }
    }

    $exe = Join-Path $env:ProgramFiles "Docker\Docker\Docker Desktop.exe"
    if (-not $started -and (Test-Path $exe)) {
        Start-Process $exe | Out-Null
        $started = $true
    }

    return $started
}

function Test-DockerServiceRunning {
    foreach ($svcName in @("com.docker.service", "docker")) {
        $svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue
        if ($svc -and $svc.Status -eq "Running") { return $true }
    }
    return $false
}

function Test-DockerDesktopRunning {
    $names = @("Docker Desktop", "com.docker.backend")
    foreach ($name in $names) {
        $p = Get-Process -Name $name -ErrorAction SilentlyContinue
        if ($p) { return $true }
    }
    return $false
}

function Clear-DockerClientOverrides {
    $cleared = $false
    if (-not [string]::IsNullOrWhiteSpace($env:DOCKER_HOST)) {
        Remove-Item Env:DOCKER_HOST -ErrorAction SilentlyContinue
        $cleared = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($env:DOCKER_CONTEXT)) {
        Remove-Item Env:DOCKER_CONTEXT -ErrorAction SilentlyContinue
        $cleared = $true
    }
    return $cleared
}

function Get-ExistingDockerPipes {
    $pipes = @()
    foreach ($pipe in @("dockerDesktopLinuxEngine", "docker_engine")) {
        if (Test-DockerPipe $pipe) { $pipes += $pipe }
    }
    return $pipes
}

function Get-DockerPipePreference {
    if (Test-DockerDesktopRunning) { return @("dockerDesktopLinuxEngine", "docker_engine") }
    if (Test-DockerServiceRunning) { return @("docker_engine", "dockerDesktopLinuxEngine") }
    return @("dockerDesktopLinuxEngine", "docker_engine")
}

function Wait-DockerReady {
    param([int]$TimeoutSec = 90)
    $start = Get-Date
    $result = $null
    $attempt = 0
    do {
        [void](Use-AvailableDockerHost -PipePreference (Get-DockerPipePreference) -Force)
        $result = Invoke-DockerInfo
        $attempt += 1
        if (Test-DockerInfoSuccess -Info $result) { return $result }
        if (($attempt % 5) -eq 0) { Write-Info "Still waiting for Docker engine..." }
        Start-Sleep -Seconds 2
    } while ((Get-Date) - $start).TotalSeconds -lt $TimeoutSec

    return $result
}

function Test-DockerInfoSuccess {
    param([Parameter(Mandatory = $true)]$Info)
    if ($Info.Output -match '^(linux|windows)\|') { return $true }
    if ($Info.Output -match 'OSType:\s*(linux|windows)') { return $true }
    if ($Info.ExitCode -ne 0) { return $false }
    if ($Info.PSObject.Properties.Name -contains "TimedOut" -and $Info.TimedOut) { return $false }
    if ($Info.Output -match '(?i)error during connect|cannot connect|is the docker daemon running|connection refused|dial tcp') { return $false }
    return $true
}

function Get-DockerOsType {
    param([string]$InfoOutput)
    if ($InfoOutput -match '^(?<os>linux|windows)\|') { return $Matches["os"] }

    $plain = Invoke-DockerInfo -Plain
    if ($plain.ExitCode -eq 0 -and $plain.Output -match 'OSType:\s*(\w+)') {
        return $Matches[1].ToLowerInvariant()
    }

    return $null
}

function Try-SwitchToLinuxEngine {
    $cli = Join-Path $env:ProgramFiles "Docker\Docker\DockerCli.exe"
    if (-not (Test-Path $cli)) { return $false }

    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $cli -SwitchLinuxEngine 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { return $true }

        & $cli -SwitchDaemon 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { return $true }
    } finally {
        $ErrorActionPreference = $oldEap
    }

    return $false
}

function Ensure-DockerEngine {
    Write-Info "Checking Docker engine..."

    $pipeName = Get-DockerHostPipeName
    if ($pipeName -and -not (Test-DockerPipe $pipeName)) {
        Write-Warn "DOCKER_HOST points to missing pipe '$pipeName'."
        Remove-Item Env:DOCKER_HOST -ErrorAction SilentlyContinue
    }

    $info = Invoke-DockerInfo
    if (-not (Test-DockerInfoSuccess -Info $info)) {
        if (Clear-DockerClientOverrides) {
            $info = Invoke-DockerInfo
        }

        if ([string]::IsNullOrWhiteSpace($env:DOCKER_HOST)) {
            $endpoint = Get-CurrentDockerEndpoint
            $endpointPipe = Get-DockerPipeFromEndpoint -Endpoint $endpoint
            if ($endpointPipe -and (Set-DockerHostPipe -PipeName $endpointPipe -Force)) {
                Write-Info "Using Docker host pipe '$endpointPipe'."
            } else {
                $selected = Use-AvailableDockerHost -PipePreference (Get-DockerPipePreference) -Force
                if ($selected) { Write-Info "Using Docker host pipe '$selected'." }
            }
        }

        $info = Invoke-DockerInfo
    }

    if (-not (Test-DockerInfoSuccess -Info $info)) {
        $desktopRunning = Test-DockerDesktopRunning
        $serviceRunning = Test-DockerServiceRunning
        $started = $false

        if (-not $desktopRunning) {
            $started = Start-DockerDesktop
        }

        if ($started -or $desktopRunning -or $serviceRunning) {
            Write-Info "Waiting for Docker engine..."
            Start-Sleep -Seconds 2
            $info = Wait-DockerReady -TimeoutSec 45
        }
    }

    if (-not (Test-DockerInfoSuccess -Info $info)) {
        $running = (Test-DockerServiceRunning) -or (Test-DockerDesktopRunning)
        $pipes = Get-ExistingDockerPipes
        Write-Error-Custom "Docker engine not reachable."
        if ($info.Output) { Write-Host $info.Output }
        if ($running) { Write-Warn "Docker services are running but the engine is not responding." }
        if ($pipes.Count -gt 0) { Write-Warn ("Detected Docker pipe(s): " + ($pipes -join ", ")) }
        if ($env:DOCKER_HOST) { Write-Warn "DOCKER_HOST is set to $env:DOCKER_HOST" }
        Write-Info "Start Docker Desktop and ensure Linux containers are enabled."
        Write-Info "WSL is not required; Docker Desktop's Hyper-V backend works without WSL."
        exit 1
    }

    if ([string]::IsNullOrWhiteSpace($env:DOCKER_HOST)) {
        $endpoint = Get-CurrentDockerEndpoint
        $endpointPipe = Get-DockerPipeFromEndpoint -Endpoint $endpoint
        if ($endpointPipe -and (Set-DockerHostPipe -PipeName $endpointPipe)) {
            Write-Info "Using Docker host pipe '$endpointPipe'."
        }
    }

    $osType = Get-DockerOsType -InfoOutput $info.Output
    if ($osType -and $osType -ne "linux") {
        Write-Warn "Docker is running Windows containers; Linux containers are required."
        if (Try-SwitchToLinuxEngine) {
            Write-Info "Switching Docker to Linux containers..."
            $info = Wait-DockerReady -TimeoutSec 90
            $osType = Get-DockerOsType -InfoOutput $info.Output
        }
    }

    if ($osType -and $osType -ne "linux") {
        Write-Error-Custom "Linux containers are required."
        Write-Info "Switch Docker Desktop to Linux containers and rerun this script."
        Write-Info "WSL is not required; Docker Desktop's Hyper-V backend works without WSL."
        exit 1
    }

    if (-not $osType) {
        Write-Warn "Unable to detect Docker OS type; proceeding anyway."
    }

    Write-Success "Docker engine ready"
}

# Detect Docker Compose
function Set-ComposeCommand {
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        $oldEap = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $outLines = & docker compose version 2>&1
            $outText = ($outLines | Out-String).Trim()
        } finally {
            $ErrorActionPreference = $oldEap
        }
        if ($outText -match "Docker Compose version" -or $outText -match '\d+\.\d+\.\d+') {
            $script:composeExe = "docker"
            $script:composeBaseArgs = @("compose")
            $script:composeCmdText = "docker compose"
            return
        }
    }

    if (Get-Command docker-compose -ErrorAction SilentlyContinue) {
        $oldEap = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $outLines = & docker-compose version 2>&1
            $outText = ($outLines | Out-String).Trim()
        } finally {
            $ErrorActionPreference = $oldEap
        }
        if ($outText -match "Docker Compose version" -or $outText -match '\d+\.\d+\.\d+') {
            $script:composeExe = "docker-compose"
            $script:composeBaseArgs = @()
            $script:composeCmdText = "docker-compose"
            return
        }
    }

    throw "Docker Compose not found."
}

function Invoke-Compose {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args,
        [switch]$StreamOutput
    )

    if (-not $script:composeExe) { Set-ComposeCommand }

    $allArgs = @()
    if ($script:composeBaseArgs) { $allArgs += $script:composeBaseArgs }
    if ($Args) { $allArgs += $Args }

    $argString = ($allArgs | ForEach-Object {
        if ($_ -match '\s') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '

    $outFile = New-TemporaryFile
    $errFile = New-TemporaryFile

    try {
        $proc = Start-Process -FilePath $script:composeExe `
            -ArgumentList $argString `
            -NoNewWindow `
            -PassThru `
            -RedirectStandardOutput $outFile `
            -RedirectStandardError $errFile
        if ($StreamOutput) {
            $outOffset = 0L
            $errOffset = 0L
            while (-not $proc.HasExited) {
                Start-Sleep -Milliseconds 200
                $outOffset = Write-NewFileContent -Path $outFile -Offset $outOffset
                $errOffset = Write-NewFileContent -Path $errFile -Offset $errOffset
            }
            $outOffset = Write-NewFileContent -Path $outFile -Offset $outOffset
            $errOffset = Write-NewFileContent -Path $errFile -Offset $errOffset
        }
        $proc.WaitForExit()

        $out = ""
        if (Test-Path $outFile) { $out = Get-Content $outFile -Raw }
        $err = ""
        if (Test-Path $errFile) { $err = Get-Content $errFile -Raw }

        return [pscustomobject]@{ Output = ($out + $err); ExitCode = $proc.ExitCode }
    } finally {
        Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
    }
}

function Test-ComposeBuildSuccessFromOutput {
    param([string]$Output)
    if ([string]::IsNullOrWhiteSpace($Output)) { return $false }
    if ($Output -match '(?i)\b(error|failed|exception|panic)\b') { return $false }
    if ($Output -match '(?im)^ *Service\s+\S+\s+Built') { return $true }
    if ($Output -match '(?im)^#\d+\s+DONE') { return $true }
    return $false
}

function Write-NewFileContent {
    param([string]$Path, [long]$Offset)
    if (-not (Test-Path $Path)) { return $Offset }
    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        if ($Offset -gt $fs.Length) { $Offset = $fs.Length }
        [void]$fs.Seek($Offset, [System.IO.SeekOrigin]::Begin)
        $reader = New-Object System.IO.StreamReader($fs)
        try {
            $text = $reader.ReadToEnd()
            $newOffset = $fs.Position
        } finally {
            $reader.Dispose()
        }
    } finally {
        $fs.Dispose()
    }
    if (-not [string]::IsNullOrEmpty($text)) { Write-Host -NoNewline $text }
    return $newOffset
}

function Get-EnvValue {
    param([string]$Key)
    if (-not (Test-Path ".env")) { return $null }
    $pattern = "^{0}=" -f [regex]::Escape($Key)
    $line = Get-Content ".env" | Where-Object { $_ -match $pattern } | Select-Object -First 1
    if (-not $line) { return $null }
    $parts = $line -split '=', 2
    if ($parts.Length -eq 2) { return $parts[1] }
    return $null
}

function Load-ExistingConfig {
    $script:adminUser = Get-EnvValue "Auth__SeedUsername"
    if ([string]::IsNullOrWhiteSpace($script:adminUser)) { $script:adminUser = "admin" }
    $script:adminPass = Get-EnvValue "Auth__SeedPassword"
    $script:apiKey = Get-EnvValue "ApiKey__SeedKey"
    $script:apiPort = Get-EnvValue "API_PORT"
    if ([string]::IsNullOrWhiteSpace($script:apiPort)) { $script:apiPort = "5078" }
    $script:webPort = Get-EnvValue "WEB_PORT"
    if ([string]::IsNullOrWhiteSpace($script:webPort)) { $script:webPort = "3000" }
    $script:mcPublicHost = Get-EnvValue "PUBLIC_MINECRAFT_HOST"
    if ([string]::IsNullOrWhiteSpace($script:mcPublicHost)) { $script:mcPublicHost = "localhost" }
    $script:bodySizeLimit = Get-EnvValue "BODY_SIZE_LIMIT"
    if ([string]::IsNullOrWhiteSpace($script:bodySizeLimit)) { $script:bodySizeLimit = "Infinity" }
    $script:mcPortRange = Get-EnvValue "MC_PORT_RANGE"
    if ([string]::IsNullOrWhiteSpace($script:mcPortRange)) { $script:mcPortRange = "25565-25570" }
    $script:baseDir = Get-EnvValue "HOST_BASE_DIRECTORY"
    if ([string]::IsNullOrWhiteSpace($script:baseDir)) { $script:baseDir = Get-EnvValue "Host__BaseDirectory" }
    if ([string]::IsNullOrWhiteSpace($script:baseDir)) { $script:baseDir = ".\\minecraft" }
    $script:dataDir = Get-EnvValue "Data__Directory"
    if ([string]::IsNullOrWhiteSpace($script:dataDir)) { $script:dataDir = ".\\data" }
}

function Assert-PortNumber {
    param([string]$Value, [string]$Name)
    if (-not ($Value -match '^\d+$')) { throw "$Name must be a number." }
    $n = [int]$Value
    if ($n -lt 1 -or $n -gt 65535) { throw "$Name must be between 1 and 65535." }
    return $n
}

function Assert-PortRange {
    param([string]$Value, [string]$Name)
    if ($Value -match '^\d+$') {
        [void](Assert-PortNumber -Value $Value -Name $Name)
        return $Value
    }
    if (-not ($Value -match '^(\d+)\-(\d+)$')) { throw "$Name must be like 25565-25570." }
    $start = [int]$Matches[1]
    $end   = [int]$Matches[2]
    if ($start -lt 1 -or $start -gt 65535 -or $end -lt 1 -or $end -gt 65535) { throw "$Name ports must be between 1 and 65535." }
    if ($end -lt $start) { throw "$Name end port must be >= start port." }
    return "$start-$end"
}

function Set-EnvValue {
    param([string]$Key, [string]$Value)
    if (-not (Test-Path ".env")) { return }
    $pattern = "^{0}=" -f [regex]::Escape($Key)
    $lines = Get-Content ".env"
    $found = $false
    $newLines = foreach ($line in $lines) {
        if ($line -match $pattern) {
            $found = $true
            "$Key=$Value"
        } else {
            $line
        }
    }
    if (-not $found) { $newLines += "$Key=$Value" }
    $newLines | Set-Content ".env" -Encoding utf8
}

function Set-EnvFileValue {
    param([string]$Path, [string]$Key, [string]$Value)
    if (-not (Test-Path $Path)) {
        "$Key=$Value" | Out-File -FilePath $Path -Encoding utf8
        return
    }
    $pattern = "^{0}=" -f [regex]::Escape($Key)
    $lines = Get-Content $Path
    $found = $false
    $newLines = foreach ($line in $lines) {
        if ($line -match $pattern) {
            $found = $true
            "$Key=$Value"
        } else {
            $line
        }
    }
    if (-not $found) { $newLines += "$Key=$Value" }
    $newLines | Set-Content $Path -Encoding utf8
}

function Write-WebDevEnv {
    $webEnvPath = Join-Path "apps\web" ".env.local"
    if (-not (Test-Path "apps\web")) { New-Item -ItemType Directory -Force -Path "apps\web" | Out-Null }

    $apiPortValue = $script:apiPort
    if ([string]::IsNullOrWhiteSpace($apiPortValue)) { $apiPortValue = "5078" }
    $apiKeyValue = $script:apiKey
    if ([string]::IsNullOrWhiteSpace($apiKeyValue)) { $apiKeyValue = Get-EnvValue "ApiKey__SeedKey" }
    $mcHostValue = $script:mcPublicHost
    if ([string]::IsNullOrWhiteSpace($mcHostValue)) { $mcHostValue = Get-EnvValue "PUBLIC_MINECRAFT_HOST" }
    if ([string]::IsNullOrWhiteSpace($mcHostValue)) { $mcHostValue = "localhost" }

    Set-EnvFileValue -Path $webEnvPath -Key "PUBLIC_API_BASE_URL" -Value "http://localhost:$apiPortValue"
    Set-EnvFileValue -Path $webEnvPath -Key "PRIVATE_API_BASE_URL" -Value "http://localhost:$apiPortValue"
    if ($apiKeyValue) { Set-EnvFileValue -Path $webEnvPath -Key "PRIVATE_API_KEY" -Value $apiKeyValue }
    Set-EnvFileValue -Path $webEnvPath -Key "ORIGIN" -Value "http://localhost:5174"
    Set-EnvFileValue -Path $webEnvPath -Key "PUBLIC_MINECRAFT_HOST" -Value $mcHostValue

    Write-Success "Updated apps\\web\\.env.local for dev"
}

function Get-EffectiveMcPortRange {
    if (-not [string]::IsNullOrWhiteSpace($script:mcPortRange)) { return $script:mcPortRange }
    $val = Get-EnvValue "MC_PORT_RANGE"
    if ([string]::IsNullOrWhiteSpace($val)) { return "25565-25570" }
    return $val
}

function Get-EffectiveMcExtraPorts {
    if (-not [string]::IsNullOrWhiteSpace($script:mcExtraPorts)) { return $script:mcExtraPorts }
    $val = Get-EnvValue "MC_EXTRA_PORTS"
    if ([string]::IsNullOrWhiteSpace($val)) { return "" }
    return $val
}

function Get-PortsFromRange {
    param([string]$Range)
    if ([string]::IsNullOrWhiteSpace($Range)) { return @() }
    if ($Range -match '^\d+$') { return @([int]$Range) }
    if ($Range -match '^(\d+)\-(\d+)$') {
        $start = [int]$Matches[1]
        $end = [int]$Matches[2]
        if ($end -lt $start) { return @() }
        return @($start..$end)
    }
    return @()
}

function Get-PortsFromList {
    param([string]$List)
    if ([string]::IsNullOrWhiteSpace($List)) { return @() }
    $parts = $List -split '[,\s;]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $ports = @()
    foreach ($p in $parts) {
        if ($p -match '^\d+$') { $ports += [int]$p }
    }
    return $ports
}

function Get-ExcludedPortRanges {
    param([string]$Protocol)
    $ranges = @()
    if ([string]::IsNullOrWhiteSpace($Protocol)) { return $ranges }

    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $outLines = & netsh int ipv4 show excludedportrange protocol=$Protocol 2>&1
        $outText = ($outLines | Out-String)
    } finally {
        $ErrorActionPreference = $oldEap
    }

    if ([string]::IsNullOrWhiteSpace($outText)) { return $ranges }

    foreach ($line in $outText -split "`r?`n") {
        if ($line -match '^\s*(\d+)\s+(\d+)\s+(\*)?') {
            $ranges += [pscustomobject]@{
                Start = [int]$Matches[1]
                End = [int]$Matches[2]
            }
        }
    }

    return $ranges
}

function Test-PortExcluded {
    param([int]$Port, [string]$Protocol)
    $ranges = Get-ExcludedPortRanges -Protocol $Protocol
    foreach ($r in $ranges) {
        if ($Port -ge $r.Start -and $Port -le $r.End) { return $true }
    }
    return $false
}

function Test-TcpPortAvailable {
    param([int]$Port)
    if (Test-PortExcluded -Port $Port -Protocol "tcp") { return $false }
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $Port)
        $listener.Start()
        $listener.Stop()
        return $true
    } catch {
        return $false
    }
}

function Test-UdpPortAvailable {
    param([int]$Port)
    if (Test-PortExcluded -Port $Port -Protocol "udp") { return $false }
    try {
        $client = New-Object System.Net.Sockets.UdpClient($Port)
        $client.Close()
        return $true
    } catch {
        return $false
    }
}

function Test-PortAvailable {
    param([int]$Port)
    return (Test-TcpPortAvailable -Port $Port) -and (Test-UdpPortAvailable -Port $Port)
}

function Find-FreePortRange {
    param(
        [int]$Count,
        [int]$Start = 25565,
        [int]$End = 25700
    )
    if ($Count -lt 1) { return $null }
    for ($s = $Start; $s + $Count - 1 -le $End; $s++) {
        $ok = $true
        for ($p = $s; $p -lt ($s + $Count); $p++) {
            if (-not (Test-PortAvailable -Port $p)) { $ok = $false; break }
        }
        if ($ok) { return "$s-$($s + $Count - 1)" }
    }
    return $null
}

function Ensure-MinecraftPortsAvailable {
    $range = Get-EffectiveMcPortRange
    $extra = Get-EffectiveMcExtraPorts

    try {
        $range = Assert-PortRange -Value $range -Name "Minecraft port range"
    } catch {
        Write-Error-Custom $_
        return $false
    }

    $ports = @()
    $ports += Get-PortsFromRange -Range $range
    $ports += Get-PortsFromList -List $extra
    $ports = $ports | Sort-Object -Unique

    $blocked = @()
    $excludedTcp = @()
    $excludedUdp = @()
    foreach ($p in $ports) {
        $isTcpExcluded = Test-PortExcluded -Port $p -Protocol "tcp"
        $isUdpExcluded = Test-PortExcluded -Port $p -Protocol "udp"
        if ($isTcpExcluded) { $excludedTcp += $p }
        if ($isUdpExcluded) { $excludedUdp += $p }
        if (-not (Test-PortAvailable -Port $p)) { $blocked += $p }
    }

    if ($blocked.Count -eq 0) { return $true }

    if ($excludedTcp.Count -gt 0 -or $excludedUdp.Count -gt 0) {
        $parts = @()
        if ($excludedTcp.Count -gt 0) { $parts += "TCP excluded: $($excludedTcp -join ", ")" }
        if ($excludedUdp.Count -gt 0) { $parts += "UDP excluded: $($excludedUdp -join ", ")" }
        Write-Error-Custom ("Minecraft ports are blocked by Windows reserved ranges. " + ($parts -join " | "))
    } else {
        Write-Error-Custom ("Minecraft ports in use: " + ($blocked -join ", "))
    }
    $count = (Get-PortsFromRange -Range $range).Count
    $suggest = Find-FreePortRange -Count $count
    if ($suggest) {
        Write-Warn "Suggested free range: $suggest"
    }

    $choice = Read-Host "Enter a new Minecraft port range (or press Enter to cancel)"
    if ([string]::IsNullOrWhiteSpace($choice)) { return $false }

    try {
        $choice = Assert-PortRange -Value $choice -Name "Minecraft port range"
    } catch {
        Write-Error-Custom $_
        return $false
    }

    $script:mcPortRange = $choice
    if (Test-Path ".env") { Set-EnvValue -Key "MC_PORT_RANGE" -Value $choice }
    Write-Info "Updated Minecraft port range to $choice"

    return $true
}

function Test-ComposeServicesRunning {
    param([string[]]$ContainerNames)

    if (-not $script:composeExe) { Set-ComposeCommand }

    if ($ContainerNames -and (Test-DockerContainersRunning -ContainerNames $ContainerNames)) { return $true }

    $r = Invoke-Compose -Args @("ps", "--status", "running", "-q")
    $lines = $r.Output -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if (-not $ContainerNames -and $lines.Count -gt 0) { return $true }
    if ($ContainerNames -and $lines.Count -ge $ContainerNames.Count) { return $true }

    $r2 = Invoke-Compose -Args @("ps")
    if ([string]::IsNullOrWhiteSpace($r2.Output)) { return $false }
    if (-not $ContainerNames) { return $false }

    foreach ($name in $ContainerNames) {
        $pattern = "(?im)^.*{0}.*(Up|running|healthy)" -f [regex]::Escape($name)
        if ($r2.Output -notmatch $pattern) { return $false }
    }

    return $true
}

function Test-DockerContainersRunning {
    param([string[]]$ContainerNames)
    if (-not $ContainerNames -or $ContainerNames.Count -eq 0) { return $false }

    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $outLines = & docker ps --format "{{.Names}}" 2>&1
        $outText = ($outLines | Out-String).Trim()
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldEap
    }

    if ($code -ne 0 -or [string]::IsNullOrWhiteSpace($outText)) { return $false }
    $running = $outText -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($name in $ContainerNames) {
        if ($running -notcontains $name) { return $false }
    }

    return $true
}
function Test-Dependencies {
    Write-Info "Checking dependencies..."
    $allOk = $true

    if (Test-CommandExists "docker") {
        $dockerOut = docker --version 2>&1
        $dockerVersion = ([regex]'\d+\.\d+\.\d+').Match([string]$dockerOut).Value
        Write-Success "Docker $dockerVersion"
    } else {
        Write-Error-Custom "Docker is not installed"
        Write-Info "Please install Docker Desktop: https://docs.docker.com/desktop/install/windows-install/"
        $allOk = $false
    }

    try {
        Set-ComposeCommand
        Write-Success "Docker Compose found"
    } catch {
        Write-Error-Custom "Docker Compose is not installed"
        $allOk = $false
    }

    if (-not $allOk) {
        Write-Error-Custom "Required dependencies are missing."
        exit 1
    }

    Ensure-DockerEngine
}

function Get-ServiceStatus {
    $r = Invoke-Compose -Args @("ps", "--format", "json")
    if ($r.ExitCode -ne 0) { return @() }
    try {
        $services = $r.Output | ConvertFrom-Json
        return $services
    } catch {
        return @()
    }
}

function Show-Status {
    Write-Header "Service Status"

    if (-not (Test-Path ".env")) {
        Write-Warn "MineOS is not installed. Run fresh install first."
        return
    }

    Load-ExistingConfig
    $r = Invoke-Compose -Args @("ps")
    Write-Host $r.Output

    Write-Host ""
    Write-Host "URLs (when running):"
    Write-Host "  Web UI:   http://localhost:$webPort" -ForegroundColor Cyan
    Write-Host "  API:      http://localhost:$apiPort" -ForegroundColor Cyan
}

function Start-ConfigWizard {
    Write-Header "Configuration Wizard"

    $script:dbType = "sqlite"
    $script:dbConnection = "Data Source=/app/data/mineos.db"

    $script:adminUser = Read-Host "Admin username (default: admin)"
    if ([string]::IsNullOrWhiteSpace($script:adminUser)) { $script:adminUser = "admin" }

    do {
        $adminPassSecure = Read-Host "Admin password" -AsSecureString
        $script:adminPass = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($adminPassSecure)
        )
        if ([string]::IsNullOrWhiteSpace($script:adminPass)) { Write-Error-Custom "Password cannot be empty" }
    } while ([string]::IsNullOrWhiteSpace($script:adminPass))

    $script:baseDir = Read-Host "Local storage directory for Minecraft servers (relative, default: .\\minecraft)"
    if ([string]::IsNullOrWhiteSpace($script:baseDir)) { $script:baseDir = ".\\minecraft" }

    $script:dataDir = Read-Host "Database directory (relative, default: .\\data)"
    if ([string]::IsNullOrWhiteSpace($script:dataDir)) { $script:dataDir = ".\\data" }

    $script:apiPort = Read-Host "API port (default: 5078)"
    if ([string]::IsNullOrWhiteSpace($script:apiPort)) { $script:apiPort = "5078" }
    [void](Assert-PortNumber -Value $script:apiPort -Name "API port")

    $script:webPort = Read-Host "Web UI port (default: 3000)"
    if ([string]::IsNullOrWhiteSpace($script:webPort)) { $script:webPort = "3000" }
    [void](Assert-PortNumber -Value $script:webPort -Name "Web UI port")

    $script:mcPublicHost = Read-Host "Public Minecraft host (default: localhost)"
    if ([string]::IsNullOrWhiteSpace($script:mcPublicHost)) { $script:mcPublicHost = "localhost" }

    $script:bodySizeLimit = Read-Host "Web UI upload body size limit (default: Infinity)"
    if ([string]::IsNullOrWhiteSpace($script:bodySizeLimit)) { $script:bodySizeLimit = "Infinity" }

    $script:mcPortRange = Read-Host "Minecraft server port range (default: 25565-25570)"
    if ([string]::IsNullOrWhiteSpace($script:mcPortRange)) { $script:mcPortRange = "25565-25570" }
    $script:mcPortRange = Assert-PortRange -Value $script:mcPortRange -Name "Minecraft port range"

    $script:mcExtraPorts = ""
    $script:curseforgeKey = Read-Host "CurseForge API key (optional, press Enter to skip)"
    $script:discordWebhook = Read-Host "Discord webhook URL (optional, press Enter to skip)"

    $script:jwtSecret = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
    $script:apiKey = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
}

function New-EnvFile {
    $envContent = @"
# Database Configuration
DB_TYPE=$dbType
ConnectionStrings__DefaultConnection=$dbConnection

# Authentication
Auth__SeedUsername=$adminUser
Auth__SeedPassword=$adminPass
Auth__JwtSecret=$jwtSecret
Auth__JwtIssuer=mineos
Auth__JwtAudience=mineos
Auth__JwtExpiryHours=24

# API Configuration
ApiKey__SeedKey=$apiKey

# Host Configuration
HOST_BASE_DIRECTORY=$($baseDir -replace '\\', '/')
Host__BaseDirectory=/var/games/minecraft
Data__Directory=$($dataDir -replace '\\', '/')
Host__ServersPathSegment=servers
Host__ProfilesPathSegment=profiles
Host__BackupsPathSegment=backups
Host__ArchivesPathSegment=archives
Host__ImportsPathSegment=imports
Host__OwnerUid=1000
Host__OwnerGid=1000

# Optional Integrations
$(if ($curseforgeKey) { "CurseForge__ApiKey=$curseforgeKey" } else { "# CurseForge__ApiKey=" })
$(if ($discordWebhook) { "Discord__WebhookUrl=$discordWebhook" } else { "# Discord__WebhookUrl=" })

# Ports
API_PORT=$apiPort
WEB_PORT=$webPort
MC_PORT_RANGE=$mcPortRange
MC_EXTRA_PORTS=$mcExtraPorts

# Minecraft Server Address
PUBLIC_MINECRAFT_HOST=$mcPublicHost

# Web UI Upload Limits
BODY_SIZE_LIMIT=$bodySizeLimit

# Logging
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
"@

    $envContent | Out-File -FilePath ".env" -Encoding utf8
    Write-Success "Created .env file"
}

function New-Directories {
    New-Item -ItemType Directory -Force -Path "$baseDir\servers" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\profiles" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\backups" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\archives" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\imports" | Out-Null
    New-Item -ItemType Directory -Force -Path "$dataDir" | Out-Null
    Write-Success "Created directories"
}

function Start-Services {
    param([switch]$Rebuild)

    Ensure-DockerEngine
    if (-not (Ensure-MinecraftPortsAvailable)) {
        Write-Error-Custom "Cannot start services until Minecraft ports are available."
        exit 1
    }
    Write-Info "Starting services..."

    if ($Rebuild) {
        Write-Info "Building Docker images (this may take a few minutes)..."
        $env:PUBLIC_BUILD_ID = (Get-Date -Format "yyyyMMddHHmmss")
        Write-Info "Build ID: $env:PUBLIC_BUILD_ID"
        if (-not $script:composeExe) { Set-ComposeCommand }
        $buildArgs = @("build", "--no-cache")
        if ($script:composeExe -eq "docker") { $buildArgs = @("build", "--no-cache", "--progress", "plain") }
        $build = Invoke-Compose -Args $buildArgs -StreamOutput
        if ($build.ExitCode -ne 0 -and $script:composeExe -eq "docker") {
            Write-Warn "Build failed with --progress; retrying without it..."
            $build = Invoke-Compose -Args @("build", "--no-cache") -StreamOutput
        }
        if ($build.ExitCode -ne 0 -and (Test-ComposeBuildSuccessFromOutput -Output $build.Output)) {
            Write-Warn "Build returned a non-zero exit code but output looks successful; continuing..."
        } elseif ($build.ExitCode -ne 0) {
            Write-Error-Custom "Build failed"
            if ($build.Output) { Write-Host $build.Output }
            exit 1
        }
        $r = Invoke-Compose -Args @("up", "-d", "--force-recreate")
    } else {
        $r = Invoke-Compose -Args @("up", "-d")
    }

    if ($r.ExitCode -ne 0) {
        $running = Test-ComposeServicesRunning -ContainerNames @("mineos-api", "mineos-web")
        if ($running) {
            Write-Success "Services started!"
        } else {
            Write-Error-Custom "Failed to start services"
            Write-Host $r.Output
            exit 1
        }
    }

    Write-Info "Waiting for services to be ready..."
    Start-Sleep -Seconds 5

    Write-Success "Services started!"
}

function Start-DevMode {
    param([switch]$Pause)

    Write-Header "Dev Mode"

    if (-not (Test-Path ".env")) {
        Write-Error-Custom "No installation found. Run fresh install first."
        return
    }

    Test-Dependencies
    Load-ExistingConfig

    if (-not (Ensure-MinecraftPortsAvailable)) {
        Write-Error-Custom "Cannot start services until Minecraft ports are available."
        exit 1
    }

    Write-Info "Stopping web service (if running)..."
    [void](Invoke-Compose -Args @("stop", "web"))

    Write-Info "Starting API service..."
    $r = Invoke-Compose -Args @("up", "-d", "api")
    if ($r.ExitCode -ne 0) {
        $running = Test-ComposeServicesRunning -ContainerNames @("mineos-api")
        if (-not $running) {
            Write-Error-Custom "Failed to start API service"
            if ($r.Output) { Write-Host $r.Output }
            exit 1
        }
        Write-Warn "Compose returned a non-zero exit code but the API container is running."
    }
    Write-Success "API service started"

    Write-WebDevEnv

    Write-Host ""
    Write-Host "Web dev server:" -ForegroundColor Cyan
    Write-Host "  cd apps\\web"
    Write-Host "  npm install"
    Write-Host "  npm run dev -- --host 0.0.0.0 --port 5174"
    Write-Host ""
    Write-Host "Web UI (dev): http://localhost:5174" -ForegroundColor Cyan
    Write-Host "API:          http://localhost:$apiPort" -ForegroundColor Cyan

    if ($Pause) { Read-Host "Press Enter to continue" }
}

function Start-WebDevContainer {
    Write-Header "Web Dev Container"

    if (-not (Test-Path ".env")) {
        Write-Error-Custom "No installation found. Run fresh install first."
        return
    }

    Test-Dependencies
    Load-ExistingConfig

    $devOrigin = Get-EnvValue "WEB_ORIGIN_DEV"
    if ([string]::IsNullOrWhiteSpace($devOrigin)) { $devOrigin = "http://localhost:5174" }
    $publicApi = Get-EnvValue "PUBLIC_API_BASE_URL"
    if ([string]::IsNullOrWhiteSpace($publicApi)) { $publicApi = "http://localhost:$apiPort" }

    $devOriginInput = Read-Host "Dev web origin (default: $devOrigin)"
    if ([string]::IsNullOrWhiteSpace($devOriginInput)) { $devOriginInput = $devOrigin }

    $publicApiInput = Read-Host "Public API base URL (default: $publicApi)"
    if ([string]::IsNullOrWhiteSpace($publicApiInput)) { $publicApiInput = $publicApi }

    Set-EnvValue -Key "WEB_ORIGIN_DEV" -Value $devOriginInput
    Set-EnvValue -Key "PUBLIC_API_BASE_URL" -Value $publicApiInput

    Write-Info "Stopping web service (if running)..."
    [void](Invoke-Compose -Args @("stop", "web"))

    Write-Info "Starting API service..."
    $r = Invoke-Compose -Args @("up", "-d", "api")
    if ($r.ExitCode -ne 0) {
        $running = Test-ComposeServicesRunning -ContainerNames @("mineos-api")
        if (-not $running) {
            Write-Error-Custom "Failed to start API service"
            if ($r.Output) { Write-Host $r.Output }
            exit 1
        }
        Write-Warn "Compose returned a non-zero exit code but the API container is running."
    }

    Write-Info "Starting web dev container..."
    $dev = Invoke-Compose -Args @(
        "-f", "docker-compose.yml",
        "-f", "docker-compose.dev.yml",
        "up", "-d", "web-dev"
    )
    if ($dev.ExitCode -ne 0) {
        Write-Error-Custom "Failed to start web dev container"
        if ($dev.Output) { Write-Host $dev.Output }
        exit 1
    }

    Write-Success "Web dev container started"
    Write-Host "Web UI (dev): $devOriginInput" -ForegroundColor Cyan
    Write-Host "API:          http://localhost:$apiPort" -ForegroundColor Cyan
}

function Stop-Services {
    Write-Info "Stopping services..."
    $r = Invoke-Compose -Args @("down")
    if ($r.ExitCode -eq 0) {
        Write-Success "Services stopped"
    } else {
        Write-Error-Custom "Failed to stop services"
        Write-Host $r.Output
    }
}

function Restart-Services {
    Write-Info "Restarting services..."
    $r = Invoke-Compose -Args @("restart")
    if ($r.ExitCode -eq 0) {
        Write-Success "Services restarted"
    } else {
        $running = Test-ComposeServicesRunning -ContainerNames @("mineos-api", "mineos-web")
        if ($running) {
            Write-Success "Services restarted"
        } else {
            Write-Error-Custom "Failed to restart services"
        }
    }
}

function Show-Logs {
    Write-Info "Showing logs (press Q to return)..."
    if (-not $script:composeExe) { Set-ComposeCommand }

    $allArgs = @()
    if ($script:composeBaseArgs) { $allArgs += $script:composeBaseArgs }
    $allArgs += @("logs", "-f", "--tail", "100")
    $argString = ($allArgs | ForEach-Object {
        if ($_ -match '\s') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '

    $outFile = New-TemporaryFile
    $errFile = New-TemporaryFile

    try {
        $proc = Start-Process -FilePath $script:composeExe `
            -ArgumentList $argString `
            -NoNewWindow `
            -PassThru `
            -RedirectStandardOutput $outFile `
            -RedirectStandardError $errFile

        $outOffset = 0L
        $errOffset = 0L
        while (-not $proc.HasExited) {
            Start-Sleep -Milliseconds 200
            $outOffset = Write-NewFileContent -Path $outFile -Offset $outOffset
            $errOffset = Write-NewFileContent -Path $errFile -Offset $errOffset
            if ([Console]::KeyAvailable) {
                $key = [Console]::ReadKey($true)
                if ($key.Key -eq [ConsoleKey]::Q -or $key.KeyChar -eq 'q') {
                    try { $proc.Kill() } catch { }
                    break
                }
            }
        }
        $outOffset = Write-NewFileContent -Path $outFile -Offset $outOffset
        $errOffset = Write-NewFileContent -Path $errFile -Offset $errOffset
    } finally {
        Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
    }
}

function Do-FreshInstall {
    Write-Header "Fresh Install"

    if (Test-Path ".env") {
        Write-Warn "Existing installation detected!"
        $confirm = Read-Host "This will OVERWRITE your configuration. Continue? (y/N)"
        if ($confirm -ne "y" -and $confirm -ne "Y") {
            Write-Info "Cancelled"
            return
        }
    }

    Test-Dependencies
    Start-ConfigWizard
    New-EnvFile
    New-Directories
    Start-Services -Rebuild

    Write-Header "Installation Complete!"
    Write-Host ""
    Write-Host "Web UI:    http://localhost:$webPort" -ForegroundColor Green
    Write-Host "Username:  $adminUser" -ForegroundColor Cyan
    Write-Host "Password:  $adminPass" -ForegroundColor Cyan
    Write-Host ""
}

function Do-Rebuild {
    Write-Header "Rebuild"

    if (-not (Test-Path ".env")) {
        Write-Error-Custom "No installation found. Run fresh install first."
        return
    }

    Write-Info "This will rebuild the Docker images and restart services."
    Write-Info "Your configuration and data will be preserved."
    $confirm = Read-Host "Continue? (Y/n)"
    if ($confirm -eq "n" -or $confirm -eq "N") {
        Write-Info "Cancelled"
        return
    }

    Load-ExistingConfig
    Start-Services -Rebuild

    Write-Success "Rebuild complete!"
    Write-Host "Web UI: http://localhost:$webPort" -ForegroundColor Green
}

function Do-Update {
    Write-Header "Update"

    if (-not (Test-Path ".env")) {
        Write-Error-Custom "No installation found. Run fresh install first."
        return
    }

    Write-Info "Pulling latest code and rebuilding..."

    # Pull latest if in git repo
    if (Test-Path ".git") {
        Write-Info "Pulling latest changes..."
        git pull
    }

    Load-ExistingConfig
    Start-Services -Rebuild

    Write-Success "Update complete!"
    Write-Host "Web UI: http://localhost:$webPort" -ForegroundColor Green
}

function Do-Reconfigure {
    Write-Header "Reconfigure"

    if (-not (Test-Path ".env")) {
        Write-Error-Custom "No installation found. Run fresh install first."
        return
    }

    Load-ExistingConfig

    $newAdminUser = Read-Host "Admin username (default: $adminUser)"
    if ([string]::IsNullOrWhiteSpace($newAdminUser)) { $newAdminUser = $adminUser }

    $newAdminPass = $null
    $adminPassSecure = Read-Host "Admin password (leave blank to keep current)" -AsSecureString
    $adminPassCandidate = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($adminPassSecure)
    )
    if (-not [string]::IsNullOrWhiteSpace($adminPassCandidate)) { $newAdminPass = $adminPassCandidate }

    $newBaseDir = Read-Host "Local storage directory (default: $baseDir)"
    if ([string]::IsNullOrWhiteSpace($newBaseDir)) { $newBaseDir = $baseDir }

    $newDataDir = Read-Host "Database directory (default: $dataDir)"
    if ([string]::IsNullOrWhiteSpace($newDataDir)) { $newDataDir = $dataDir }

    $newApiPort = Read-Host "API port (default: $apiPort)"
    if ([string]::IsNullOrWhiteSpace($newApiPort)) { $newApiPort = $apiPort }
    [void](Assert-PortNumber -Value $newApiPort -Name "API port")

    $newWebPort = Read-Host "Web UI port (default: $webPort)"
    if ([string]::IsNullOrWhiteSpace($newWebPort)) { $newWebPort = $webPort }
    [void](Assert-PortNumber -Value $newWebPort -Name "Web UI port")

    $newMcPublicHost = Read-Host "Public Minecraft host (default: $mcPublicHost)"
    if ([string]::IsNullOrWhiteSpace($newMcPublicHost)) { $newMcPublicHost = $mcPublicHost }

    $newBodySizeLimit = Read-Host "Web UI upload body size limit (default: $bodySizeLimit)"
    if ([string]::IsNullOrWhiteSpace($newBodySizeLimit)) { $newBodySizeLimit = $bodySizeLimit }

    $newMcPortRange = Read-Host "Minecraft server port range (default: $mcPortRange)"
    if ([string]::IsNullOrWhiteSpace($newMcPortRange)) { $newMcPortRange = $mcPortRange }
    $newMcPortRange = Assert-PortRange -Value $newMcPortRange -Name "Minecraft port range"

    $newCurseforgeKey = Read-Host "CurseForge API key (leave blank to keep current)"
    if ([string]::IsNullOrWhiteSpace($newCurseforgeKey)) { $newCurseforgeKey = Get-EnvValue "CurseForge__ApiKey" }

    $newDiscordWebhook = Read-Host "Discord webhook URL (leave blank to keep current)"
    if ([string]::IsNullOrWhiteSpace($newDiscordWebhook)) { $newDiscordWebhook = Get-EnvValue "Discord__WebhookUrl" }

    Set-EnvValue -Key "Auth__SeedUsername" -Value $newAdminUser
    if ($newAdminPass) { Set-EnvValue -Key "Auth__SeedPassword" -Value $newAdminPass }
    Set-EnvValue -Key "HOST_BASE_DIRECTORY" -Value $newBaseDir
    Set-EnvValue -Key "Data__Directory" -Value $newDataDir
    Set-EnvValue -Key "API_PORT" -Value $newApiPort
    Set-EnvValue -Key "WEB_PORT" -Value $newWebPort
    Set-EnvValue -Key "PUBLIC_MINECRAFT_HOST" -Value $newMcPublicHost
    Set-EnvValue -Key "BODY_SIZE_LIMIT" -Value $newBodySizeLimit
    Set-EnvValue -Key "MC_PORT_RANGE" -Value $newMcPortRange
    if ($newCurseforgeKey -ne $null) { Set-EnvValue -Key "CurseForge__ApiKey" -Value $newCurseforgeKey }
    if ($newDiscordWebhook -ne $null) { Set-EnvValue -Key "Discord__WebhookUrl" -Value $newDiscordWebhook }

    Write-WebDevEnv
    Write-Success "Configuration updated."
    Write-Info "Restart services to apply changes."
}

function Show-Menu {
    Clear-Host
    Write-Host ""
    Write-Host "  __  __ _             ___  ____  " -ForegroundColor Green
    Write-Host " |  \/  (_)_ __   ___ / _ \/ ___| " -ForegroundColor Green
    Write-Host " | |\/| | | '_ \ / _ \ | | \___ \ " -ForegroundColor Green
    Write-Host " | |  | | | | | |  __/ |_| |___) |" -ForegroundColor Green
    Write-Host " |_|  |_|_|_| |_|\___|\___/|____/ " -ForegroundColor Green
    Write-Host ""
    Write-Host " Minecraft Server Management" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "================================" -ForegroundColor DarkGray

    $installed = Test-Path ".env"
    if ($installed) {
        Load-ExistingConfig
        Write-Host " Status: " -NoNewline
        Write-Host "Installed" -ForegroundColor Green
    } else {
        Write-Host " Status: " -NoNewline
        Write-Host "Not Installed" -ForegroundColor Yellow
    }

    Write-Host "================================" -ForegroundColor DarkGray
    Write-Host ""

    if (-not $installed) {
        Write-Host " [1] Fresh Install" -ForegroundColor White
        Write-Host ""
        Write-Host " [Q] Quit" -ForegroundColor DarkGray
    } else {
        Write-Host " [1] Start Services" -ForegroundColor White
        Write-Host " [2] Stop Services" -ForegroundColor White
        Write-Host " [3] Restart Services" -ForegroundColor White
        Write-Host " [4] View Logs" -ForegroundColor White
        Write-Host " [5] Show Status" -ForegroundColor White
        Write-Host " [R] Reconfigure (update .env)" -ForegroundColor Yellow
        Write-Host " [W] Web Dev Container (Vite)" -ForegroundColor Yellow
        Write-Host ""
        Write-Host " [6] Rebuild (keep config)" -ForegroundColor Yellow
        Write-Host " [7] Update (git pull + rebuild)" -ForegroundColor Yellow
        Write-Host " [8] Fresh Install (reset everything)" -ForegroundColor Red
        Write-Host " [9] Dev Mode (API only + web dev env)" -ForegroundColor Yellow
        Write-Host ""
        Write-Host " [Q] Quit" -ForegroundColor DarkGray
    }

    Write-Host ""
    $choice = Read-Host "Select option"
    return $choice
}

function Main {
    if ($Dev) {
        Start-DevMode
        return
    }

    Test-Dependencies

    while ($true) {
        $choice = Show-Menu
        $installed = Test-Path ".env"

        if (-not $installed) {
            switch ($choice.ToUpper()) {
                "1" { Do-FreshInstall; Read-Host "Press Enter to continue" }
                "Q" { Write-Host "Goodbye!"; exit 0 }
                default { Write-Warn "Invalid option" }
            }
        } else {
            switch ($choice.ToUpper()) {
                "1" { Load-ExistingConfig; Start-Services; Read-Host "Press Enter to continue" }
                "2" { Stop-Services; Read-Host "Press Enter to continue" }
                "3" { Restart-Services; Read-Host "Press Enter to continue" }
                "4" { Show-Logs }
                "5" { Show-Status; Read-Host "Press Enter to continue" }
                "R" { Do-Reconfigure; Read-Host "Press Enter to continue" }
                "W" { Start-WebDevContainer; Read-Host "Press Enter to continue" }
                "6" { Do-Rebuild; Read-Host "Press Enter to continue" }
                "7" { Do-Update; Read-Host "Press Enter to continue" }
                "8" { Do-FreshInstall; Read-Host "Press Enter to continue" }
                "9" { Start-DevMode -Pause }
                "Q" { Write-Host "Goodbye!"; exit 0 }
                default { Write-Warn "Invalid option" }
            }
        }
    }
}

Main
