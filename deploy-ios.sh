#!/bin/bash
# Build the iOS app for a physical iPhone, then install and launch it.
#   ./deploy-ios.sh              the first device in .devices
#   ./deploy-ios.sh phone ipad   devices by their name in .devices, from one build
#   ./deploy-ios.sh <UDID>       any device (xcrun devicectl list devices)
#   ./deploy-ios.sh --release    an optimized (AOT) build, as TestFlight gets: Debug builds run
#                                through an interpreter on the device, so judge speed with this
set -e

CONFIG=Debug
if [ "$1" = "--release" ]; then CONFIG=Release; shift; fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJ="$SCRIPT_DIR/Readout"
APP="$PROJ/bin/$CONFIG/net10.0-ios/ios-arm64/Readout.app"
BUNDLE="md.alejandro.readout"

# The newest installed iOS workload's mlaunch, so a workload update doesn't break this.
MLAUNCH="$(ls -d /usr/local/share/dotnet/packs/Microsoft.iOS.Sdk.net10.0_*/*/tools/bin/mlaunch 2>/dev/null | sort -V | tail -1)"
if [ -z "$MLAUNCH" ]; then
  echo "mlaunch not found: is the iOS workload installed? (dotnet workload install ios)" >&2
  exit 1
fi

# Device names are kept out of the repository: put lines like `phone=<UDID>` in
# .devices (git-ignored). The first line is the default.
DEVICES_FILE="$SCRIPT_DIR/.devices"
udid_for() {
  if [ -f "$DEVICES_FILE" ]; then
    local match
    match="$(grep -E "^$1=" "$DEVICES_FILE" | head -1 | cut -d= -f2)"
    [ -n "$match" ] && { echo "$match"; return; }
  fi
  echo "$1"
}
default_device() {
  [ -f "$DEVICES_FILE" ] && head -1 "$DEVICES_FILE" | cut -d= -f1
}

# Signing: $CODESIGN_KEY, else the keychain's first "Apple Development" identity.
KEY="${CODESIGN_KEY:-$(security find-identity -v -p codesigning | grep -o '"Apple Development: [^"]*"' | head -1 | tr -d '"')}"
if [ -z "$KEY" ]; then
  echo "No Apple Development certificate in the keychain (Xcode > Settings > Accounts)." >&2
  exit 1
fi

DEVICES=("$@")
if [ ${#DEVICES[@]} -eq 0 ]; then
  DEFAULT="$(default_device)"
  [ -z "$DEFAULT" ] && { echo "Usage: ./deploy-ios.sh <UDID or name from .devices>" >&2; exit 1; }
  DEVICES=("$DEFAULT")
fi

echo "==> Building ($CONFIG)..."
dotnet build "$PROJ" -f net10.0-ios -c "$CONFIG" \
  /p:RuntimeIdentifier=ios-arm64 \
  /p:CodesignKey="$KEY" \
  /p:AutomaticProvisioning=true

for device in "${DEVICES[@]}"; do
  UDID="$(udid_for "$device")"
  echo "==> Installing on $device ($UDID)..."
  "$MLAUNCH" --installdev "$APP" --devname "$UDID"
  echo "==> Launching on $device..."
  "$MLAUNCH" --launchdevbundleid "$BUNDLE" --devname "$UDID"
done

echo "==> Done."
