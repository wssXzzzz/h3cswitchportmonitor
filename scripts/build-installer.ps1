param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$ServiceOut = Join-Path $Root "artifacts\service"
$InstallerOut = Join-Path $Root "artifacts\installer"
$PayloadDir = Join-Path $Root "installer\Payload"
$PayloadZip = Join-Path $PayloadDir "service.zip"

Remove-Item $ServiceOut, $InstallerOut, $PayloadDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $ServiceOut, $InstallerOut, $PayloadDir | Out-Null

dotnet publish (Join-Path $Root "H3CSwitchPortMonitor.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o $ServiceOut

Remove-Item `
    (Join-Path $ServiceOut "installer"), `
    (Join-Path $ServiceOut "artifacts"), `
    (Join-Path $ServiceOut "bin"), `
    (Join-Path $ServiceOut "obj") `
    -Recurse -Force -ErrorAction SilentlyContinue

Copy-Item (Join-Path $Root "portable\config-editor.ps1") $ServiceOut
Copy-Item (Join-Path $Root "portable\edit-config.cmd") $ServiceOut
Copy-Item (Join-Path $Root "portable\edit-config-raw.cmd") $ServiceOut
Copy-Item (Join-Path $Root "portable\restart-service.cmd") $ServiceOut

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
