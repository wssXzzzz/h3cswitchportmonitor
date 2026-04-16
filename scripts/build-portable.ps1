param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$ServiceOut = Join-Path $Root "artifacts\service"
$PortableOut = Join-Path $Root "artifacts\portable\H3CSwitchPortMonitor"
$ZipPath = Join-Path $Root "artifacts\portable\H3CSwitchPortMonitor-portable-win-x64.zip"

Remove-Item $ServiceOut, $PortableOut, $ZipPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $ServiceOut, $PortableOut | Out-Null

dotnet publish (Join-Path $Root "H3CSwitchPortMonitor.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o $ServiceOut

Copy-Item (Join-Path $ServiceOut "H3CSwitchPortMonitor.exe") $PortableOut
Copy-Item (Join-Path $ServiceOut "appsettings.json") $PortableOut
Copy-Item (Join-Path $Root "portable\*") $PortableOut -Recurse

Compress-Archive -Path $PortableOut -DestinationPath $ZipPath -Force

Write-Host "Portable zip:"
Write-Host $ZipPath
