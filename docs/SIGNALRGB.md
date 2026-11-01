# SignalRGB integration (X68 PRO HE)

Measured on 2026-11-01 with an X68 V2 (id 2901, USB fw 0x0505) through the receiver `3151:5026` and the cable
`3151:502F`, SignalRGB 2.5.84, a webcam aimed at the keyboard (mean RGB of the keyboard area, 30 fps).

## Why the keyboard flickered

The stock plugin (`Plugins/AttackShark/Attack_Shark_X68_Pro_HE_Keyboard.js`, PIDs `5026` and `502D` only) sends
`SetLedParam 0x07 [01 04 04 07 R G B]` on **every** `Render()`.

| Test (5 s, 30 Hz, same colour) | Busy/idle events (`0F 01`/`0F 00`) | Camera brightness, keyboard area |
|---|---|---|
| idle | 0 | 26.6 ± 0.33 |
| `0x07` repeated (what the stock plugin does) | **296** (one pair per packet) | 24.0 ± 1.32, min 20.4 (−23 %), 17 jumps > 10 % |
| mode 21 + `0x0E` repeated | **0** | 26.5 ± 0.36 (same as idle) |

Each `0x07` is stored in flash (the keyboard reports busy/idle for it) and restarts the effect: that is the
flicker, plus ~30 flash writes per second. `0x0E` (SET_SCREEN_COLOR, `0E R G B 00 00 00 cksum7`) only feeds the
host-driven "screen colour" effect (mode 21); it is not stored and causes no busy report. A colour sequence
red → off → white → blue → green streamed with `0x0E` was followed exactly by the keyboard (blue and green look
weak under red keycaps; a bare switch shows the real colour).

## Why the plugin does not talk to the keyboard

In a SignalRGB **HID** plugin, `udp` (after `device.addFeature("udp")`) only has `send(host, port, bytes)` and
two signals; `udp.createSocket`, `bind` and `on` are not available and nothing can be received (checked with a
diagnostic plugin reporting through the keyboard LEDs). So SignalRGB cannot be told to pause or stop. Instead:

```
SignalRGB effect ─► plugin (integrations/signalrgb) ─UDP 127.0.0.1:47851─► SharkBabyDrivers ─0x0E─► keyboard
                                                     "SBD1" v1 type pid R G B          (same queue as every read/write)
```

* SharkBabyDrivers is the **only** program writing to the keyboard. Colours go through `X68Device.RunAsync`
  (the device's single queue): during a backup, *Apply* or profile load they wait, and afterwards only the newest
  colour is sent. Colours are sent when they change, at most ~30/s, plus a refresh every 2 s.
* **Owner = SignalRGB:** `LightingLocked` makes `WriteImage` skip lighting and LED pictures (Apply, profile load,
  backup restore); the keyboard is put in mode 21 once (its previous `0x87` bytes are kept in `settings.json`).
* **Owner = SharkBabyDrivers:** frames are ignored (nothing reaches the keyboard) and the kept lighting is written
  back. A profile or backup taken while SignalRGB owned the lights never writes mode 21.
* A user plugin in `Documents\WhirlwindFX\Plugins` replaces the stock one for the same ids (SignalRGB log:
  "HID plugin with id 0x3151:0x5026 already exists. Overwriting with new path").
* One connection at a time: while the cable (`502F`/`502D`) is present the receiver (`5026`) is not opened. Each
  connection has its own SignalRGB device; the bridge uses the frames of the connection in use, falling back to
  the newest frames of any instance.
* Nothing is written before the device has its ORIGINAL backup. No profile switch (`0x04`) is ever sent.

## Limitations

* SharkBabyDrivers must be running (the tray is enough; "Start with Windows" opens it there). If it is closed,
  the keyboard keeps its last colour.
* With SharkBabyDrivers in control, SignalRGB keeps its (idle) HID handle and the plugin keeps sending UDP to
  localhost; the keyboard receives nothing. Disabling the device in SignalRGB stops that too.
* One colour for the whole keyboard (`0x0E` has no per-key form on this firmware). Per-key colour exists only as
  the LED pictures `0x0C`, which are stored in flash (7 packets per picture) — streaming them per frame would bring
  back the flicker and the flash wear. Per-key streaming (`0xE8` in the monsgeek project) needs patched firmware.
* Over the receiver, ~15–20 colours/s reach the keyboard (each relayed write waits for the RF link);
  over the cable, ~30/s. While the keyboard sleeps the colours are lost until it wakes.

## Idle light-off (receiver)

While colours keep arriving the keyboard's own "light off after" timer (`0x11`, 2.4G idle at bytes 10–11) never
fires. With SignalRGB in control the pump therefore applies the same value itself: after that many seconds
without a key press **on the Attack Shark keyboard** (Raw Input, `VID_3151` only, key-down timestamps only) it
sends black once and stops sending; the next key press sends the current colour again. Over the cable it is off
(the keyboard does not sleep there). Validated 2026-11-01 with 30 s: dark ~30 s after the last key (no traffic
while dark), back on the next key press (log + camera).

Sleep timers are never written when out of the firmware range (0–64800 s): the cable ORIGINAL backup of
2026-10-31 holds garbled timers (247 s / 65108 s) read while SignalRGB was flooding the link, and the keyboard
was found with garbled timers (254/255/1790/65364 s) — they were rewritten to the receiver ORIGINAL values
(180/180/1620/1620 s) before the test.

## Validation (2026-11-01, user observation + `lighting.log`)

1. SignalRGB in control, receiver: followed the effects, no flicker.
2. Switch to SharkBabyDrivers: keyboard went steady; `sent` stopped while frames kept arriving.
3. Back to SignalRGB: mode 21 again, streaming resumed, no restart of either program.
4. Cable plugged: switched to `502F`, read in 5 s, resumed; unplugged: back to `5026`, resumed after the read.
5. Apply in the app while SignalRGB in control: typing normal, lights resumed afterwards.
