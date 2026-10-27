# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project

Readout is a companion app for the EEVblog 121GW multimeter over Bluetooth LE, built with .NET 10 and .NET MAUI. It's open source (MIT) and meant for the App Store, Google Play and the Microsoft Store.

- `Readout.Core/` (plain `net10.0`, warnings are errors): the protocol (`Protocol/`: `MeterPacket`, `PacketFramer`, `MeterCommands`, `MeterBluetooth`), LCD decoding (`Display/`: `ReadingDecoder`, `ModeInfo` range tables, `SiFormat`), logging and CSV (`Logging/`), the simulated meter (`Simulation/`), and the connection contract plus session (`Connection/`: `IMeterConnection`, `SimulatedMeterConnection`, `MeterSession`). No MAUI or Bluetooth stack here, so a future Linux front end can reuse it.
- `Readout/`: the MAUI app. `Services/BleMeterConnection` implements `IMeterConnection` over Plugin.BLE. `ViewModels/` use CommunityToolkit.Mvvm. `Drawing/` draws the LCD (`LcdDrawable`, `SevenSegment`) and the trend graph with MAUI Graphics.
- `Readout.Core/Logging/`: `MeasurementLog` (the live log), `SampleCsv` (the CSV format, written and read), `SessionStore` (saved sessions: `{id}.csv` + `{id}.json` in the app data folder), `RecordingAutosave` (appends the live log to `recording.csv` every 10 s and on sleep, restored at launch). `Display/TrendWindow` is the graph's zoom and pan; `Display/TrendBucket` groups long logs into time slots (the graph draws each slot's average as the line over a faint min/max band, so noisy readings don't fill the plot).
- `Readout.Core/Feedback/`: `ContinuityAlert` (on/off with hysteresis for continuity and resistance) and `ToneWave` (the looping beep). `Readout/Services/ContinuityFeedback` plays it through `ContinuityTone` (per platform) and vibrates; `AppSettings` holds the Settings tab's values in Preferences.
- Navigation is a Shell `TabBar` (Meter, Sessions, Settings). On iOS and Mac it gets no Shell colors on purpose, so iOS 26 draws the system Liquid Glass bar; Android and Windows get the `BrandShell` style.
- `Readout/Views/TrendView` is the graph control (pinch, drag, double-tap when `Interactive`; `RecentSeconds` limits it to the newest stretch, as on the meter page); `GraphPage` is it full screen, `SessionsPage` and `SessionDetailPage` list and show saved sessions.
- `Readout.Tests/`: xUnit v3 tests for the core.
- `docs/PROTOCOL.md`: the packet format. Read it before touching `Protocol/` or `Display/`.

The protocol is ported from the official EEVblog app's `PrivatePostRelease` branch (https://gitlab.com/eevblog/app-121gw, MIT, David Ledger), and matches EEVblog's "Revised Packet Format Blob V2" document. Its `master` branch has an obsolete 52-character text parser; ignore it. Keep the David Ledger credit in THIRD_PARTY_NOTICES.md and the About page.

## Commands

```bash
dotnet test --project Readout.Tests
dotnet build Readout -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64
dotnet build Readout -f net10.0-maccatalyst
dotnet build Readout -f net10.0-android
./deploy-ios.sh        # physical iPhone; device names come from .devices (git-ignored)
./deploy-mac.sh        # build and launch the Mac app (--log follows its output)
./publish-ios.sh       # Release .ipa for TestFlight / App Store
```

## Things to know

- `MeterSession` raises its events through `Dispatch`, which the app sets to the main thread. The log is only touched there.
- Simulators have no Bluetooth: Debug builds on a simulator or emulator (or with `READOUT_FAKE_BLE=1`) scan the simulated meter. "Try the demo meter" exists in every build, for App Review, which has no 121GW.
- Per-machine settings stay out of git: `Directory.Build.local.props` (Mac Debug code signing; see the `.example`) and `.devices` (iPhone UDIDs for `deploy-ios.sh`).
- The name "121GW" belongs to EEVblog. Use it only to say what the app works with ("for the 121GW"), never as the app's name or in its icon.
- After editing an `Info.plist`, build with `-t:Rebuild`: an incremental build can keep a stale compiled plist.
