# Packs an already built tree (package-linux.sh stages it) into an rpm, so it builds on any
# host with rpmbuild, the release workflow's Ubuntu runner included, for either architecture.
# Defines: _version (like 1.2.3 or 1.2.3~beta.1) and _stagedir (the tree to pack).

# Prebuilt, self-contained .NET: nothing to compile, strip or scan. Without AutoReqProv the
# host's view of the binaries' libraries doesn't leak into Requires; they are listed below.
%global debug_package %{nil}
%global __os_install_post %{nil}
%global __arch_install_post %{nil}
%global _build_id_links none

Name:           readout
Version:        %{_version}
Release:        1
Summary:        Companion app for Bluetooth multimeters like the EEVblog 121GW
License:        MIT
URL:            https://github.com/amd989/readout
AutoReqProv:    no

# The GTK4 backend needs GTK 4.12 or later. .NET loads ICU and OpenSSL itself, and only
# OpenSSL 3: without libssl.so.3 the app aborts at its first random number (the MCP token).
# Fedora 45 moved to OpenSSL 4 and keeps 3 in openssl3-libs, so ask for the library itself.
Requires:       gtk4 >= 4.12
Requires:       bluez
Requires:       libicu
Requires:       libssl.so.3()(64bit)
Requires:       glibc
Requires:       libgcc
Requires:       libstdc++
# The repository's key and .repo file, so updates keep coming. Not required: the package also
# installs from a downloaded file.
Recommends:     alejandro-md-release

%description
Readout shows the meter's display, graphs readings over time, logs and exports them as CSV,
and presses the meter's buttons remotely, over Bluetooth LE through BlueZ.

The Linux build is a preview on the GTK4 backend for .NET MAUI.

%install
cp -a %{_stagedir}/. %{buildroot}/

%files
%license /usr/share/licenses/readout/LICENSE
%license /usr/share/licenses/readout/THIRD_PARTY_NOTICES.md
/usr/bin/readout
/usr/lib/readout
/usr/share/applications/md.alejandro.readout.desktop
/usr/share/icons/hicolor/scalable/apps/md.alejandro.readout.svg
/usr/share/metainfo/md.alejandro.readout.metainfo.xml
