# Readout

A companion app for the **EEVblog 121GW** multimeter, for iPhone, iPad, Mac, Android,
Windows and Linux. Built with .NET 10 and .NET MAUI.

**[Website](https://amd989.github.io/readout/)** · [Support](https://amd989.github.io/readout/support.html) ·
[Privacy](https://amd989.github.io/readout/privacy.html) · [The 121GW](https://www.eevblog.com/product/121gw/)

<a href="https://apps.microsoft.com/detail/9P80492G34GN">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/ms-store-badge-light.svg">
    <img src="docs/ms-store-badge-dark.svg" width="161" height="44" alt="Get it from Microsoft">
  </picture>
</a>

<p>
  <img src="docs/screenshots/iphone-meter.png" width="200" alt="Live DC voltage reading with a graph">
  <img src="docs/screenshots/iphone-ac-dark.png" width="200" alt="AC voltage and frequency in dark mode">
  <img src="docs/screenshots/iphone-session.png" width="200" alt="A saved battery discharge session">
  <img src="docs/screenshots/iphone-sessions.png" width="200" alt="The list of saved sessions">
</p>

<img src="docs/screenshots/ipad-landscape.png" width="820" alt="iPad in landscape: the meter in a side column and a large graph">

- **Live LCD.** A drawing of the meter's display: main and sub readings, units, the bar graph
  and every symbol.
- **Remote buttons.** Press any of the meter's eight buttons from the app, short or long.
- **Logging.** Every reading is logged while connected, with a live trend graph and
  min/max/average/peak-to-peak. Logging keeps running with the screen locked (iOS), and the
  recording is kept on disk, so closing the app doesn't lose it.
- **Readable graphs.** The meter screen shows the last 10 minutes; full screen shows the whole
  recording, with long recordings drawn as an average line over a min/max band. Pinch to
  zoom, drag to pan.
- **Saved sessions.** Save a recording with a name and notes; each keeps its graph and
  statistics.
- **CSV export.** Timestamped readings in SI units, ready for a spreadsheet.
- **Continuity beep.** In continuity and resistance modes the phone beeps and vibrates below
  a threshold you set, with hysteresis so it doesn't chatter.
- **Signal and battery.** The connection line shows the meter's signal strength and battery.
- **Reconnects by itself** to the last meter when it comes back in range (can be turned off
  in Settings).
- **Demo meter.** Try the app without a meter (simulated readings).
- **Works with Claude (Mac, Windows and Linux).** An optional, local-only MCP server lets an AI
  assistant read the meter, look at recent readings, press buttons and save sessions. See
  [docs/MCP.md](docs/MCP.md).

> Readout is an independent, open-source project. It is not made, endorsed or supported
> by EEVblog. 121GW and EEVblog are trademarks of their owners.

## Platforms

| Platform | Status |
|---|---|
| iOS / iPadOS 15+ | ✅ |
| macOS (Mac Catalyst) 12+ | ✅ |
| Android 6+ | ✅ |
| Windows 10/11 | ✅ builds on Windows |
| Linux (GTK 4.12+, BlueZ) | Preview: MAUI's GTK4 backend, see [Linux](#linux) |

## Repository layout

| Path | What |
|---|---|
| `Readout.Core/` | Everything about the meter that isn't UI or a Bluetooth stack: packet parsing, LCD decoding, key commands, logging and CSV, the simulated meter. Plain `net10.0`. |
| `Readout.Mcp/` | The MCP server for AI assistants: the tools and a small loopback HTTP host, on the official MCP C# SDK. Plain `net10.0`. |
| `Readout/` | The .NET MAUI app: Bluetooth through Plugin.BLE, view models, pages, the LCD and graph drawings. |
| `Readout.Linux/` | The Linux build of the same app, on MAUI's GTK4 backend, with Bluetooth through BlueZ. |
| `Readout.Tests/` | xUnit tests for the core, the MCP server and the BlueZ helpers. |
| `tools/linux-test/` | Runs the Linux build in Docker, with the simulated meter or a stand-in for BlueZ. |
| `docs/PROTOCOL.md` | The 121GW Bluetooth protocol, documented. |
| `docs/` | Also the GitHub Pages site (`index.html`, `privacy.html`, `support.html`) and the web screenshots. |
| `store/` | Full-size App Store screenshots. |

## Building

You need the .NET 10 SDK and the MAUI workload (`dotnet workload install maui`), plus Xcode
for Apple targets and the Android SDK for Android.

```bash
dotnet test --project Readout.Tests
```

```bash
dotnet build Readout -f net10.0-ios
```

```bash
dotnet build Readout -f net10.0-maccatalyst
```

```bash
dotnet build Readout -f net10.0-android
```

On Windows, `-f net10.0-windows10.0.19041.0`. The Linux build is a separate project that
builds anywhere, without the MAUI workload:

```bash
dotnet build Readout.Linux
```

Simulators and emulators have no Bluetooth. Debug builds running on one use the simulated
meter when you scan (set `READOUT_FAKE_BLE=1` to do that anywhere). Every build also has
**Try the demo meter** on the connect screen.

## Publishing

`./publish-ios.sh` builds the signed iPhone/iPad `.ipa` and refuses it if any binary carries a
slice other than arm64, which App Store Connect rejects. `./publish-mac.sh` builds the signed
Mac App Store `.pkg` (Apple silicon and Intel). Upload with Transporter.

`./publish-windows.ps1` builds the Microsoft Store packages: an unsigned `.msix` each for x64
and arm64 in `artifacts/windows` (the Store signs them). Release Windows builds are MSIX; Debug
builds stay a plain `.exe`. `-Test -Install` builds and installs a local test copy (Developer
Mode), and `-Wack` also runs the Windows App Certification Kit from an administrator shell.

`./publish-linux.sh x64 arm64` builds self-contained Linux tarballs
(`Readout.Linux/bin/publish/Readout-linux-<arch>.tar.gz`); it runs on macOS and Windows too.

<img src="docs/screenshots/mac-meter.png" width="820" alt="The Mac app with the meter and a wide graph">

## Linux

`Readout.Linux` runs the same app on Linux, on Microsoft's GTK4 backend for .NET MAUI
([dotnet/maui-labs](https://github.com/dotnet/maui-labs/tree/main/platforms/Linux.Gtk4), a
preview). It compiles the app's own pages and code; only the Bluetooth connection and a few
fixes for gaps in the preview backend are Linux's own. Bluetooth goes through BlueZ over
D-Bus. The MCP server for AI assistants works there too.

To run it, unpack a tarball from `./publish-linux.sh` and start `readout/Readout`. It's
self-contained (no .NET install); the machine needs GTK 4.12 or later (`libgtk-4-1`) and, for
the meter, BlueZ running with a powered Bluetooth LE adapter. BlueZ's default policy lets any
local user scan and connect, so nothing needs root. `READOUT_FAKE_BLE=1` uses the simulated
meter, and **Try the demo meter** works as everywhere else.

What's different on Linux:

- Tabs sit at the top, and pages pushed on top of a tab get a back button in the title bar.
- Graphs zoom with the mouse wheel (or a touchpad), pan by dragging, and reset on a double-click.
- Export CSV opens a save dialog instead of a share sheet.
- BlueZ reports a meter's signal strength only while scanning, so once connected the signal
  bars show the strength from the scan and hide after 30 seconds without a new one.
- No continuity beep or vibration yet (as on Windows). Alert boxes show no title.

`tools/linux-test/` runs it in Docker (Ubuntu 24.04) with the simulated meter, or against
`mock-bluez.py`, a stand-in for BlueZ that plays a 121GW. It hasn't been tried with a real
meter on Linux yet.

## Credits

The meter protocol comes from the official EEVblog 121GW app by David Ledger (MIT). See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## License

[MIT](LICENSE). Privacy: [PRIVACY.md](PRIVACY.md) (no data collected).
