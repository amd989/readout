#!/bin/bash
# Build the Mac Catalyst app and launch it, replacing a copy that's already running.
#   ./deploy-mac.sh          build + launch
#   ./deploy-mac.sh --log    ...then follow the log (Ctrl-C stops following, the app keeps running)
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJ="$SCRIPT_DIR/Readout"
RID="maccatalyst-$(uname -m | sed 's/x86_64/x64/')"
APP="$PROJ/bin/Debug/net10.0-maccatalyst/$RID/Readout.app"
LOG="$PROJ/bin/mac.log"

echo "==> Building ($RID)..."
dotnet build "$PROJ" -f net10.0-maccatalyst -p:RuntimeIdentifier="$RID"

# Match this build's path: a bare `Readout` would also kill the app in iOS simulators.
BIN="$APP/Contents/MacOS/Readout"
if pgrep -f "$BIN" >/dev/null; then
  echo "==> Quitting the running copy..."
  pkill -f "$BIN" || true
  for _ in {1..50}; do pgrep -f "$BIN" >/dev/null || break; sleep 0.1; done
fi

# Launch through `open`, never Contents/MacOS/Readout: run from a shell, macOS
# kills the app on its first Bluetooth access. Log lines go to stderr.
echo "==> Launching..."
: > "$LOG"
open "$APP" --stdout "$LOG" --stderr "$LOG"
echo "==> Done. Log: $LOG"

if [ "$1" = "--log" ]; then
  tail -f "$LOG"
fi
