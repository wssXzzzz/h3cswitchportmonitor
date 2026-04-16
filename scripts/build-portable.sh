#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET:-dotnet}"
RUNTIME="${RUNTIME:-win-x64}"
SERVICE_OUT="$ROOT/artifacts/service"
PORTABLE_ROOT="$ROOT/artifacts/portable"
PORTABLE_OUT="$PORTABLE_ROOT/H3CSwitchPortMonitor"
ZIP_PATH="$PORTABLE_ROOT/H3CSwitchPortMonitor-portable-win-x64.zip"

rm -rf "$SERVICE_OUT" "$PORTABLE_OUT" "$ZIP_PATH"
mkdir -p "$SERVICE_OUT" "$PORTABLE_OUT" "$PORTABLE_ROOT"

"$DOTNET_BIN" publish "$ROOT/H3CSwitchPortMonitor.csproj" \
  -c Release \
  -r "$RUNTIME" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$SERVICE_OUT"

cp "$SERVICE_OUT/H3CSwitchPortMonitor.exe" "$PORTABLE_OUT/"
cp "$SERVICE_OUT/appsettings.json" "$PORTABLE_OUT/"
cp -R "$ROOT/portable/." "$PORTABLE_OUT/"

(cd "$PORTABLE_ROOT" && zip -qr "$ZIP_PATH" H3CSwitchPortMonitor)

echo "Portable zip:"
echo "$ZIP_PATH"
