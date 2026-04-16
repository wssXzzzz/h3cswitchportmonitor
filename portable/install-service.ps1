$ErrorActionPreference = "Stop"

$ServiceName = "H3CSwitchPortMonitor"
$DisplayName = "H3C Switch Port Monitor"
$ExePath = Join-Path $PSScriptRoot "H3CSwitchPortMonitor.exe"

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`""
    )
    exit
}

if (-not (Test-Path $ExePath)) {
    throw "未找到服务程序：$ExePath"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "服务已存在，正在更新服务路径..."
    if ($existing.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }

    & sc.exe config $ServiceName binPath= "`"$ExePath`"" start= auto DisplayName= "$DisplayName" | Write-Host
}
else {
    New-Service `
        -Name $ServiceName `
        -DisplayName $DisplayName `
        -BinaryPathName "`"$ExePath`"" `
        -StartupType Automatic `
        -Description "Monitor H3C switch port status by SNMP and send Feishu robot notifications."
}

& sc.exe description $ServiceName "Monitor H3C switch port status by SNMP and send Feishu robot notifications." | Write-Host
Start-Service -Name $ServiceName

Write-Host ""
Write-Host "安装完成并已启动服务。"
Write-Host "服务名：$ServiceName"
Write-Host "程序路径：$ExePath"
Write-Host "配置文件：$(Join-Path $PSScriptRoot 'appsettings.json')"
