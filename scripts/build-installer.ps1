param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$ServiceOut = Join-Path $Root "artifacts\service"
$InstallerOut = Join-Path $Root "artifacts\installer"
$PayloadDir = Join-Path $Root "installer\Payload"
$PayloadZip = Join-Path $PayloadDir "service.zip"

Remove-Item $ServiceOut, $InstallerOut -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $ServiceOut, $InstallerOut, $PayloadDir | Out-Null
Remove-Item $PayloadZip -Force -ErrorAction SilentlyContinue

dotnet publish (Join-Path $Root "H3CSwitchPortMonitor.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o $ServiceOut

Compress-Archive -Path (Join-Path $ServiceOut "*") -DestinationPath $PayloadZip -Force

dotnet publish (Join-Path $Root "installer\H3CSwitchPortMonitorInstaller.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o $InstallerOut

Write-Host "Installer:"
Write-Host (Join-Path $InstallerOut "H3CSwitchPortMonitorInstaller.exe")
