#!/bin/bash
set -e

# Builds a Release .ipa signed for TestFlight and the App Store; upload it with Transporter.
# Needs an "Apple Distribution" certificate in the keychain and an App Store Connect
# provisioning profile for md.alejandro.readout named "$PROFILE".

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJ="$SCRIPT_DIR/Readout"
OUT="$PROJ/bin/Release/net10.0-ios/ios-arm64/publish"
PROFILE="${PROFILE:-Readout App Store}"
# Each upload needs a higher build number than the last: the date and time (261022.1702),
# or ./publish-ios.sh <number>. The 10# drops a leading zero from the time.
BUILD="${1:-$(date +%y%m%d).$((10#$(date +%H%M)))}"
KEY="$(security find-identity -v -p codesigning | grep -o '"Apple Distribution: [^"]*"' | head -1 | tr -d '"')"

if [ -z "$KEY" ]; then
  echo "No Apple Distribution certificate in the keychain (Xcode > Settings > Accounts > Manage Certificates)." >&2
  exit 1
fi

echo "==> Building build $BUILD, signed with $KEY..."
dotnet publish "$PROJ" -f net10.0-ios -c Release \
  /p:RuntimeIdentifier=ios-arm64 \
  /p:ArchiveOnBuild=true \
  /p:CodesignKey="$KEY" \
  /p:CodesignProvision="$PROFILE" \
  /p:ApplicationVersion="$BUILD"

echo "==> Done:"
ls "$OUT"/*.ipa
