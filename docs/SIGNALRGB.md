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

## Per-key colour: what the stock firmware can and cannot do (diagnosed 2026-11-01)

Question: can SignalRGB effects with several colours at once (background X + wave Y) be shown key by key, live?
**Not on the stock firmware (id 2901, fw 0x0505).** Evidence, all without sending anything to the keyboard:

| Kind | Command | Stored? | Live? | Evidence |
|---|---|---|---|---|
| 1. Global colour, live | mode 21 + `0x0E` R,G,B | no | **yes** (one colour, whole keyboard) | measured: no busy events, no flicker |
| 2. Static multi-colour picture | `0x0C` SET_USERPIC (5 slots, 7 pages × 56 B, matrix order) + mode 13 | **flash** | no | official app `_setLightPic` + `vendorSleep`; RY5088 flash map has "User Pictures / LEDs" at 0x0802F800 |
| 3. Per-key animation upload | `0x12` SET_USERGIF (start, then frames with `frameNum`/`frameDelay`) | **flash** (300 ms erase wait after start) | no — the firmware plays the uploaded loop | official app `setUserGifStart`/`setUserGif`; not offered for the X68 (mode 25 absent from its light layout) — never sent |
| Per-key live streaming | — | — | **does not exist** | official `iot_driver` streams only `Music2` (`0x0D` band levels) and `Screen` (`0x0E`); the monsgeek project, which disassembled RY5088 firmware, had to **add** `0xE8 LED_STREAM` in a patched firmware, and its tools refuse stock firmware |

So the single zone in the chain (plugin `Size [1,1]` → `RgbFrame` with one RGB → `ScreenColorPump` → `0x0E`) is not
what limits the effect: there is no command that would apply more colours live. Sending more zones would have
nowhere to go; emulating a stream with `0x0C`/`0x12` would rewrite flash on every frame (flicker and wear), and
alternating one colour quickly would be a fake. Flashing a patched firmware is out of scope (no firmware updates).

What *is* possible, and implemented: a **static** multi-colour picture (kind 2) captured once from SignalRGB's
layout and written on request to a user-picture slot — not an animation.

### Can the keyboard be made to "recognise" per-key animation? (researched 2026-11-01)

* **`0x12` SET_USERGIF is ignored by RY5088 firmware.** Disassembly of `vendor_command_dispatch` (0x08013328) in the
  monsgeek project's RY5088 firmware 2949 v408 (same platform/SDK as the X68): commands above 0x0D branch on 0x0E,
  0x10, 0x11, 0x1B, 0x1C, 0x1E and 0x40–0x44; **0x12 falls through to the exit (0x08013AC4) without doing anything**.
  The official app's GIF upload belongs to another keyboard family (its variant uses command ids 24/25), and the X68's
  light layout has no "user colour" effect (25). It was never sent to the keyboard.
* The flash area for LED pictures is 2048 B (`userpics` 0x0802F800–0x0802FFFF: room for the 5 pictures only), right
  before `calib_data` (0x08032000) — there is no space for stored animation frames.
* The only other live command is **`0x0D` SET_AUDIO_VIZ** (music modes 20/22, offered for the X68 by the official app):
  16 band levels, rendered as **column bars** (pairs of bands per column, ~6 rows high), with the firmware's rainbow or
  one solid colour — live per-column brightness, but not SignalRGB's per-key colours. Not implemented.
* Real per-key streaming needs new firmware (the monsgeek patch adds `0xE8` for the M1 V5 only). No X68 firmware image
  is available and flashing firmware is out of scope.

### Static picture from SignalRGB (implemented 2026-11-01)

* The plugin (1.1) declares the 80 keys of the X68 V2 at their physical positions (16 × 6 grid, table generated from
  the app's layout by `SharkBabyDrivers.Cli signalrgb-layout` and checked by a test). Live it still reduces them to one
  colour (*Live Color*: Accent — default: the average of the keys that differ from the dominant background, i.e. the wave
  of a wave effect —, Center, Average or Dominant), because that is all the firmware can show.
* Ten times a second (plugin 1.3.0; twice before) it also sends every key's colour (UDP type 3: count × [slot, R, G, B]).
* *Lighting → Static picture from SignalRGB → Capture* (with a confirmation) writes that snapshot **once** with
  `0x0C` into picture 1–5 (7 packets), reads it back with `0x8C` and rewrites if a packet was lost (receiver), then
  sets effect 13 with that slot (`[4] = slot << 4`, as the official app). The lighting goes back to the app (live sync
  off) without restoring the old effect; choosing SignalRGB again resumes the live sync.
* Validated on the receiver with a "green wave over yellow" effect: 78 keys, 47 colours captured; read back from the
  keyboard: F1–F8 / Esc–9 / Tab–I / Caps–K / Shift–, / Ctrl Win Alt Space green, the rest `FFDC00` yellow; effect
  `0D 04 04 00` (picture 1); the camera shows the bare switches green and the capped keys yellow (orange under red
  caps) at the same time. Over the cable the same commands are used (the official app sends them over USB); the
  stored picture is shown on any link since it lives in the keyboard.
* The saved "lighting before SignalRGB" is now validated (`X68Lighting.IsPlausibleEffect`): a garbled 0x87 answer
  (seen as `87 04 BF 04 FB FF FF FF`) is never stored nor written back; a steady light is used instead.

## Keyboard effects imitating SignalRGB (2026-11-02)

The stock firmware has only two live paths: `0x0E` (one colour for every key) and `0x0D` (music bars in the
firmware's own rainbow). Several colours with movement exist only as the firmware's own effects, which are set with
`0x07` (stored in flash). So, with the style **Keyboard effects** (default; *Live colour only* keeps the old
behaviour), SharkBabyDrivers reads SignalRGB's scene (per-key colours, 10/s since plugin 1.3.0, last 3 s) and picks
the closest look the firmware can show (`SignalRgbScene`, Core/Lighting):

| SignalRGB scene | Keyboard |
|---|---|
| one colour on the keys (≤ 2 % differ from the median), also breathing, flashing, colour cycling | live colour `0x0E` (exact) |
| a pattern moving across the board (2-D shift of the 16 × 6 grid) | **Wave** right/left/down/up, speed from board crossings per second |
| sparse things falling (< 45 % of the keys stand out) | **RainDown** |
| a pattern turning around the centre | **CircleWave** clockwise / counter-clockwise |
| a board mostly lit, brightening and dimming together | **Breath** |
| things lighting up and fading with no direction | **Raindrop** |
| still rainbow / any other still picture | very slow rainbow **Wave** / live colour |

* Colour: the accent, i.e. the keys that clearly differ from each frame's median colour, weighted towards the ones
  that differ most (the wave, the drops — not the background nor the blends at their edges).
* Rainbow (the firmware's only multi-colour variant): the hues lit **at the same time** have no gap wider than 120°
  around the colour circle in most frames; or what stands out from the usual background (drops, spots) is spread
  around the colour circle (circular variance ≥ 0.3: two colours ≥ ~90° apart, or many hues — e.g. Psychedelic
  Dream, Cyber Rain); or the whole board flows through the circle over ~15 s **without a flat background** (≥ 30 %
  of the keys stand out: rainbow rings). A background with one colour passing over it is not a rainbow, even when
  both cycle through the hues (Color Wave). A still picture becomes the slow rainbow wave only when it is a rainbow.
* The firmware effects light the keys in one colour (or the rainbow) on black: a coloured background behind the
  drops / waves cannot be shown; per-key colours exist only as a stored, still picture.
* Motion: on the colours and on an "how much each key stands out" image (things moving over a still background),
  between frames ~0.3 s apart (slow) or consecutive (fast, up to 30 columns/s); a shift is trusted only when it beats
  both "no shift" and the other shifts and most measurements agree; near-ties go to the smallest shift (vertical
  stripes look the same shifted up or down).
* `SceneMirror`: a new effect, direction or rainbow choice is written after 2 s of steady decisions; back to the live
  colour after 10 s (a colour-cycling wave briefly matching its background); a change of only colour, speed or
  brightness after 20 s (so a wave whose colour keeps cycling keeps one colour). At least 8 s between writes, 60 s
  when 4 writes happened in 2 minutes, never more than 20 writes per hour. Each write is one `0x07`, confirmed by a
  `0x87` read-back of **every** field (the colour setter of `X68Image` keeps an existing rainbow flag: the look clears
  it explicitly — found when a red-background/colour-cycle wave stayed a rainbow on the keyboard).
* While an imitating effect is on, the live colour stream is paused (inside the device queue) and
  `KeyboardSignalRgbEffectActive` is set: it is never saved as "the lighting before SignalRGB", a reconnection keeps
  it (no rewrite), and handing the lights back to SharkBabyDrivers restores the saved lighting. SignalRGB's shutdown
  colour brings the live mode back.
* `lighting.log`: one `mirror <look> (<scene numbers>)` line per write, `style=`/`look=`/`scene=[…]` on every tick.

### Validation with SignalRGB's own effects (2026-11-02)

The app records the per-key snapshots when `SBD_SCENE_RECORD` names a file; `SharkBabyDrivers.Cli scene-replay
<file> [fromMs toMs]` replays them through the analysis. 24 effects were applied through SignalRGB's local API
(`POST http://localhost:16038/api/v1/lighting/effects/{id}/apply`, 12 s each) with the style set to live colour only
(nothing written to the keyboard), then replayed; 14 of them are test fixtures
(`tests/.../Fixtures/SignalRgb/*.txt.gz`, ≥ 70 % of the decisions after 3 s must match):

| Keyboard | SignalRGB effects (parameters checked through the API) |
|---|---|
| live colour | Solid Color, Color Cycle, Lightning |
| rainbow wave | Rainbow (right), Rainbow Rise (up), Gradient and Spiral Rainbow (very slow) |
| rainbow wave (several colours) | Neon Sunset Wave (direction Left; pink, orange, purple) |
| single-colour wave | Color Wave (red background, colour-cycling wave, right), Custom Wave (right; one of its two colours covers most keys), Aurora (cyan) |
| rainbow rain down | Rain (colour mode Rainbow), Cyber Rain (yellow and blue), Psychedelic Dream (drops of many hues over dark indigo) |
| single-colour rain down | Drizzle (a narrow range of warm hues), Fire |
| raindrops | Fireflies (hue 0 → red), Bubbles, Meteors, Falling Stars, Snake, Side to Side |
| breath (rainbow) | Quad-Color Breath |

On the keyboard (camera): the firmware single-colour wave is bands of that one colour, brighter and darker, moving;
the rainbow wave sweeps the hues across the keys; raindrops light single keys and fade them.

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
