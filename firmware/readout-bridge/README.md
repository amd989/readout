# Readout bridge

An ESP32 wired to a chip's serial port that forwards its bytes over Bluetooth LE to Readout,
which frames and decodes them. The bridge doesn't decode anything: all protocol knowledge
lives in the app (`Readout.Core`) and, for the DSO150, in its firmware.

The chip is picked at build time. Each build advertises as **Readout &lt;chip&gt; XXXX** (the
last four hex digits of its MAC), and the app picks the driver from that name.

| Chip | Builds | Direction | Serial | Wiring | Sleeps |
|---|---|---|---|---|---|
| FS9721 (cheap meters) | `fs9721-esp32dev`, `fs9721-esp32dev-vdd`, `fs9721-esp32c3`, `fs9721-esp32c3-vdd` | meter to app | 2400 8N1 | optocoupler | after 10 s without data |
| DSO150 running [DSOShell](#dso150-oscilloscope) | `dso150-esp32dev`, `dso150-esp32c3` | both ways | 115200 8N1 | direct, 3.3 V | never |

Boards: a classic ESP32 such as an ESP-WROOM-32 DevKit (`esp32dev`), or an ESP32-C3 such as
the C3 SuperMini (`esp32c3`).

## Build and flash

With [PlatformIO](https://platformio.org) installed, from the repository's root:

```bash
./deploy-esp.sh --log                    # fs9721-esp32dev: finds the board, builds, flashes, follows its output
./deploy-esp.sh dso150-esp32c3 --log     # another build
```

Or with PlatformIO directly (the CLI, or the VS Code extension):

```bash
cd firmware/readout-bridge
pio run -e dso150-esp32c3 -t upload      # any build from the table above
pio device monitor                       # 115200 baud
```

## FS9721 meters

An ESP32 inside (or clipped onto) a cheap meter built on Fortune's FS9721_LP3 chip. It reads
the chip's serial output and forwards it, and Readout shows, logs and graphs the readings like a
121GW's. `docs/FS9721.md` explains the chip and how to find its pins.

It turns on and off with the meter: after 10 seconds without data (the meter is off, or its
serial output is) it goes to deep sleep, and the meter's next frame wakes it. The app then
reconnects by itself.

### What you need

- An ESP32 board (above).
- An optocoupler: a PC817 (or similar), or the slotted optical switch from an old ball mouse
  (pins A, K, C, E). A 330 Ω resistor for its LED and a 10–47 kΩ pull-up (10 kΩ for a PC817,
  more for a mouse switch, which passes less light).
- A small switch for ENTX, so serial can be turned off: while it's on, the meter's own auto
  power-off is off.
- Power of its own (below). Not the meter's 9 V battery: keeping that isolated would take a DC-DC
  converter that wastes more than the ESP32 uses.

### Wiring

There are two ways to light the optocoupler's LED. Pick one, and flash the matching build.

#### TXD lights the LED (3 wires out of the meter): `fs9721-esp32dev`, `fs9721-esp32c3`

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

#### VDD lights the LED (4 wires, least drain): `fs9721-esp32dev-vdd`, `fs9721-esp32c3-vdd`

```
 VDD ──── 330 Ω ──── LED anode (A)
 TXD ──────────────── LED cathode (K)         (ESP32 side as above)
```

The LED lights only during the 0 bits of each byte, so it draws next to nothing between frames,
and the signal arrives the right way up. VDD is the chip's positive supply, about 3 V above VSS:
the chip pad next to AGND and VSS (docs/FS9721.md), or measure for it.

#### Either way

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

#### Bench test without the optocoupler

For a first try, TXD can go straight to the input pin and VSS to GND, with a `-vdd` build (the
signal isn't inverted). The ESP32 is then connected to the meter's circuit: keep the test leads
out of anything but low voltages, and never measure mains while the ESP32 is on USB.

### Power

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

### Checking it

The monitor prints each frame as it's forwarded, so you can check the wiring before opening
the app:

```
Readout bridge 0.3.0 (powered on): FS9721 on GPIO 33, inverted
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

## DSO150 oscilloscope

A JYE Tech DSO150 pocket oscilloscope running DSOShell, an open firmware with a serial
protocol (text lines, plus IEEE 488.2 `#<d><len>` binary blocks for waveforms; DSOShell's
`docs/PROTOCOL.md`). The bridge is a transparent two-way pipe: what the scope sends goes to the
app as notifications on DATA, and what the app writes to RX goes out to the scope unchanged.
It never sleeps (the scope says nothing until asked) and powers up with the scope.

### Wiring

Both sides are 3.3 V logic, so the UART goes straight across:

```
 DSO150                                      ESP32
                                             dso150-esp32dev   dso150-esp32c3
 UART TX (PA9)  ──────────────────────────── GPIO 16 (RX)      GPIO 20 (RX)
 UART RX (PA10) ──────────────────────────── GPIO 17 (TX)      GPIO 21 (TX)
 GND ─────────────────────────────────────── GND

 9 V input ──── buck converter to 3.3 V ──── 3V3 pin
 GND ──────────────────────────────────────── GND
```

- **Power**: from the scope's 9 V input through a small buck converter (an MP1584 module set
  to 3.3 V, or a fixed 3.3 V buck), into the ESP32's 3V3 pin. Not from the scope's own 3.3 V
  LDO: it can't supply the ESP32's radio peaks (a few hundred mA for a moment) and the scope
  would brown out. A buck after the scope's power switch turns the bridge on and off with it.
- **Ground**: the ESP32's ground is the scope's ground, which is also the probe's ground clip.
  With the bridge on its own power and no USB cable, nothing else ties the clip to earth. While
  the ESP32 is on USB to a PC (flashing, the serial monitor), the clip is at the PC's ground:
  don't clip it to anything that isn't at earth potential then.
- Where the PA9 and PA10 pads are on the board is in DSOShell's docs. Phase 5 of the plan adds
  two more wires (BOOT0 and NRST) so the app can update the scope's firmware; not needed yet.

### Hardware test (DSOShell milestone 0)

The M0 firmware answers `*IDN?`, `SYST:INFO?`, `INP?` and `HELP?` and sends `EVT:KEY` and
`EVT:ENC` lines when buttons are pressed and the encoder turns. That's enough to check the
whole path.

1. Flash DSOShell M0 to the scope (DSOShell's README). Optional first check without the
   bridge: a USB-serial adapter on PA9/PA10 at 115200, type `*IDN?` and Enter.
2. Wire the bridge as above and flash it: `./deploy-esp.sh dso150-esp32c3 --log` (or
   `dso150-esp32dev`). The monitor says:

   ```
   Readout bridge 0.3.0 (powered on): DSO150 at 115200 baud, RX on GPIO 20, TX on GPIO 21
   advertising as "Readout DSO150 A1B2"
   ```

3. In **nRF Connect** (phone or desktop), scan and connect to **Readout DSO150 XXXX**. On
   Android, ask for MTU 247 from the menu (iOS negotiates by itself); the monitor prints
   `mtu 247: notifications of up to 244 bytes`.
4. In service `1ac73563-…`:
   - read INFO (`6daa6868-…`): `chip=DSO150`, `baud=115200`, `firmware=0.3.0`.
   - turn on notifications for DATA (`4d19a30c-…`).
   - write to RX (`3065c3ca-…`) the bytes `2A 49 44 4E 3F 0A` (`*IDN?` and a newline;
     nRF Connect's text mode doesn't add the newline, so send it as a byte array).
5. DATA notifies `DSOShell,DSO150,<uid>,<fw version>` and a newline. The monitor shows both
   directions: `<- *IDN?` and `-> DSOShell,DSO150,…`. Try `SYST:INFO?`
   (`53 59 53 54 3A 49 4E 46 4F 3F 0A`) and `HELP?` (`48 45 4C 50 3F 0A`) too: `HELP?`'s reply
   is several lines and may arrive in one notification.
6. Press the scope's buttons and turn its encoder: `EVT:KEY,down=0x..,up=0x..` and
   `EVT:ENC,<pos>,<delta>` lines arrive on DATA without asking.
7. With **Readout**: the scan lists **Readout DSO150 XXXX**; connect. The meter page stays empty
   (the DSO150 driver makes no readings yet), and the app's log shows
   `mtu … (asked for 247)`, `connected, data on …, commands on 3065c3ca-…` and a
   `dso150 rx …` line for each notification, so pressing the scope's buttons shows its
   `EVT:KEY` lines (Mac: `./deploy-mac.sh --log`; Windows: the debugger's output). Readout has
   no screen to type commands yet, so the `*IDN?` round trip is nRF Connect's.

If nothing comes back: swap TX and RX (the most common mistake), check the grounds are joined,
and check the scope answers on its own (step 1).

### Throughput

A waveform frame is about 1.1 KB (1024 samples and a header), so the DSO150 builds are tuned
for it:

- MTU 247 on both ends; each notification carries up to the negotiated MTU less 3 (244 bytes,
  or 20 when the app didn't negotiate).
- A notification goes out when it's full, or when the scope pauses for 3 ms (the end of a line
  or a frame). The UART hands bytes over every 16, and a 4 KB buffer holds what arrives while
  the radio is busy.
- When NimBLE's buffers are full the bridge waits and retries instead of dropping bytes (twice
  NimBLE's default buffers, too). Bytes it still has to drop are counted on the monitor; the
  app drops the frame (its CRC fails) rather than show a broken one.
- It asks the app for a 15–30 ms connection interval.

At 115200 baud the serial link is the limit: about 11.5 KB/s, so roughly 10 frames a second
at most with nothing else on the line. BLE with 244-byte notifications carries more than that
on current phones.

**Open decision: faster serial.** DSOShell's `SYST:BAUD` switches the scope up to 921600 for
streaming waveforms. This bridge stays at 115200 for now: after `SYST:BAUD` the scope and the
bridge no longer understand each other until the scope resets. The two ways forward:

1. **The bridge follows `SYST:BAUD`**: it watches the bytes the app writes for the command and
   the scope's `OK`, then switches its own UART. The bridge stays a byte pipe in every other
   way, but this is the one place it would peek at the protocol.
2. **A fixed 921600 link**: DSOShell boots at 921600 when it detects the bridge (or always), and
   the bridge builds for 921600 (`-D UART_BAUD=921600`). Simpler, but a serial terminal then
   needs 921600 too.

## Settings

In `platformio.ini`:

| Flag | What it is |
|---|---|
| `CHIP_DSO150` | Build for the DSO150 (two-way); without it, the FS9721 |
| `UART_RX_PIN`, `UART_TX_PIN` | The GPIOs the chip's TX reaches and its RX is driven from. For the FS9721, RX must be able to wake the chip |
| `UART_RX_INVERTED` | FS9721: 1 when TXD lights the LED; 0 when VDD does, or TXD is wired straight to the pin. Set by the build you pick |
| `UART_BAUD` | 2400 for the FS9721, 115200 for the DSO150 |
| `SLEEP_AFTER_S` | Deep sleep after this many seconds without data (10 for the FS9721); 0 never sleeps (the DSO150's default) |
| `METER_SYMBOLS` | FS9721: the meter's four maker-chosen LCD symbols, bit 0 first (`°F,hFE,°C,` on the Miyako 890G) |
| `LED_PIN`, `LED_ACTIVE_LOW` | The status LED, or -1 for none. It blinks with each chunk, both ways |

## Bluetooth layout

One service, matching `Readout.Core/Protocol/BridgeBluetooth.cs` (a test checks the UUIDs, the
name format and the MTU against this firmware):

| UUID | | |
|---|---|---|
| `1ac73563-3941-40bc-90d4-47fb89f88271` | Service | In the scan response (the name is in the advertisement) |
| `4d19a30c-94d7-404e-a69d-b513188f13dc` | Notify | The chip's bytes as they arrive, up to MTU less 3 per notification |
| `6daa6868-77cd-4539-8b02-6a82b3c6bc0c` | Read | `chip=FS9721` or `chip=DSO150`, `baud=…`, `firmware=…` and, for the FS9721, `symbols=…`, one per line |
| `3065c3ca-b78a-4b98-96b6-280cf0784ec4` | Write, write without response | DSO150 builds only: bytes for the chip, out the UART unchanged |

The app writes to RX with a response (a write longer than the MTU allows goes as a long write,
up to 512 bytes). It doesn't read the info characteristic yet; for the FS9721 it uses the
Miyako 890G's symbols.
