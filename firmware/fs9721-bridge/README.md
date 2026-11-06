# Readout bridge for FS9721 meters

An ESP32 inside (or clipped onto) a cheap meter built on Fortune's FS9721_LP3 chip. It reads
the chip's serial output and forwards it over Bluetooth LE to Readout, which shows, logs and
graphs the readings like a 121GW's. `docs/FS9721.md` explains the chip and how to find its pins.

The bridge doesn't decode anything: it sends the bytes as they arrive, and the app frames and
decodes them. It advertises as **Readout FS9721 XXXX** (the last four hex digits of its MAC).

It turns on and off with the meter: after 10 seconds without data (the meter is off, or its
serial output is) it goes to deep sleep, and the meter's next frame wakes it. The app then
reconnects by itself.

## What you need

- An ESP32 board: a classic ESP32 board such as an ESP-WROOM-32 DevKit (`esp32dev`), or an
  ESP32-C3 board such as the C3 SuperMini (`esp32c3`).
- An optocoupler: a PC817 (or similar), or the slotted optical switch from an old ball mouse
  (pins A, K, C, E). A 330 Ω resistor for its LED and a 10–47 kΩ pull-up (10 kΩ for a PC817,
  more for a mouse switch, which passes less light).
- A small switch for ENTX, so serial can be turned off: while it's on, the meter's own auto
  power-off is off.
- Power of its own (below). Not the meter's 9 V battery: keeping that isolated would take a DC-DC
  converter that wastes more than the ESP32 uses.

## Wiring

There are two ways to light the optocoupler's LED. Pick one, and flash the matching build.

### TXD lights the LED (3 wires out of the meter) — `esp32dev`, `esp32c3`

```
 Meter (FS9721 side)                          ESP32 side

 ENTX ──── switch ──── VSS (button common)

 TXD ──── 330 Ω ──── LED anode (A, pin 1)     collector (C, pin 4) ──┬── GPIO 33 (esp32dev)
 VSS ──────────────── LED cathode (K, pin 2)                         │   GPIO 3  (esp32c3)
                                                                     └── 10–47 kΩ ── 3.3 V
                                              emitter (E, pin 3) ────── GND
```

Only TXD, VSS and ENTX leave the meter (two, with the ENTX switch inside the case). The LED is
lit whenever the meter is on and idle, which costs the meter's battery about 1–2 mA, about as
much as the meter itself: it lasts roughly half as long. The signal arrives inverted, which these
builds expect.

### VDD lights the LED (4 wires, least drain) — `esp32dev-vdd`, `esp32c3-vdd`

```
 VDD ──── 330 Ω ──── LED anode (A)
 TXD ──────────────── LED cathode (K)         (ESP32 side as above)
```

The LED lights only during the 0 bits of each byte, so it draws next to nothing between frames,
and the signal arrives the right way up. VDD is the chip's positive supply, about 3 V above VSS:
the chip pad next to AGND and VSS (docs/FS9721.md), or measure for it.

### Either way

- **VSS** is the chip's negative supply. The push buttons' common side is VSS, which is also
  where ENTX goes; it's a much bigger pad to solder to than the ones around the chip.
- The optocoupler keeps the ESP32 apart from the meter's circuit, which floats at whatever the
  probes touch. Don't connect ESP32 GND to the meter.
- The input has to be a pin that can wake the chip from deep sleep: one of the RTC pins on a
  classic ESP32 (such as 4, 25–27 or 32–39), GPIO 0–5 on an ESP32-C3.
- A mouse's slotted switch: cover the slot so room light doesn't switch it, and check K to E
  reads open once it's off the mouse's board (mice tie them together on the board).
- Adding parts and wires inside the case shrinks the gaps its safety rating relies on. Treat a
  modified meter as a low-voltage bench meter.

### Bench test without the optocoupler

For a first try, TXD can go straight to the input pin and VSS to GND, with a `-vdd` build (the
signal isn't inverted). The ESP32 is then connected to the meter's circuit: keep the test leads
out of anything but low voltages, and never measure mains while the ESP32 is on USB.

## Power

A 1S LiPo with a charger, into the board's **3V3 pin** through a regulator that draws little
while idle:

```
LiPo ── TP4056 module (with protection) ── HT7333 or MCP1700-3302 ── 3V3 pin
                                                                GND ── GND
```

- Use the 3V3 pin, not 5V or VIN. A dev board's own regulator (often an AMS1117) needs about
  4.4 V in and draws around 5 mA doing nothing.
- Take a TP4056 module with battery protection (a DW01 chip next to it, B+/B− and OUT+/OUT−
  pads): it cuts off before the cell runs flat.
- Remove the board's power LED, which draws 1–3 mA all the time.
- Don't connect USB and the battery's regulator at the same time.

While connected it draws roughly 30–60 mA (less on an ESP32-C3); asleep, a dev board still
draws a few hundred microamps through its USB chip, which a 500 mAh cell lasts weeks on.

## Build and flash

With [PlatformIO](https://platformio.org) installed, from the repository's root:

```bash
./deploy-esp.sh --log             # finds the board, builds, flashes, then follows its output
./deploy-esp.sh esp32dev-vdd      # another build: see Wiring
```

Or with PlatformIO directly (the CLI, or the VS Code extension):

```bash
cd firmware/fs9721-bridge
pio run -e esp32dev -t upload     # or esp32dev-vdd, esp32c3, esp32c3-vdd: see Wiring
pio device monitor                # 115200 baud
```

The monitor prints each frame as it's forwarded, so you can check the wiring before opening
the app:

```
Readout bridge 0.2.0 (powered on): FS9721 on GPIO 33
advertising as "Readout FS9721 A1B2"
17 25 3b 4f 5f 67 7f 87 9f a0 b0 c0 d4 e8
…
no data for 10 s: sleeping until GPIO 33 goes low
```

Every line should run `1x 2x … ex`. Lines of other bytes, the same every frame, mean the build
doesn't match the wiring: flash the other one (with or without `-vdd`). When nothing arrives
for 5 seconds, the bridge says what the input pin is doing, and how far its voltage swings:

- *always high*, flat near 3.3 V: no light reaches the optocoupler. Check ENTX (the RS232 symbol
  should be on), that the LED's A and K are the right way round for your wiring, and its wires.
- *always low*: the optocoupler is stuck on (room light in a mouse switch's slot, C and E
  swapped), or the build doesn't match the wiring.
- *changing, but no bytes*, or a swing that dips but stays above about 0.8 V: too little light.
  Use a larger pull-up (47 kΩ) or a smaller LED resistor.

The LED blinks with each frame (GPIO 2 on dev boards, GPIO 8 on the C3 SuperMini). After a
wake-up the first line says "woken by the meter".

## Settings

In `platformio.ini`:

| Flag | What it is |
|---|---|
| `METER_RX_PIN` | The GPIO the signal reaches; must be able to wake the chip |
| `METER_RX_INVERTED` | 1 when TXD lights the LED; 0 when VDD does, or TXD is wired straight to the pin. Set by the build you pick |
| `SLEEP_AFTER_S` | Deep sleep after this many seconds without data (10); 0 never sleeps |
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
