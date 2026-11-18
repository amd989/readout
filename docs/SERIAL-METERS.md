# Meters with a data output (survey)

Which of the meters HKJ reviewed on lygte-info.dk could feed Readout, through the ESP32 bridge
or on their own, and which chip and protocol each one uses. The aim is to pick the next
drivers after `Fs9721Driver`.

Source list: the [DMM selection table](https://lygte-info.dk/info/DMMReviews.html), 123
meters reviewed from 2017 to 2020. The chip for each meter comes from the teardown in its
review. Where HKJ couldn't read the chip (an epoxy blob or a sanded marking), the chip is
marked *(community)* and comes from
[improwis.com's list of DMM chips](https://www.improwis.com/projects/reveng_multimeters/).
Protocol details come from libsigrok's decoders (`src/dmm/*.c`) and the
[sigrok Multimeter ICs page](https://sigrok.org/wiki/Multimeter_ICs).

## The short version

| Driver | Wire format | Reviewed meters it would cover | How the bridge gets the data |
|---|---|---|---|
| **DTM0660** (done, untested on a meter) | 2400 8N1, 15 bytes, FS9721 segments with each nibble's bits reversed | Aneng V8, Mestek DM91A, UNI-T UT139C; *(community)* Aneng AN8203, AN860B, AN8002, BSide ZT301, ZT302 | Tap TX (pin 20). Serial is off until a bit in the meter's I²C EEPROM is set |
| **ES51922** (and ES51932) | 19200 7O1, 14 bytes ASCII-ish | UNI-T UT61E, Mastech MS8040, Mastech MS8340B | Phototransistor on the meter's IR port. No opening the meter |
| **FS9922** (DMM3/DMM4) | 2400 8N1, 14 bytes ASCII-ish | Mastech MS2108A | Tap TXD (pin 92), enable with TXEN (pin 94) |
| **FS9721** (done) | 2400 8N1, 14 bytes | BTMeter BT-570C-APP (already sends it over its own BLE); maybe Mustool MT8206 (CS7721CN, pin-compatible clone) and VC105 | As now |

DTM0660 was the cheapest win and is done (`Readout.Core/Dtm0660`, the bridge's `dtm0660-*`
builds): turning each byte's segment bits around gives exactly an FS9721 frame, so
`Fs9721Decoder` reads it, and a 15th byte adds MAX, MIN and auto power-off. It still needs a
capture from a real meter. ES51922 is the best user experience:
the UT61E and the two Mastechs have an optical port, so the bridge clips on without opening
the meter.

## Every reviewed meter with an interface, or a chip known to have one

### One-way serial from the meter's chip

These fit the bridge as it is: it only listens.

| Meter | Chip (from the review) | Output today | Notes |
|---|---|---|---|
| BTMeter BT-570C-APP | FS9721-LP3 | BLE (Dialog DA14580 module) | "The RS232 is always on and is used for Bluetooth connection." The module most likely forwards the FS9721 frames as they are: Readout might connect directly, no bridge. Needs a BLE scan to find its service. |
| UNI-T UT61E | ES51922A | Optical RS232 cable | sigrok `uni-t-ut61e-ser`: 19200 7O1, 14 bytes. |
| Mastech MS8040 (bench) | ES51922A | Optical to DB9, one LED transmits | PC-Link key turns output on. |
| Mastech MS8340B | ES51922A | USB box (CP2102 behind an optocoupler), one way | PC-Link hold turns output on. |
| Mastech MS2108A (clamp) | FS9922-DMM3 | None | TXD pin 92, TXEN pin 94 (improwis pin table). |
| Aneng V8 | DTM0660L | None | EEPROM K24C08. |
| Mestek DM91A | DTM0660L | None | EEPROM T24C02A. |
| UNI-T UT139C | DTM0660L | None | EEPROM T24C02A. |
| Mastech MS8218 | FS9704 + HD64738024 microcontroller | Optical, one LED, "hold HOLD for RS232" | Protocol unknown; needs a capture. The microcontroller makes the frames, not the FS9704. |
| VC105 | Hidden under the display | ~2400 baud on the USB-shaped connector, through an optocoupler | HKJ: "probably segment information". Possibly FS9721-family; needs a capture. |
| Victor 98A+ | HY12P65 + MSP430 | USB (CP2102 behind two optocouplers) | The meter side is a UART into the optocouplers. Protocol unknown; sigrok's "Victor protocol" is for the HID cables of other Victor models. |
| BSide ADM20 | DTA0660L + HY11P14 | USB (CH340G inside, fed by a TX LED) | The HY11P14 makes the frames. Protocol unknown. |
| UNI-T UT258A (clamp) | MSP430 | RS232 levels (SP3232, ±13 V) on a mini-USB socket | Needs a level shifter. Protocol unknown. HKJ found the firmware unreliable. |
| Vici VC8145 (bench) | FS9704 + FSUP01 | DB9 RS232 through optocouplers, powered by the PC's handshake lines | Bench meter; the FS970x datasheet shows the FSUP01 interface. Low priority. |

### Two-way optical ports (request and response)

The PC must send a request, so the bridge would need an IR LED and a write characteristic.

| Meter | Protocol | Notes |
|---|---|---|
| Brymen BM525s, BM829s | Brymen BM52x/BM82x, published by Brymen | sigrok `brymen-bm52x`; same parser, 0x52 vs 0x82 in requests. "It is the display segments, not digits that is transmitted." |
| Brymen BM869s | Brymen BM86x, published by Brymen | sigrok `brymen-bm86x`; request `00 00 86 66`. sigrok reaches both Brymen families through the BU-86X USB cable, so the bare IR signalling still has to be checked. |
| Fluke 289 | Fluke remote interface: 115200 8N1, ASCII (`QM<CR>`) | Documented in Fluke's remote spec for the 287/289; open-source tools exist (`f289ctrl`). |
| Keysight U1282A, U1233A | Keysight IR, SCPI-like text | Bluetooth through Keysight's U1117A/U1177A adapter. |
| UNI-T UT171A | UNI-T, through a HID adapter | USB must be turned on in the meter's settings each time. |
| Kyoritsu KEW1062, Hioki DT4282, Prova 903 | Proprietary | Expensive adapters, no public protocol found. Not worth it now. |

### Bluetooth already in the meter (direct drivers, no bridge)

| Meter | Chip | Protocol |
|---|---|---|
| Owon B35T | CS7729CN | 6-byte BLE notifications, reverse-engineered ([owonb35](https://github.com/DeanCording/owonb35)). Older B35Ts with an FS9922 send something else ([inflex/owon-b35](https://github.com/inflex/owon-b35)). |
| Owon B41T(+) | ES51932 | Owon's BLE format (three int16 per notification), documented by [owonb41t](https://www.npmjs.com/package/owonb41t). |
| BTMeter BT-570C-APP | FS9721-LP3 | See above. |
| Southwire 16040T | DTA0660L + MSP430 | Unknown. |
| EEVblog 121GW | HY3131 | Done. |

### Chips that may have a hidden UART, unconfirmed

- **HY12P65 / DM1106EN** (Mustool X3, MT109, Zeast282, WH5000A, Klein MM600, UNI-T UT210E;
  *(community)* Aneng AN8008, Brymen BM235): the chip has UART pins, but people who tried on
  the Zeast282 and the UT210E got nothing out, and some think the option is fixed in the chip's
  OTP. Skip until someone shows a working capture.
- **CS7721CN** (Mustool MT8206): improwis lists it as pin-compatible with the FS9721-LP3. In
  the MT8206 a second chip drives the display, so the CS7721CN's TX may still carry frames.
  Plain CS7721 (Mastech MS8216) is listed without serial.
- **ES51926A** (UNI-T UT118B), **ES51995A** (Amprobe AM-570): not in sigrok; no protocol found.
- **UNI-T UT33D+**: RXD and TXD pads on the board, probably for factory calibration.
- **DTA0660L with a second processor** (UNI-T UT195E, UT191T as DTA0661L, UT139S, BSide
  ADM20, Southwire 16040T): the processor drives the display, so the front end's own serial
  output, if it has one, wouldn't carry the reading.

### Not candidates

- **No serial output**: Sanwa PS8a and PM3 use the FS9711-LP1/FS9721-LP1, which have no TX
  pin. 7106-family and most epoxy-blob meters have no serial output either.
- **Bench meters with USB or LAN SCPI** (East Tester ET3240, Siglent SDM3045X, Keysight 34461A,
  Keithley DMM6500): no bridge needed. The app could talk SCPI over the network directly,
  which is a separate feature.

## What the bridge firmware would need

`firmware/readout-bridge` picks its chip at build time (`CHIP_DTM0660`, `CHIP_DSO150`, else the
FS9721), each with its own UART settings, and `BridgeBluetooth.ModelFromName` maps the chip in
the advertised name to a driver. Decoding happens in the app, so:

1. **FS9922**: the FS9721's UART settings. A `CHIP_FS9922` profile and a decoder in the app,
   as the DTM0660 got.
2. **ES51922**: 19200 7O1. The profile sets `UART_BAUD` and the serial config (`SERIAL_7O1`,
   which `chip.begin` doesn't take from a flag yet). An IR phototransistor on the meter's port
   usually inverts the signal, which `UART_RX_INVERTED` already handles. The flush gap must
   suit 19200 baud and the chip's frame rate (not checked yet).
3. **Two-way meters (Brymen, Fluke, Keysight)**: the DSO150 work built most of this: the RX
   characteristic, `IMeterConnection.SendAsync`, the two-way profile and MTU 247. What's left
   is an IR LED driven from `UART_TX_PIN` (with `UART_TX_INVERTED` if the LED's transistor
   inverts), the meter's own baud and serial config, and a driver that polls through
   `SendAsync` (Brymen and Fluke only answer requests). Drivers today only receive, so
   `IMeterDriver` needs a way to ask the session to send.

Each new chip also needs real captures in `Readout.Tests/TestData/`, like `fs9721-890g.txt`.

## Enabling serial on a DTM0660 meter

Wiring and the bridge builds are in `firmware/readout-bridge/README.md`.

From Kerry Wong's write-up and the sigrok DTM0660 page: the chip's TX is pin 20 (LQFP-64), and
serial output is turned on by a bit in the external 24C02/24C08 EEPROM. The EEPROM also holds
the meter's calibration, so dump it first and change only that bit. According to sigrok, the
datasheet says other EEPROM settings give 14-byte frames in the FS9721's bit order: those
meters work with the `fs9721-*` bridge builds as they are. The default 15-byte format needs the
`dtm0660-*` builds.
