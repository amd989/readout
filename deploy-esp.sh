#!/bin/bash
# Build the Readout bridge firmware and flash it to an ESP32 on USB.
#   ./deploy-esp.sh                  fs9721-esp32dev (TXD lights the optocoupler's LED)
#   ./deploy-esp.sh dso150-esp32c3   another build: fs9721-esp32dev-vdd, fs9721-esp32c3(-vdd), dso150-esp32dev, dso150-esp32c3
#   ./deploy-esp.sh --log            ...then follow its serial output (Ctrl-C stops)
#   ./deploy-esp.sh --port /dev/cu.usbserial-0001   when more than one board is plugged in
# The builds and their wiring are in firmware/readout-bridge/README.md.
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
FIRMWARE="$SCRIPT_DIR/firmware/readout-bridge"
ENV=fs9721-esp32dev
PORT=""
LOG=0

while [ $# -gt 0 ]; do
  case "$1" in
    --log) LOG=1 ;;
    --port) PORT="$2"; shift ;;
    -h|--help) sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) echo "Unknown option: $1" >&2; exit 1 ;;
    *) ENV="$1" ;;
  esac
  shift
done

if ! command -v pio >/dev/null; then
  echo "PlatformIO isn't installed: pip install platformio (or the VS Code extension)." >&2
  exit 1
fi

if ! grep -q "^\[env:$ENV\]" "$FIRMWARE/platformio.ini"; then
  echo "No build called $ENV. These exist:" >&2
  grep -o '^\[env:[^]]*\]' "$FIRMWARE/platformio.ini" | sed 's/\[env:\(.*\)\]/  \1/' >&2
  exit 1
fi

# The board's USB serial port: CP210x and CH340 adapters, or a C3's own USB. A CP210x shows up
# twice (usbserial and SLAB_USBtoUART); the first name is enough.
if [ -z "$PORT" ]; then
  PORTS=$(ls /dev/cu.usbserial-* /dev/cu.wchusbserial* /dev/cu.usbmodem* 2>/dev/null || true)
  [ -z "$PORTS" ] && PORTS=$(ls /dev/cu.SLAB_USBtoUART* 2>/dev/null || true)
  COUNT=$(printf '%s\n' "$PORTS" | grep -c . || true)
  if [ "$COUNT" -eq 0 ]; then
    echo "No ESP32 found on USB. Plug it in (with a cable that carries data) and try again." >&2
    exit 1
  elif [ "$COUNT" -gt 1 ]; then
    echo "More than one serial port; pick one with --port:" >&2
    printf '  %s\n' $PORTS >&2
    exit 1
  fi
  PORT="$PORTS"
fi

echo "==> Building and flashing $ENV to $PORT..."
pio run -d "$FIRMWARE" -e "$ENV" -t upload --upload-port "$PORT"
echo "==> Done."

if [ "$LOG" = 1 ]; then
  echo "==> Following $PORT (Ctrl-C stops)..."
  pio device monitor -d "$FIRMWARE" -e "$ENV" -p "$PORT"
fi
