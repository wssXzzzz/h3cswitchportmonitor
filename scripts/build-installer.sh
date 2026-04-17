#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET:-dotnet}"
RUNTIME="${RUNTIME:-win-x64}"
SERVICE_OUT="$ROOT/artifacts/service"
INSTALLER_OUT="$ROOT/artifacts/installer"
PAYLOAD_DIR="$ROOT/installer/Payload"
PAYLOAD_ZIP="$PAYLOAD_DIR/service.zip"

rm -rf "$SERVICE_OUT" "$INSTALLER_OUT" "$PAYLOAD_DIR"
mkdir -p "$SERVICE_OUT" "$INSTALLER_OUT" "$PAYLOAD_DIR"

"$DOTNET_BIN" publish "$ROOT/H3CSwitchPortMonitor.csproj" \
  -c Release \
  -r "$RUNTIME" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$SERVICE_OUT"

rm -rf "$SERVICE_OUT/installer" "$SERVICE_OUT/artifacts" "$SERVICE_OUT/bin" "$SERVICE_OUT/obj"

cp "$ROOT/portable/config-editor.ps1" "$SERVICE_OUT/"
cp "$ROOT/portable/edit-config.cmd" "$SERVICE_OUT/"
cp "$ROOT/portable/edit-config-raw.cmd" "$SERVICE_OUT/"
cp "$ROOT/portable/restart-service.cmd" "$SERVICE_OUT/"

(cd "$SERVICE_OUT" && zip -qr "$PAYLOAD_ZIP" .)

"$DOTNET_BIN" publish "$ROOT/installer/H3CSwitchPortMonitorInstaller.csproj" \
  -c Release \
  -r "$RUNTIME" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$INSTALLER_OUT"

echo "Installer:"
echo "$INSTALLER_OUT/H3CSwitchPortMonitorInstaller.exe"
