#!/bin/bash
set -euo pipefail

# Packs the self-contained builds from publish-linux.sh into a .deb and an .rpm for each
# architecture asked for, in artifacts/linux:
#   ./publish-linux.sh x64 arm64 && ./package-linux.sh 1.2.3 x64 arm64
# Runs on Linux (file modes and owners have to be right inside the packages) with dpkg-deb and
# rpmbuild; on Debian or Ubuntu, `apt install rpm` brings the latter. Either architecture
# packs on any host, since nothing is compiled here.
#
# The packages install the app to /usr/lib/readout, with a launcher in /usr/bin/readout, a
# .desktop file, an icon and AppStream metadata. The version is the tag without its v: 1.2.3,
# or 1.2.3-beta.1 for a prerelease, which becomes 1.2.3~beta.1 so it sorts before 1.2.3.
#
# Debian's dependencies are trixie's: GTK 4.12 or later rules out bookworm (4.8), and ICU's
# package is named after its version.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PUBLISH="$SCRIPT_DIR/Readout.Linux/bin/publish"
FILES="$SCRIPT_DIR/packaging/linux"
OUT="$SCRIPT_DIR/artifacts/linux"

if [ $# -lt 1 ]; then
  echo "Usage: $0 <version> [x64] [arm64]" >&2
  exit 1
fi
VERSION="$1"
shift
ARCHS=("${@:-x64}")

PKG_VERSION="${VERSION/-/\~}"
if [[ ! "$PKG_VERSION" =~ ^[0-9]+(\.[0-9]+)*(~[0-9A-Za-z.]+)?$ ]]; then
  echo "Version '$VERSION' should look like 1.2.3 or 1.2.3-beta.1." >&2
  exit 1
fi
DEB_VERSION="$PKG_VERSION-1"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$OUT"

# The file tree both formats share, in $2.
stage() {
  local app="$PUBLISH/linux-$1/readout" root="$2"
  if [ ! -f "$app/Readout" ]; then
    echo "No build in $app: run ./publish-linux.sh $1 first." >&2
    exit 1
  fi
  mkdir -p "$root/usr/lib" "$root/usr/bin" "$root/usr/share/applications" \
    "$root/usr/share/icons/hicolor/scalable/apps" "$root/usr/share/metainfo"
  cp -r "$app" "$root/usr/lib/readout"
  # .NET's LTTng tracing provider: optional, and it links liblttng-ust, which nothing else needs.
  rm -f "$root/usr/lib/readout/libcoreclrtraceptprovider.so"
  # With an icon in the app's hicolor folder, the GTK backend writes its own
  # ~/.local/share/applications/md.alejandro.readout.desktop at every launch, which hides the
  # packaged one (and outlives the package). Without it, the desktop finds the window's icon
  # through the app id, md.alejandro.readout, in the packaged .desktop file.
  rm -rf "$root/usr/lib/readout/hicolor"
  cp "$FILES/readout" "$root/usr/bin/readout"
  cp "$FILES/md.alejandro.readout.desktop" "$root/usr/share/applications/"
  cp "$FILES/md.alejandro.readout.metainfo.xml" "$root/usr/share/metainfo/"
  # The app icon with its background, as on the website.
  cp "$SCRIPT_DIR/docs/icon.svg" "$root/usr/share/icons/hicolor/scalable/apps/md.alejandro.readout.svg"

  find "$root" -type d -exec chmod 755 {} +
  find "$root" -type f -exec chmod 644 {} +
  chmod 755 "$root/usr/bin/readout" "$root/usr/lib/readout/Readout" "$root/usr/lib/readout/createdump"
}

for ARCH in "${ARCHS[@]}"; do
  case "$ARCH" in
    x64) DEB_ARCH=amd64 RPM_ARCH=x86_64 ;;
    arm64) DEB_ARCH=arm64 RPM_ARCH=aarch64 ;;
    *) echo "Unknown architecture '$ARCH' (x64 or arm64)." >&2; exit 1 ;;
  esac

  echo "==> readout_${DEB_VERSION}_$DEB_ARCH.deb"
  root="$WORK/deb-$ARCH"
  stage "$ARCH" "$root"
  mkdir -p "$root/usr/share/doc/readout" "$root/DEBIAN"
  {
    echo "Readout: https://github.com/amd989/readout"
    echo
    cat "$SCRIPT_DIR/LICENSE"
    echo
    cat "$SCRIPT_DIR/THIRD_PARTY_NOTICES.md"
  } > "$root/usr/share/doc/readout/copyright"
  chmod 644 "$root/usr/share/doc/readout/copyright"
  cat > "$root/DEBIAN/control" <<EOF
Package: readout
Version: $DEB_VERSION
Architecture: $DEB_ARCH
Maintainer: amd989 <amd989@users.noreply.github.com>
Installed-Size: $(du -sk "$root/usr" | cut -f1)
Depends: libgtk-4-1 (>= 4.12), bluez, libicu76 | libicu78 | libicu77 | libicu74 | libicu72, libssl3t64 | libssl3, libc6, libgcc-s1, libstdc++6
Recommends: alejandro-md-keyring
Section: electronics
Priority: optional
Homepage: https://github.com/amd989/readout
Description: companion app for Bluetooth multimeters like the EEVblog 121GW
 Readout shows the meter's display, graphs readings over time, logs and
 exports them as CSV, and presses the meter's buttons remotely, over
 Bluetooth LE through BlueZ.
 .
 The Linux build is a preview on the GTK4 backend for .NET MAUI.
EOF
  dpkg-deb --root-owner-group -Zxz --build "$root" "$OUT/readout_${DEB_VERSION}_$DEB_ARCH.deb"

  echo "==> readout-$PKG_VERSION-1.$RPM_ARCH.rpm"
  root="$WORK/rpm-$ARCH"
  stage "$ARCH" "$root"
  mkdir -p "$root/usr/share/licenses/readout"
  install -m 644 "$SCRIPT_DIR/LICENSE" "$SCRIPT_DIR/THIRD_PARTY_NOTICES.md" "$root/usr/share/licenses/readout/"
  rpmbuild -bb --quiet --target "$RPM_ARCH" \
    --define "_topdir $WORK/rpmbuild" \
    --define "_version $PKG_VERSION" \
    --define "_stagedir $root" \
    "$FILES/readout.spec"
  cp "$WORK/rpmbuild/RPMS/$RPM_ARCH/"*.rpm "$OUT/"
done

echo "==> Done:"
ls -lh "$OUT"/*.deb "$OUT"/*.rpm
