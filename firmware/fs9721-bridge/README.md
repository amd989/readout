# Readout bridge for FS9721 meters

An ESP32 inside (or clipped onto) a cheap meter built on Fortune's FS9721_LP3 chip. It reads
the chip's serial output and forwards it over Bluetooth LE to Readout, which shows, logs and
graphs the readings like a 121GW's. `docs/FS9721.md` explains the chip and how to find its pins.

The bridge doesn't decode anything: it sends the bytes as they arrive, and the app frames and
decodes them. It advertises as **Readout FS9721 XXXX** (the last four hex digits of its MAC).

## What you need

- An ESP32 board: a classic ESP32 dev board (`esp32dev`) or an ESP32-C3 board such as the C3
  SuperMini (`esp32c3`, smaller and lighter on the battery).
- A PC817 (or similar) optocoupler, a 680 Ω and a 10 kΩ resistor.
- A small switch for ENTX, so serial can be turned off: while it's on, the meter's auto
  power-off is off.
- A battery for the ESP32 of its own, such as a 1S LiPo with a charger board. Don't power it
  from the meter's 9 V battery: Bluetooth draws tens of milliamps, far more than the meter.

## Wiring

```
 Meter (FS9721 side)                          ESP32 side

 ENTX ──── switch ──── VSS (button common)

 TXD ──── 680 Ω ──── PC817 pin 1 (anode)      PC817 pin 4 (collector) ──┬── GPIO 16 (esp32dev)
 VSS ──────────────── PC817 pin 2 (cathode)                             │   GPIO 20 (esp32c3)
                                                                        └── 10 kΩ ── 3.3 V
                                              PC817 pin 3 (emitter) ────── GND
```

- **VSS** is the chip's negative supply. The push buttons' common side is VSS, which is also
  where ENTX goes; it's a much bigger pad to solder to than the ones around the chip.
- The optocoupler keeps the ESP32 apart from the meter's circuit, which floats at whatever the
  probes touch. With it the signal arrives inverted, which the firmware expects
  (`METER_RX_INVERTED=1`).
- Adding parts and wires inside the case shrinks the gaps its safety rating relies on. Treat a
  modified meter as a low-voltage bench meter.

### Bench test without the optocoupler

For a first try, TXD can go straight to the GPIO and VSS to GND. Flash the `-direct` build
(`pio run -e esp32dev-direct -t upload`), which reads the signal the right way up. The ESP32 is then connected to the meter's circuit:
keep the test leads out of anything but low voltages, and never measure mains while the ESP32
is on USB.

## Build and flash

With [PlatformIO](https://platformio.org) (the CLI, or the VS Code extension):

```bash
cd firmware/fs9721-bridge
pio run -e esp32dev -t upload     # or -e esp32c3
pio device monitor                # 115200 baud
```

The monitor prints each frame as it's forwarded, so you can check the wiring before opening
the app:

```
Readout bridge 0.1.0: FS9721 on GPIO 16, through an optocoupler (inverted)
advertising as "Readout FS9721 A1B2"
17 25 3b 4f 5f 67 7f 87 9f a0 b0 c0 d4 e8
```

Every line should run `1x 2x … ex`. Lines of other bytes mean the inversion setting is wrong
for your wiring. When nothing arrives for 5 seconds, the bridge says what the input pin is doing:

- *always high*: the meter isn't sending. Check ENTX; the RS232 symbol should be on.
- *always low*: TXD or ground isn't connected (or is on the wrong pin), or the inversion is wrong.
- *changing, but no bytes*: the inversion setting is wrong.

The LED blinks with each frame (GPIO 2 on dev boards, GPIO 8 on the C3 SuperMini).

## Settings

In `platformio.ini`:

| Flag | What it is |
|---|---|
| `METER_RX_INVERTED` | 1 through an optocoupler, 0 wired directly |
| `METER_RX_PIN` | The GPIO TXD reaches |
| `METER_SYMBOLS` | The meter's four maker-chosen LCD symbols, bit 0 first (`°F,hFE,°C,` on the Miyako 890G) |
| `LED_PIN`, `LED_ACTIVE_LOW` | The status LED, or -1 for none |

## Bluetooth layout

One service, matching `Readout.Core/Protocol/BridgeBluetooth.cs`:

| UUID | | |
|---|---|---|
| `1ac73563-3941-40bc-90d4-47fb89f88271` | Service | Advertised |
| `4d19a30c-94d7-404e-a69d-b513188f13dc` | Notify | The meter's bytes, one notification per frame |
| `6daa6868-77cd-4539-8b02-6a82b3c6bc0c` | Read | `chip=FS9721`, `symbols=…`, `firmware=…`, one per line |

The app doesn't read the info characteristic yet; it uses the Miyako 890G's symbols.
