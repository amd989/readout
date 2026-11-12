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
| Linux (GTK 4.12+, BlueZ) | Preview: MAUI's GTK4 backend, see [Linux](https://github.com/amd989/readout/wiki/Linux) |

## Building

You need the .NET 10 SDK and the MAUI workload (`dotnet workload install maui`), plus Xcode
for Apple targets and the Android SDK for Android.

```bash
dotnet test --project Readout.Tests
```

```bash
dotnet build Readout -f net10.0-maccatalyst
```

No meter? Every build has **Try the demo meter** on the connect screen.

## Documentation

The [wiki](https://github.com/amd989/readout/wiki) has the rest:

- [Building](https://github.com/amd989/readout/wiki/Building): every platform, code coverage, running on a device.
- [Publishing](https://github.com/amd989/readout/wiki/Publishing): store packages, Linux tarballs, releasing from a tag.
- [Linux](https://github.com/amd989/readout/wiki/Linux): running it, what's different there, testing in Docker.
- [Repository layout](https://github.com/amd989/readout/wiki/Repository-Layout): what each project holds.

In this repository: [the 121GW protocol](docs/PROTOCOL.md), [the MCP server](docs/MCP.md),
[FS9721 meters through an ESP32 bridge](docs/FS9721.md) and [releases from CI](docs/STORE-CI.md).

<img src="docs/screenshots/mac-meter.png" width="820" alt="The Mac app with the meter and a wide graph">

## Credits

The meter protocol comes from the official EEVblog 121GW app by David Ledger (MIT). See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## License

[MIT](LICENSE). Privacy: [PRIVACY.md](PRIVACY.md) (no data collected).
