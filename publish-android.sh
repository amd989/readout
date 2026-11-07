#!/bin/bash
set -e

# Builds a signed Release .aab for Google Play and an .apk for sideloading, both in
# artifacts/android. Both are signed with the upload key; Google Play re-signs the .aab with
# the app signing key, so the .apk and the Play Store copy can't update each other.
#
#   ANDROID_KEYSTORE           path to the upload keystore (.jks or .keystore)
#   ANDROID_KEY_ALIAS          the key's alias in it
#   ANDROID_KEYSTORE_PASSWORD  the keystore password
#   ANDROID_KEY_PASSWORD       the key password (defaults to the keystore password)
#   DISPLAY_VERSION            versionName, like 1.2.3 (defaults to ApplicationDisplayVersion)
#
# ./publish-android.sh [versionCode]
# Each upload needs a higher versionCode than the last: the UTC date and time as yyDDDHHMM
# (263101702), or pass one.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJ="$SCRIPT_DIR/Readout"
PUBLISH="$PROJ/bin/Release/net10.0-android/publish"
OUT="$SCRIPT_DIR/artifacts/android"
BUILD="${1:-$(date -u +%y%j%H%M)}"
NAME="${DISPLAY_VERSION:-$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' "$PROJ/Readout.csproj" | head -1)}"

: "${ANDROID_KEYSTORE:?Set ANDROID_KEYSTORE to the upload keystore.}"
: "${ANDROID_KEY_ALIAS:?Set ANDROID_KEY_ALIAS to the key alias.}"
: "${ANDROID_KEYSTORE_PASSWORD:?Set ANDROID_KEYSTORE_PASSWORD.}"
export ANDROID_KEY_PASSWORD="${ANDROID_KEY_PASSWORD:-$ANDROID_KEYSTORE_PASSWORD}"
export ANDROID_KEYSTORE_PASSWORD

echo "==> Building $NAME ($BUILD)..."
# Stale packages from an earlier build would otherwise be picked up below.
rm -rf "$PUBLISH"
# The passwords go through environment variables (env:), so they stay out of the build log.
# %3B is an escaped ';' (MSBuild list separator).
dotnet publish "$PROJ" -f net10.0-android -c Release \
  -p:AndroidPackageFormats=aab%3Bapk \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore="$ANDROID_KEYSTORE" \
  -p:AndroidSigningKeyAlias="$ANDROID_KEY_ALIAS" \
  -p:AndroidSigningStorePass=env:ANDROID_KEYSTORE_PASSWORD \
  -p:AndroidSigningKeyPass=env:ANDROID_KEY_PASSWORD \
  -p:ApplicationDisplayVersion="$NAME" \
  -p:ApplicationVersion="$BUILD"

AAB="$(ls "$PUBLISH"/*-Signed.aab 2>/dev/null | head -1)"
APK="$(ls "$PUBLISH"/*-Signed.apk 2>/dev/null | head -1)"
if [ -z "$AAB" ] || [ -z "$APK" ]; then
  echo "Expected a signed .aab and .apk in $PUBLISH." >&2
  exit 1
fi

mkdir -p "$OUT"
cp "$AAB" "$OUT/Readout-$NAME.aab"
cp "$APK" "$OUT/Readout-$NAME.apk"
echo "==> Done: $OUT/Readout-$NAME.aab and .apk"
