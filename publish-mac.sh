#!/bin/bash
set -e

# Builds a signed Release .pkg of the Mac app for the Mac App Store; upload it with Transporter.
# Needs, in the keychain:
#   - "Apple Distribution" (signs the app; the same certificate as publish-ios.sh)
#   - "3rd Party Mac Developer Installer" (signs the .pkg; Xcode > Settings > Accounts >
#     Manage Certificates > + > Mac Installer Distribution)
# and a Mac App Store provisioning profile for md.alejandro.readout named "$PROFILE".

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJ="$SCRIPT_DIR/Readout"
OUT="$PROJ/bin/Release/net10.0-maccatalyst/publish"
PROFILE="${PROFILE:-Readout Mac App Store}"
# Each upload needs a higher build number than the last; see publish-ios.sh.
BUILD="${1:-$(date +%y%m%d).$((10#$(date +%H%M)))}"
KEY="$(security find-identity -v -p codesigning | grep -o '"Apple Distribution: [^"]*"' | head -1 | tr -d '"')"
INSTALLER="$(security find-identity -v | grep -o '"3rd Party Mac Developer Installer: [^"]*"' | head -1 | tr -d '"')"

if [ -z "$KEY" ]; then
  echo "No Apple Distribution certificate in the keychain (Xcode > Settings > Accounts > Manage Certificates)." >&2
  exit 1
fi
if [ -z "$INSTALLER" ]; then
  echo "No \"3rd Party Mac Developer Installer\" certificate in the keychain: add a Mac Installer Distribution" >&2
  echo "certificate in Xcode > Settings > Accounts > Manage Certificates." >&2
  exit 1
fi

echo "==> Building build $BUILD (Apple silicon and Intel), signed with $KEY..."
dotnet publish "$PROJ" -f net10.0-maccatalyst -c Release \
  /p:CreatePackage=true \
  /p:EnableCodeSigning=true \
  /p:EnablePackageSigning=true \
  /p:CodesignKey="$KEY" \
  /p:CodesignProvision="$PROFILE" \
  /p:CodesignEntitlements=Platforms/MacCatalyst/Entitlements.plist \
  /p:PackageSigningKey="$INSTALLER" \
  /p:UseHardenedRuntime=true \
  /p:ApplicationVersion="$BUILD"

PKG="$(ls -t "$OUT"/*.pkg | head -1)"

# The Mac app is meant to be universal: every binary should carry both Apple silicon and Intel.
echo "==> Checking the app's binaries..."
UNPACKED="$(mktemp -d)"
trap 'rm -rf "$UNPACKED"' EXIT
pkgutil --expand-full "$PKG" "$UNPACKED/pkg"
APP="$(find "$UNPACKED/pkg" -name "Readout.app" -type d | head -1)"
ARCHS="$(lipo -archs "$APP/Contents/MacOS/Readout")"
echo "    Readout: $ARCHS"
case "$ARCHS" in
  *x86_64*arm64*|*arm64*x86_64*) ;;
  *) echo "Expected a universal binary (x86_64 and arm64)." >&2; exit 1 ;;
esac
# The App Sandbox is required on the Mac App Store, and Bluetooth needs its entitlement.
ENT="$(codesign -d --entitlements - --xml "$APP" 2>/dev/null)"
for key in com.apple.security.app-sandbox com.apple.security.device.bluetooth; do
  echo "$ENT" | grep -q "$key" || { echo "Missing entitlement $key." >&2; exit 1; }
done

echo "==> Done: $PKG"
