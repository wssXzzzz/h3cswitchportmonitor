param(
    [string]$ServiceName = "H3CSwitchPortMonitor",
    [string]$DisplayName = "H3C Switch Port Monitor",
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ExePath)) {
    throw "Executable not found: $ExePath"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service already exists: $ServiceName"
    exit 0
}

New-Service `
    -Name $ServiceName `
    -DisplayName $DisplayName `
    -BinaryPathName "`"$ExePath`"" `
    -StartupType Automatic `
    -Description "Monitor H3C switch port status by SNMP and send Feishu robot notifications."

Start-Service -Name $ServiceName
Write-Host "Service installed and started: $ServiceName"
