#!/bin/bash
# Build the Android app, install it on a phone over adb and launch it.
#   ./deploy-android.sh              the only phone plugged in (emulators are skipped)
#   ./deploy-android.sh <serial>     a given device or emulator (adb devices)
#   ./deploy-android.sh --release    an optimized (AOT) build, as Google Play gets: judge speed with this
#   ./deploy-android.sh --log        ...then follow the app's log (Ctrl-C stops following, the app keeps running)
# Flags go before the serial. The phone needs USB debugging on (Developer options) and this
# Mac allowed on its "Allow USB debugging?" prompt.
set -e

CONFIG=Debug
FOLLOW_LOG=0
while [ $# -gt 0 ]; do
  case "$1" in
    --release) CONFIG=Release; shift ;;
    --log) FOLLOW_LOG=1; shift ;;
    -*) echo "Unknown option: $1" >&2; exit 1 ;;
    *) break ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJ="$SCRIPT_DIR/Readout"
PACKAGE="md.alejandro.readout"

# adb: from the SDK the Android workload uses, else PATH.
ADB=""
for sdk in "$ANDROID_HOME" "$ANDROID_SDK_ROOT" "$HOME/Library/Android/sdk" "$HOME/Android/Sdk"; do
  [ -n "$sdk" ] && [ -x "$sdk/platform-tools/adb" ] && { ADB="$sdk/platform-tools/adb"; break; }
done
[ -z "$ADB" ] && ADB="$(command -v adb || true)"
if [ -z "$ADB" ]; then
  echo "adb not found: install the Android SDK platform-tools, or set ANDROID_HOME." >&2
  exit 1
fi

SERIAL="$1"
if [ -z "$SERIAL" ]; then
  # A Debug build would otherwise also go to a running emulator; pick the physical phone.
  PHONES="$("$ADB" devices | awk 'NR > 1 && $1 != "" && $1 !~ /^emulator-/ { print $1 " " $2 }')"
  case "$(echo -n "$PHONES" | grep -c '^')" in
    0) echo "No phone found on adb. Plug it in with USB debugging on, or pass an emulator's serial." >&2
       "$ADB" devices -l >&2; exit 1 ;;
    1) SERIAL="${PHONES%% *}" ;;
    *) echo "More than one phone on adb; pass a serial:" >&2; echo "$PHONES" >&2; exit 1 ;;
  esac
fi

STATE="$("$ADB" -s "$SERIAL" get-state 2>&1 || true)"
if [ "$STATE" != device ]; then
  if echo "$STATE" | grep -q unauthorized; then
    echo "==> $SERIAL hasn't allowed this Mac yet: tap Allow on the phone's \"Allow USB debugging?\" prompt..."
    "$ADB" -s "$SERIAL" wait-for-device
  else
    echo "$SERIAL isn't ready on adb ($STATE)." >&2
    exit 1
  fi
fi
MODEL="$("$ADB" -s "$SERIAL" shell getprop ro.product.model | tr -d '\r')"

# The Install target, not `adb install` of the .apk: a Debug .apk holds no assemblies (Fast
# Deployment copies them to the device separately), so on its own it aborts at launch.
echo "==> Building ($CONFIG) and installing on $MODEL ($SERIAL)..."
dotnet build "$PROJ" -f net10.0-android -c "$CONFIG" -t:Install "-p:AdbTarget=-s $SERIAL"

echo "==> Launching..."
"$ADB" -s "$SERIAL" shell am force-stop "$PACKAGE"
"$ADB" -s "$SERIAL" logcat -c
"$ADB" -s "$SERIAL" shell monkey -p "$PACKAGE" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1

PID=""
for _ in {1..50}; do
  PID="$("$ADB" -s "$SERIAL" shell pidof "$PACKAGE" | tr -d '\r')"
  [ -n "$PID" ] && break
  sleep 0.2
done
# A startup abort (missing assemblies, a bad native lib) takes the process down within a second.
if [ -n "$PID" ]; then
  sleep 2
  [ "$("$ADB" -s "$SERIAL" shell pidof "$PACKAGE" | tr -d '\r')" = "$PID" ] || PID=""
fi
if [ -z "$PID" ]; then
  echo "The app didn't start. Its last log lines:" >&2
  "$ADB" -s "$SERIAL" logcat -d | grep -E "monodroid|AndroidRuntime|DEBUG   :" | tail -20 >&2
  exit 1
fi
echo "==> Done (pid $PID)."

if [ "$FOLLOW_LOG" = 1 ]; then
  "$ADB" -s "$SERIAL" logcat --pid="$PID"
fi
