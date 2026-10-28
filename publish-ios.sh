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

IPA="$(ls -t "$OUT"/*.ipa | head -1)"

# App Store Connect rejects any simulator (x86_64) slice in an iPhone build (ITMS-90087,
# with 90080, 90125 and 90209). Some NuGet packages ship fat native frameworks the SDK
# doesn't thin (Wavelength's SkiaSharp did), so check every binary before uploading.
echo "==> Checking that every binary in the .ipa is arm64 only..."
UNPACKED="$(mktemp -d)"
trap 'rm -rf "$UNPACKED"' EXIT
unzip -q "$IPA" -d "$UNPACKED"
BAD=""
while IFS= read -r -d '' f; do
  if file -b "$f" | grep -q "Mach-O"; then
    ARCHS="$(lipo -archs "$f" 2>/dev/null)"
    [ "$ARCHS" != "arm64" ] && BAD="$BAD\n  $ARCHS  ${f#"$UNPACKED"/Payload/}"
  fi
done < <(find "$UNPACKED/Payload" -type f -print0)
if [ -n "$BAD" ]; then
  echo -e "Not arm64 only, App Store Connect will reject it:$BAD" >&2
  echo "Strip the extra slices (lipo -remove x86_64) before signing; see Wavelength's _ThinFrameworksForDevice." >&2
  exit 1
fi

echo "==> Done: $IPA"
