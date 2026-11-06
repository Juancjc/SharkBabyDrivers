# Attack Shark X11 — HID protocol

Derived from the official **AttackShark.exe v1.1** (Inno Setup "Attack Shark Software", DuiLib, class `CMS_4_Page`
plus the shared "classic" mouse writer) and cross-checked with the community drivers
[mousectl](https://github.com/D3m0nZOnFire/mousectl) and [OpenSharkX11](https://github.com/clevim/OpenSharkX11).
Verified on a real X11 (firmware `0109`) over the 2.4 GHz dongle: reads, and an idempotent write of reports
0x04/0x05/0x06 that the mouse accepted and echoed back byte-for-byte.

## Transport

| | |
|---|---|
| VID / PID | `1D57:FA60` (2.4 GHz dongle, shared by several Attack Shark mice), `1D57:FA55` (USB cable) |
| Config collection | interface 2, usage page `0x000B`, feature reports (64-byte collection) |
| Event collection | interface 2, usage page `0x000A`, 5-byte input reports |

The official app always sends the report-specific length (0x38, 0x0F, 0x09, 0x3B…); Windows accepts it.

## Reading (backup) — report 0xA0 handshake

```
SetFeature  A0 rid len 00 flag 00 00 00        (flag = 0 for 0x0B, 1 otherwise)
sleep ≥ 250 ms
GetFeature  A0 → byte[1] == 01 means "ready"   (00 = mouse not answering: asleep / out of range)
GetFeature  rid (len bytes)
```
Official read order: `0B` (8 bytes, firmware id), `06` (9), `05` (15), `04` (56). Over 2.4 GHz the receiver
answers `status 00` by itself while the mouse sleeps — keep retrying until it wakes (move/click the mouse).
Buttons (0x08) and macros (0x09) **cannot be read back**; the app keeps them in its own profile.

## Applying and recovering saved profiles

Before every Apply, read the actual 0x06/0x05/0x04 configuration inside the device queue. Never use only
the editor's cached baseline to decide which reports to send: the mouse may have reset or changed since
that baseline was read. The preflight also confirms the mouse is awake before sending write-only commands.

Loading/importing a saved profile forces 0x04, 0x05, 0x06, its referenced macros (0x09), and the complete
button table (0x08), even when the file matches the app's previous state. Send buttons last, after macro data.
Ordinary Apply also resends known app-owned button assignments and referenced macros: another configurator
can change just the side buttons while all readable reports still match. Before this app has ever written
buttons, preserve the safeguard against writing assumed factory assignments during unrelated edits.
Read the configuration again after all writes and accept the new baseline only when it matches. This
confirms the readable reports; buttons and macros still cannot be verified by reading this firmware.

Keep the recovery request and draft after an error so Apply can be retried without reloading the device.
Discarding edits cancels the full-profile recovery request. DPI events must not replace a staged DPI
selection or mutate a write in progress. The editor is disabled while writing. Drift found during preflight
and the result of each Apply are recorded in `sharkbabydrivers.log`; the app does not automatically rewrite
the mouse in the background.

## Report 0x04 — DPI, colours, sensor (56 bytes)

| Byte | Meaning |
|---|---|
| 0–2 | `04 38 01` |
| 3 | angle snap (0/1) |
| 4 | ripple control (0/1) |
| 5 | enabled-stage bitmask (8 stages) |
| 6, 7 | "doubled DPI" bitmask (stage > 10 000 DPI); byte 7 = byte 6 |
| 8–15 | DPI low byte per stage (PAW3311 table, 220 entries — identical to the one in AttackShark.exe @0x4E50A8) |
| 16–23 | DPI high byte per stage (0 = 50-DPI steps, 1 = 100-DPI steps) |
| 24 | active stage (1-based) |
| 25–48 | RGB per stage (8 × 3) |
| 49 | one-shot animation (official app sends `02`) |
| 50–51 | checksum: 16-bit sum of bytes 3..49, big-endian |

## Report 0x05 — lighting / power / debounce (15 bytes)

| Byte | Meaning |
|---|---|
| 3 | mode << 4 (0 off, 1 static, 2 breathing, 3 neon, 4 colour breathing, 5 static DPI, 6 breathing DPI) |
| 4 | high nibble: deep-sleep minutes (high nibble) · low nibble: 9 − speed (speed 1–8) |
| 5 | high nibble: deep-sleep minutes (low nibble) · low nibble: brightness 1–8 (forced to 8 outside static modes) |
| 6–8 | colour RGB |
| 9 | sleep / light-off timer in 0.5-minute units |
| 10 | debounce = ms / 2 |
| 11–12 | checksum: 16-bit sum of bytes 3..10, big-endian |

The firmware ignores 0x05 until it has seen a 0x04; send 0x04, wait ~300 ms, then 0x05.

## Report 0x06 — polling (9 bytes)
`06 09 01 rate ~rate 00 00 00 00` — rate `08`=125, `04`=250, `02`=500, `01`=1000 Hz.

## Report 0x08 — buttons (59 bytes)
`08 3B 01` + 18 slots × `(action, p1, p2)` + checksum (16-bit sum of bytes 3..56, big-endian in 57–58).
Physical buttons: slot 0 left, 1 right, 2 middle, 5 DPI, 6 forward, 7 back; slots 16/17 = wheel up/down.
Action codes: 01 disabled, 02 left, 03 right, 04 middle, 05 back, 06 forward, 07 double click, 08 fire,
09/0A wheel, 0B/0C tilt, 0D DPI cycle, 0E/0F DPI ±, 10 easy aim, 11 keyboard (`p1` modifiers, `p2` HID usage),
12 macro (`p2` = slot index + 1), 15–1E media, 20–26 browser/system.

## Report 0x09 — macros
Logical packet of 131 bytes: `09 83 slot mode 00 00 00 repeat …`, byte 28 = pair count, pairs from byte 29:
`(delay/10 | 0x01 pressed, 0x81 released), key`; delays > 1270 ms add a pair `(n × 200 ms, 03)`.
Checksum: 16-bit sum of bytes 3..128, big-endian in 129–130. Sent as three 64-byte pages
`09 40 slot page` + 60 bytes, 200 ms apart.

## Others
* `0C 0A 01 FE 01 FE 00 00 00 00` — factory reset.
* `0B` — **read only**. Writing it un-pairs the dongle (documented by OpenSharkX11).
* Input events: `03 55 40 01 xx` battery %, `03 55 41 …` charging, `03 55 10 n` DPI stage changed on the mouse.
