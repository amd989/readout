# 121GW Bluetooth protocol

What the EEVblog 121GW sends over Bluetooth LE, and how to press its buttons remotely. It
matches EEVblog's own "Revised Packet Format Blob V2" document, and the format the official
app's shipped version decoded (`Packet121GW.cs` on the
`PrivatePostRelease` branch of <https://gitlab.com/eevblog/app-121gw>), written down so
others don't have to dig for it. The code is in `Readout.Core/Protocol` and
`Readout.Core/Display`.

> The `master` branch of that repository still has an older parser for a 52-character
> hex-text packet. The meter's current firmware sends the 19-byte binary packet below.

## Connection

- The meter advertises as **121GW** (older units may advertise the Bluetooth module's default
  name, **Bluegiga**). Turn Bluetooth on at the meter first.
- The radio is a Silicon Labs (Bluegiga) module in its serial "cable replacement" mode. One
  characteristic carries everything:
  **`E7ADD780-B042-4876-AAE1-112855353CC1`**. Subscribe to it (it sends indications) to
  receive packets, and write to it to press keys.
- A packet may arrive split across two indications, or several may arrive together, so
  buffer bytes and look for whole packets (`PacketFramer`).

## Packet: 19 bytes

| Byte | Bits | Meaning |
|---|---|---|
| 0 | | `0xF2`, start of packet |
| 1 | | Year − 2000 (a fixed value, not today's date) |
| 2 | 7–4 | Month |
| 2 | 3–0 | Serial number, digit 1 |
| 3 | 7–4, 3–0 | Serial number, digits 2 and 3 |
| 4 | 7–4, 3–0 | Serial number, digits 4 and 5 |
| 5 | 4–0 | Main mode (table below) |
| 5 | 7–6 | Main value, bits 17–16 |
| 6 | 7 | Main overload ("OFL") |
| 6 | 6 | Main negative |
| 6 | 5 | Main °C |
| 6 | 4 | Main °F |
| 6 | 3–0 | Main range index |
| 7–8 | | Main value, bits 15–0, big-endian |
| 9 | | Sub mode (table below; 100+ are SETUP pages) |
| 10 | 7 | Sub overload |
| 10 | 6 | Sub negative |
| 10 | 5 | Sub "k" |
| 10 | 4 | Sub "Hz" |
| 10 | 3 | Always 0 |
| 10 | 2–0 | Digits after the sub display's decimal point (0–4) |
| 11–12 | | Sub value, big-endian |
| 13 | 4 | Bar graph **off** when set |
| 13 | 3 | Bar tick labels 0–150 |
| 13 | 2 | Bar negative |
| 13 | 1–0 | Bar full scale: 0 = 5, 1 = 50, 2 = 500, 3 = 1000 |
| 14 | 4–0 | Bar value; the LCD lights value + 1 of its 26 segments |
| 15 | 7 | °C symbol |
| 15 | 6 | "1kHz" (AC low-pass filter) |
| 15 | 5 | "1ms" (PEAK) |
| 15 | 4–3 | Coupling: 0 none, 1 DC, 2 AC, 3 AC+DC |
| 15 | 2 | AUTO (range) |
| 15 | 1 | APO (auto power-off) |
| 15 | 0 | Low battery |
| 16 | 7 | °F symbol |
| 16 | 6 | Bluetooth |
| 16 | 5 | Arrow |
| 16 | 4 | REL |
| 16 | 3 | dBm |
| 16 | 2–0 | MIN/MAX: 0 off, 1 MAX, 2 MIN, 3 AVG, 4 all three |
| 17 | 6 | TEST |
| 17 | 5–4 | MEM (0 off) |
| 17 | 3–2 | Hold: 0 off, 1 A-HOLD (auto-hold), 2 HOLD |
| 17 | 1 | AC symbol |
| 17 | 0 | DC symbol |
| 18 | | XOR of bytes 0–17 |

Notes on byte 13:

- **Bit 4.** EEVblog's document labels it "USE", which reads as "set = bar shown". The
  shipped app treats set as *off*, and others have confirmed that on real meters, so this
  code follows the app.
- **Bit 2.** The official app decodes it inverted and then swaps the two LCD images, so the
  net behaviour is "set = negative".

The main mode on the wire is 0–24; 25 and 26 (°C and °F temperature) are what the app
calls mode 5 once it has read the °C/°F bits in byte 6.

## Reading the main value

The LCD has 5 digits. Each mode has a table of ranges; the range index (byte 6, low nibble)
picks how many digits come before the decimal point, and an SI prefix:

```
value = main_value / 10^(5 − integer_digits)
```

then negate it if byte 6 bit 6 is set, and apply the prefix. For example DC volts, range 1
(2 integer digits, no prefix) with main value 12345 reads **12.345 V**.

| Mode | Name | Integer digits per range | Prefix per range |
|---|---|---|---|
| 0 | Low-Z voltage | 4 | – |
| 1 | DC voltage | 1, 2, 3, 4 | – |
| 2 | AC voltage | 1, 2, 3, 4 | – |
| 3 | DC millivolts | 2, 3 | m, m |
| 4 | AC millivolts | 2, 3 | m, m |
| 5 | Temperature (°C/°F from byte 6) | 4 | – |
| 6 | Frequency | 2, 3, 1, 2, 3 | –, –, k, k, k |
| 7 | Pulse width (ms) | 1, 2, 3 | m (always) |
| 8 | Duty cycle | 4 | – |
| 9 | Resistance | 2, 3, 1, 2, 3, 1, 2 | –, –, k, k, k, M, M |
| 10 | Continuity | 3 | – |
| 11 | Diode | 1, 2 | – |
| 12 | Capacitance | 3, 4, 2, 3, 4, 5 | n, n, µ, µ, µ, µ |
| 13 | AC µVA | 3, 4, 4, 5 | µ (always) |
| 14 | AC mVA | 2, 3, 3, 4 | m, m, –, – |
| 15 | AC VA | 4, 5, 2, 3 | m, m, –, – |
| 16 | AC µA | 2, 3 | µ (always) |
| 17 | DC µA | 2, 3 | µ (always) |
| 18 | AC mA | 1, 2 | m, m |
| 19 | DC mA | 1, 2 | m, m |
| 20 | AC A | 3, 1, 2 | m, –, – |
| 21 | DC A | 3, 1, 2 | m, –, – |
| 22 | DC µVA | 3, 4, 4, 5 | µ (always) |
| 23 | DC mVA | 2, 3, 3, 4 | m, m, –, – |
| 24 | DC VA | 4, 5, 2, 3 | m, m, –, – |
| 25 | Temperature °C | 4 | – |
| 26 | Temperature °F | 4 | – |

Sub-display SETUP pages (byte 9): 100 temperature °C, 105 temperature °F, 110 battery,
120 auto power-off on, 125 auto power-off off, 130 year, 135 date, 140 time, 150 burden
voltage, 160 LCD contrast, 180 dBm, 190 logging interval.

What a real meter (2025 firmware) sends, from walking one through every dial position,
MODE step, range and SETUP page:

- SETUP steps the small display through: (the mode's own reading, e.g. Hz on AC V, then
  dBm 180) → temperature 100 → battery 110 → auto power-off 120/125 → buzzer → LCD contrast
  160 → year 130 → date 135 → time 140 → meter ID → logging interval → back. In most modes
  the small display rests on the temperature page.
- The buzzer, meter ID and logging interval pages come as sub mode **0** with a value of 0:
  the packet doesn't say which page it is or what it shows. Only the page before tells them
  apart (the buzzer follows auto power-off; ID and interval follow the time).
- Year: two digits (19 for 2019). Date and time: one byte each in bytes 11 and 12, month
  then day, hour then minute (`0A 0E` is 10-14, `10 2A` is 16:42). Not a 16-bit number.
- Burden voltage comes as **156** on the µA, mA and A positions, not 150.
- dBm (180) is a live reading of the input in AC V, with the dBm symbol lit.
- AC+DC on V is sent as main mode 2 (AC voltage) with coupling 3.
- On the VA position the small display alternates between the current and the voltage.
- Temperature with no thermocouple: the meter shows OFL, but the packet sends 0 with the
  overload bit clear.

### Setting the clock

There's no command for it: the year, date and time pages are edited with the keys, as on the
meter. Hold SETUP on the page and its first field flashes; ▲ (REL) and ▼ (1ms PEAK) step it
by one; hold SETUP to save it, which moves to the second field (day, minute) or ends the
edit. A short SETUP drops the edit and goes to the next page.

- While a field flashes, packets keep showing the saved clock, not the value being edited.
  Each saved field shows up in the next packet.
- A key sent within about a second of the SETUP hold is lost.
- Saving the minute keeps the meter's own seconds, which the packet doesn't carry, so the
  clock can only be set to within a minute.

`MeterClockSync` does this, checking each page after saving it.

Several users on the EEVblog forum reported the VA (power) readings as unreliable, so the
power ranges above are the least certain part of this table.

## Pressing keys

Write five bytes to the data characteristic:

```
F4 k c k c
```

`k` is ASCII `'0'` (0x30) for a short press or `'8'` (0x38) for a long press; `c` is the
key's number as an ASCII digit:

| Key | Number |
|---|---|
| RANGE | 1 |
| HOLD | 2 |
| REL (▲ in SETUP) | 3 |
| 1ms PEAK (▼ in SETUP) | 4 |
| MODE | 5 |
| MIN/MAX | 6 |
| MEM | 7 |
| SETUP | 8 |

For example a short HOLD is `F4 30 32 30 32`, and a long SETUP is `F4 38 38 38 38`.
