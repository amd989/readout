#!/bin/bash
set -e

# Builds the Linux app (Readout.Linux, MAUI on GTK4) as a self-contained folder for each
# architecture asked for, and packs each as Readout-linux-<arch>.tar.gz:
#   ./publish-linux.sh            x64
#   ./publish-linux.sh arm64      arm64
#   ./publish-linux.sh x64 arm64  both
# Cross-publishes from macOS or Windows. The machine it runs on needs GTK 4.12+ (libgtk-4-1)
# and, for the meter, BlueZ running; no .NET install. Unpack and run ./readout/Readout
# (READOUT_FAKE_BLE=1 for the simulated meter).
#
# DISPLAY_VERSION (like 1.2.3) and BUILD_NUMBER override ApplicationDisplayVersion and
# ApplicationVersion; the release workflow sets them from the git tag.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJ="$SCRIPT_DIR/Readout.Linux"
OUT="$PROJ/bin/publish"
ARCHS=("${@:-x64}")
EXTRA=()
if [ -n "$DISPLAY_VERSION" ]; then EXTRA+=("-p:ApplicationDisplayVersion=$DISPLAY_VERSION"); fi
if [ -n "$BUILD_NUMBER" ]; then EXTRA+=("-p:ApplicationVersion=$BUILD_NUMBER"); fi

for ARCH in "${ARCHS[@]}"; do
  case "$ARCH" in x64|arm64) ;; *) echo "Unknown architecture '$ARCH' (x64 or arm64)." >&2; exit 1 ;; esac
  DIR="$OUT/linux-$ARCH/readout"
  echo "==> Publishing linux-$ARCH..."
  rm -rf "$DIR"
  dotnet publish "$PROJ" -c Release -r "linux-$ARCH" --self-contained -o "$DIR" -p:DebugType=none "${EXTRA[@]}"
  # Without macOS's extended attributes, which GNU tar warns about for every file.
  COPYFILE_DISABLE=1 tar --no-xattrs -czf "$OUT/Readout-linux-$ARCH.tar.gz" -C "$OUT/linux-$ARCH" readout
done

echo "==> Done:"
ls -lh "$OUT"/*.tar.gz
