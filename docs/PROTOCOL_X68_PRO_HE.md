# ATTACK SHARK X68 PRO HE — HID protocol (reverse-engineered from "Attack Shark Driver v4" 3.1.12)

Scope: the wired protocol was derived statically from the vendor installer
`ATTACK SHARK X68PROHE SOFT.exe`. The 2.4 GHz transport in §5 was subsequently confirmed on a real X68 V2
(device id 2901) with the `3151:5026` receiver; it replaces the earlier, incorrect static interpretation.
Confidence tags: **[CODE]** = read directly from code, **[LIVE]** = confirmed on that keyboard/receiver,
**[INF]** = inferred, **[UNVERIFIED]** = needs a live test / USB capture.

---------------------------------------------------------------------------------------------------

## 0. TL;DR for the implementer

* Config channel = **HID feature reports, Report ID 0, 64-byte payload** on the vendor interface
  (**interface 2 / MI_02, UsagePage 0xFFFF, Usage 0x0002**). With hidapi: `send_feature_report([0x00] + 64 bytes)` /
  `get_feature_report(0x00, 65)` and drop byte 0. **[CODE]**
* Every packet: `byte[0] = command`, `byte[1..6] = params`, `byte[7] = checksum`, `byte[8..63] = payload (56 B)`.
  Checksum "Bit7": `byte[7] = 0xFF - (sum(byte[0..6]) & 0xFF)` (= bitwise NOT of the 8-bit sum). LED packets use "Bit8":
  `byte[8] = 0xFF - (sum(byte[0..7]) & 0xFF)`. Firmware-upgrade packets use no checksum. **[CODE]** (JS `iA.sendFeature`
  and native `iot_driver.exe` @0x5aacd8 / 0x4542a2 both do this.)
* Read = write a GET command (Bit7) → wait ~10 ms → `GetFeature` → 64 bytes. Echoed command in `resp[0]` for single
  GETs; **multi-page GETs (key matrix, macros, HE tables, per-key RGB) return 64 raw data bytes with no header**. **[CODE]**
* Device is identified by **command 0x8F** → `uint32 LE deviceId @resp[1..4]`, `uint16 LE fw version @resp[7..8]`.
  X68 PRO HE = **deviceId 2370** (`ry5088_x68_8k_002`) or **2901** (`ry5088_x68v2_8k`, has an F-row in the matrix).
  Wired USB = **VID 0x3151 PID 0x502F** (8 kHz). **[CODE]**
* `3151:5026` is a **2.4 GHz "dongle_common" receiver (1 kHz)**. It forwards the same 64-byte keyboard packets
  without a prefix; retrieving an RF response needs the receiver-local `F7`/`FC` sequence in §5. **[LIVE]**
* **Onboard profiles: 4** (`layer: 4`). Key matrix / Fn layer GETs take an explicit profile index;
  **HE (magnetic switch) tables and probably lighting apply to the *active* profile only**. **Never send 0x04
  over the dongle, or to X68 V2 (id 2901) even over USB.** `CanSwitchProfiles` stays false until identification,
  and remains false for every dongle and for model 2901. Snapshots of 2901 use GETs only and cover HE of the active
  profile on both links; change profiles physically on the keyboard, then reread. Other identified models retain
  USB profile switching (§7).

---------------------------------------------------------------------------------------------------

## 1. Tech stack & where things live

| Part | What | Path (inside the extracted vendor app) |
|---|---|---|
| Installer | NSIS 3.04, non-solid zlib blocks | `blocks\blk_000..008.bin` (blk_005 = electron-builder 7z) |
| App | Electron + React (Vite build), product name "Attack Shark Driver v4", window title "GearHub", v3.1.12 | `app\resources\app\` |
| Main process | trivial (window/tray only) | `app\resources\app\dist-electron\main.js` |
| Renderer bundle | **all protocol logic** | `app\resources\app\dist\js\index.b078bf5f.js` (beautified: `pretty\index.b078bf5f.js`, 91 k lines) |
| RY5088 keyboard base class | every FEA_CMD encoder/decoder | `dist\js\5e635fe2.js` → `pretty\5e635fe2.js` |
| X68 PRO HE subclasses | default key matrices | `4e9c4eb7.js` (id 2370), `69a807f0.js` (id 2901) |
| Model loader | name → class map | `4a39166d.js` (`findRy5088Device`) |
| Native helper | `iot_driver.exe` — **32-bit Rust** (tonic gRPC server on `http://127.0.0.1:3814`, hidapi, tokio). Does the raw HID I/O, checksums, and the 2.4 G dongle multiplexing. Was BCJ2-packed; extracted with a custom decoder, CRC verified (0x03B78687) | `out\iot_driver.exe` |

Two transports exist in the renderer (`class iA`, index.js ~45352):
1. **IOT SDK (default in the desktop app)** – gRPC-web to `iot_driver.exe`: `sendMsg{msg, devicePath, checkSumType, dangleDevType}`,
   `readMsg{devicePath}`, `sendRawFeature`, `readRawFeature`, `watchDevList`, `watchVender`, `changeWirelessLoopStatus{lock}`.
   iot_driver adds the checksum itself (`CheckSumType`: 0 Bit7, 1 Bit8, 2 None).
2. **WebHID** (`class GT`, `FEATURE_REPORT_SIZE = 64`) – JS pads to 64 B, computes checksum, `sendFeatureReport(0, data)` /
   `receiveFeatureReport(0)`. WebHID refuses 2.4 G dongles ("存在无线设备"), i.e. dongles need iot_driver.

Relevant vendor snippets were kept in the local analysis workspace (not versioned, see §11).

---------------------------------------------------------------------------------------------------

## 2. Device identification

### 2.1 USB IDs **[CODE]**
| Role | VID:PID | Notes |
|---|---|---|
| X68 PRO HE wired (both ids 2370/2901) | **3151:502F** | `$_` list, `reportRate: 8000` |
| X68 HE (non-PRO) wired | 3151:502D | ids 2270, 2472, 2902 (same `Common66_X68` layout) |
| 2.4 G receiver plugged in now | **3151:5026** | `n_` list: `dongle_common: true, reportRate: 1000`; also in iot_driver TOML: `{vid=0x3151,pid=0x5026,usage=0x2,usage_page=0xffff,interface_number=2,dongle_common=true}` |
| Bluetooth (various) | 3151:xxxx | `ch` list, usage_page 0xFF55/0xFF66 usage 0x202 (BLE GATT HID) — not relevant on Windows wired |
| Boot-loader PIDs | 3151:502A, 3151:4036, 3151:504D, 046A:012E/0130 | firmware update only — **never trigger** |

Which wireless keyboard sits behind a dongle is decided at runtime from the dongle status (`keyboardId`), not from the PID.

### 2.2 Interface / collection **[CODE]**
* **Config/feature interface**: `interface_number = 2`, **UsagePage 0xFFFF, Usage 0x0002** (wired and dongle).
* **Event (input report) collection**: UsagePage 0xFFFF, **Usage 0x0001** (`gL` filter = same VID/PID with usage 1;
  on your dongle this is probably `MI_01&COL05`). Used for unsolicited "vendor events" (§6).
* Report ID **0** (unnumbered). Feature report = 64 data bytes (65 with the report-ID byte through hidapi/Windows).
  iot_driver `hid_normal.rs:32` builds `[0x00] + msg` and calls `hid_send_feature_report`. **[CODE]**

### 2.3 Identify the model **[CODE]**
`0x8F` (FEA_CMD_GET_USB_VERSION, Bit7) → `resp[0]==0x8F`, `deviceId = u32 LE resp[1..4]`, `usbFw = resp[8]<<8 | resp[7]`.
Retry up to 3×. If `resp[0]==0` the device did not answer.
Device DB entries (index.js 14403 and 19027):
```
{id:2370, vid:0x3151, pid:0x502F, name:"ry5088_x68_8k_002", displayName:"X68PRO HE", keyLayout:Common66_X68,
 layer:4, fnSysLayer:{win:1, mac:1}, magnetism:true, lightLayout:I,
 other:{isSwitchReplaceable:true, sleep24:{sleep:{min:0,max:64800}}, sleepBT:{sleep:{min:0,max:64800}, deep:{min:10,max:64800}}}}
{id:2901, ... name:"ry5088_x68v2_8k", displayName:"X68PRO HE", same flags}
```
`0x80` (GET_RF_VERSION) → `rf = resp[2]<<8 | resp[1]` (0 = none). `0xAE` → MLED version, `0xAD` → OLED/flash version (not
used by X68).

Firmware version drives two behaviours (`v = rfVersion || usbVersion`) **[CODE]**:
* **travel step multiplier** `k`: `v < 0x300 → 10` (0.1 mm units), `0x300 ≤ v < 0x500 → 100` (0.01 mm), `v ≥ 0x500 → 200` (0.005 mm).
* **top dead-zone supported** iff `v ≥ 0x400`.

---------------------------------------------------------------------------------------------------

## 3. Framing, timing, ack

```
offset: 0    1..6          7         8..63
        CMD  params        CKSUM     payload (56 bytes, zero-padded)
```
* JS pads short commands to 8 bytes (`sendFeature`) and WebHID pads to 64. iot_driver resizes to 64(+prefix) as well.
* Checksum Bit7 / Bit8 as in §0. Command constants below are **already the final byte value** (GETs are SET|0x80).
* `commonMsg(cmd, cs, sendDelay=10, readDelay=10)`: sleep 10 ms, SetFeature, sleep 10 ms, GetFeature. Bluetooth adds 60/100 ms.
* After every SET the app calls `vendorSleep(t)` (default **100 ms**; 500 ms after bulk HE write; **2000 ms after factory
  reset**; 300 ms after GIF start). If the device emits the busy event `[0x0F,1,0]` the app blocks further I/O until
  `[0x0F,0,0]` or the timeout (§6).
* There is **no explicit ACK** for SETs: success = the SetFeature call succeeded. For GETs, check `resp[0]==cmd` where the
  firmware echoes (single-value GETs). Multi-page GETs: no echo — raw data.

---------------------------------------------------------------------------------------------------

## 4. Command reference (wired path)  **[CODE]** — from `5e635fe2.js` unless noted

| Cmd | Name | Dir | Packet (bytes not listed = 0) | Response |
|---|---|---|---|---|
| 0x01 | SET_RESET (factory reset) | ⚠ | vendor command; **blocked by the app on every link, never send** | – |
| 0x03 | SET_REPORT (polling rate) | W | `[2]=code` (0=8000,1=4000,2=2000,3=1000,4=500,5=250,6=125 Hz) | – |
| 0x83 | GET_REPORT | R | `[0x83]` | `resp[2]=code` |
| 0x04 | SET_PROFILE | W | `[1]=profile 0..3`; **blocked over dongle and on id 2901 on every link** | – |
| 0x84 | GET_PROFILE | R | `[0x84]` | `resp[1]=active profile` |
| 0x85 | GET_LEDONOFF | R | `[0x85]` | `resp[1]==1` → power-save mode on |
| 0x06 | SET_DEBOUNCE | W | `[1]=value` | – |
| 0x86 | GET_DEBOUNCE | R | | `resp[1]` |
| 0x07 | SET_LEDPARAM | W **Bit8** | see §4.4 | – |
| 0x87 | GET_LEDPARAM | R | | see §4.4 |
| 0x08 / 0x88 | SET/GET_SLEDPARAM (side light) | | same layout as LED; **X68 has no side light** (no `sideLightLayout`) | |
| 0x09 | SET_KBOPTION | W | `[2]=fnLayerIndex [3]=anti-mistouch(0/1) [4]=RTStab/25 [5]=WASD↔arrows swap(0/1)` | – |
| 0x89 | GET_KBOPTION | R | | `resp[1]`=OS (0 win,1 mac,2 ios,3 android), `resp[2]`=fnIndex, `resp[3]`=anti-mistouch, `resp[4]`=RTStab/25 (>5 ⇒ 0), `resp[5]`=WASD swap |
| 0x0A | SET_KEYMATRIX | W | bulk or single, §4.1 | – |
| 0x8A | GET_KEYMATRIX | R | `[1]=profile [2]=0xFF [3]=page 0..7 [4]=sonProfile 0..3` | 64 raw bytes/page → 512 B = 128 keys × 4 B |
| 0x0B | SET_MACRO | W | `[1]=macroIdx [2]=page [3]=56 [4]=isLast` + 56 B | – |
| 0x8B | GET_MACRO | R | `[1]=macroIdx [2]=page 0..3` | raw; stop after a page containing `00 00 00 00` |
| 0x0C | SET_USERPIC (per-key RGB) | W | `[1]=slot 0..4 [2]=0xFF [3]=page 0..6 [4]=len(56, last=42) [5]=isLast` + RGB | – |
| 0x8C | GET_USERPIC | R | `[1]=slot [2]=0xFF [3]=page 0..5` | 6×64 raw = RGB triplets by matrix index |
| 0x10 | SET_FN (Fn layer) | W | bulk or single, §4.2 | – |
| 0x90 | GET_FN | R | `[1]=os (0 win,1 mac,2 android,3 ios) [2]=fnLayer(0) [3]=0xFF [4]=page 0..7` | 512 raw bytes |
| 0x11 | SET_SLEEPTIME | W | 64-byte: u16 LE `[8..9]`=BT idle s, `[10..11]`=2.4G idle s, `[12..13]`=BT deep s, `[14..15]`=2.4G deep s | – |
| 0x91 | GET_SLEEPTIME | R | | same offsets (defaults if no answer: 120,120,1680,1680) |
| 0x12 | SET_USERGIF | W | per-key animation frames (not in X68 UI) | |
| 0x17 | SET_AUTOOSEN (auto OS detect) | W | `[1]=0/1` | – |
| 0x97 | GET_AUTOOSEN | R | | `resp[1]==1` |
| 0x1B | SET_MAGNETISM_REPORT (live travel stream) | W | `[1]=0/1` | events `[0x1B,…]` §6 |
| 0x1C | SET_MAGNETISM_CAL (min/zero calibration) | W | `[1]=0/1` | – |
| 0x1E | SET_MAGNETISM_MAXIMUM_CALIBRATION | W | `[1]=0/1` | – |
| 0x65 | SET_MULTI_MAGNETISM | W | §4.5 | – |
| 0xE5 | GET_MULTI_MAGNETISM | R | `[1]=op [2]=1 [3]=page` | raw |
| 0x8F | GET_USB_VERSION / device id | R | | §2.3 |
| 0x80 | GET_RF_VERSION | R | | `resp[1..2]` |
| 0xAC | SET_FLASHCHIPERASE | ⚠ | erases flash — **never send** | `resp=AC AA AA 55 55` |
| 0x7F 55 AA 55 AA | enter USB bootloader | ⚠ | **never send** | |
| 0xF8 55 AA 55 AA 00 00 82 | enter RF bootloader (dongle) | ⚠ | **never send** | |
| 0xBA C0 / 0xBA C2 | bootloader upgrade start / finish | ⚠ | firmware update only | |
| 0x22, 0x25, 0x27, 0x28, 0x29, 0x30-0x32, 0x50/0xD0 | OLED/TFT/screen/SKU | | not used by X68 | |

### 4.1 Key matrix (remap) — per profile × sonProfile **[CODE]**
* Matrix = **128 slots × 4 bytes**; slot index = `column*6 + row` (row 0 = F-row … row 5 = bottom row). Table in §10.
* **sonProfile 0..3** are four parallel matrices per profile: 0 = normal layer; for HE "advanced" keys they carry the
  extra bindings (DKS stage keys 0..3, MT: 0 = hold action, 1 = tap action; TGL likewise). (`固件读取磁轴ListToDJ磁轴List`)
* Bulk SET 0x0A: header `[0]=0x0A [1]=profile [2]=0xFF [3]=page [4]=bytesInPage [5]=isLast [6]=sonProfile`, 56 B payload;
  512 B → 10 pages (9×56 + 8). The app always sends the *whole* matrix built from defaultMatrix + changes.
* Single-key SET 0x0A: `[1]=profile [2]=slot (<128) [5]=1 [6]=sonProfile [8..11]=action`.
* 4-byte **action encoding** (`Zi.configToMatrix` / `matrixToConfigs`):

| byte0 | meaning | bytes 1..3 |
|---|---|---|
| 0x00 | keyboard / combo | `[1]=modifier bitmask, [2]=HID usage, [3]=2nd HID usage`; plain key `[0,0,hid,0]`; **all-zero = key disabled** |
| 0x01 | mouse | `[2]`: 0xF0 L, 0xF1 R, 0xF2 M, 0xF3 Back, 0xF4 Fwd, 0xF5 wheel (`[3]` +1/−1), 0xF6 X, 0xF7 Y/wheel fwd, 0xF8 wheel back |
| 0x02 | system | `[2]`: 0x81 power, 0x82 sleep, 0x83 wake |
| 0x03 | consumer | `[2..3]` = 16-bit usage LE (0xE9 vol+, 0xEA vol−, 0xE2 mute, 0xCD play/pause, 0xB5 next, 0xB6 prev, 0xB7 stop, 0x0183 player, 0x0192 calc, 0x018A mail, 0x0194 my-PC, 0x0221 search, 0x0223 home, 0x0224 back, 0x0227 refresh, 0x006F/0x0070 screen brightness ±) |
| 0x06 | `[6,0x80,0,n]` mic toggle (n=0) / launch program n | |
| 0x08 | profile | `[8,0,op,arg]` op 0 select, 1 next, 2 prev, 3 loop, 4 select `arg` |
| 0x09 | macro | `[9, mode, macroIdx, 0]` mode 0 = repeat N times, 1 = toggle on/off, 2 = repeat while held |
| 0x0A | firmware function | `[10,1,0,0]` Fn, `[10,1,1,0]` right-Fn, `[10,13..15,..]` volume↔backlight knobs, `[10,24]` AI, others see Fn defaults §10 (unnamed in app — **copy verbatim**) |
| 0x0B | "fire" (turbo) | |
| 0x0D | LED control | `[13,2,1]` brightness+, `[13,2,2]` brightness−, others in Fn defaults |
| 0x0E | wireless channel `[14,0,n]` (n=0..2 BT1-3, 5 = 2.4G) **[INF]** | |
| 0x12 | key sequences (`[18,0,227,44]` Siri) | |
| 0x14 | DPI | |
| 0x15 | gamepad | |
| 0x16 | snap/recoil | |
| 0x17 | MDS | |
| 0x18 | MT (old style) | `[24, key1, key2, time/10]` |

  Full tables: `snippets\10_special_key_tables_mv_ba_wp.js`, HID↔name `snippets\19_hid_html_keycode_table_En.js`.

### 4.2 Fn layer **[CODE]**
`fnSysLayer: {win:1, mac:1}` → read 0x90 with os=0 (win) and os=1 (mac), layer 0. Bulk SET 0x10: `[1]=os [2]=layer [3]=0xFF
[4]=page 0..9 [5]=56 (0 on page 9) [6]=isLast(page 9)`. Single: `[1]=os [2]=layer [3]=slot [8..11]=action`. "Restore
default" for a slot = write that slot of `defaultFnMatrix`/`defaultFnMacMatrix`.
Note: `getKBOption` maps `resp[1]` 2→ios 3→android but `_解码fnSys` maps android→2 ios→3 (vendor inconsistency; X68 only uses win/mac).

### 4.3 Macros **[CODE]**
Buffer (≤ 256 B): `u16 LE repeatCount`, then events, terminated by `00 00 00 00`:
* key / mouse-button: `[code, flags]` where `flags = (down ? 0x80 : 0) | delay` if `delay ≤ 127` ms, else
  `[code, down?0x80:0, delayLo, delayHi]`. Mouse-button codes 0xF0–0xF4 (L,R,M,Back,Fwd). Keys = HID usage 0x04–0xEF.
* mouse move: `[0xF9, delay≤127 ? delay : 0, dx(int8), dy(int8)]` + `[delayLo, delayHi]` if delay > 127.
  (Decoder reads the short delay as `byte>>1` – vendor inconsistency.)
Write: only non-empty 56-byte pages, max 5. Read: up to 4 pages until a page contains `00 00 00 00`.
Macro index used = the one stored in the key action `[9,mode,idx,0]`.

### 4.4 Lighting **[CODE]**
SET 0x07 (**Bit8 checksum**, checksum at byte 8): `[1]=mode [2]=4-speed [3]=brightness(0..4) [4]=option<<4 | (dazzle?8:7) [5..7]=R,G,B`.
* exceptions: UserPicture → `[4]=slot<<4`, `[5..7]=0,200,200`; MusicFollow2/3 → `[4]=option<<4 | (dazzle?0:4)`; ScreenColor → `[4]=0`.
* white 0xFFFFFF is sent as 0xFAFFFA (and mapped back on read).
GET 0x87 → `mode=resp[1]`, `speed=4-resp[2]`, `bright=resp[3]`, `option=resp[4]>>4`, `low=resp[4]&15`
(8 = dazzle/rainbow, 7 = custom colour, 0..6 = preset colour 0xFF0000,0xFF8000,0xFFFF00,0x00FF00,0x00FFFF,0x0000FF,0xFF00FF), RGB `resp[5..7]`.
Mode ids: 0 Off, 1 AlwaysOn, 2 Breath, 3 Neon, 4 Wave, 5 Ripple, 6 Raindrop, 7 Snake, 8 PressAction, 9 Converage,
10 SineWave, 11 Kaleidoscope, 12 LineWave, 13 UserPicture, 14 Laser, 15 CircleWave, 16 Dazzling, 17 RainDown, 18 Meteor,
19 PressActionOff, 20 MusicFollow3, 21 ScreenColor, 22 MusicFollow2, 23 Train, 24 FireWorks, 25 UserColor.
X68 offers (layout `I`): Wave(options →,←,↓,↑), Ripple, Raindrop, Snake(Z, ring), PressAction, Converage, SineWave,
Kaleidoscope(out,in), PressActionOff, LineWave(→,←), Laser, CircleWave(ccw,cw), Dazzling, RainDown, Meteor, AlwaysOn,
Breath, Neon, UserPicture(5 slots), MusicFollow2(upright/separate/intersect), ScreenColor. brightness max 4, speed max 4.
MusicFollow/ScreenColor are host-driven (iot_driver streams colours) — skip or reimplement host-side.
Per-key picture: §4 table rows 0x0C/0x8C; RGB array index = matrix slot.

### 4.5 Hall-effect (magnetic switch) settings — 0x65 / 0xE5 **[CODE]**
Bulk SET: `[0]=0x65 [1]=op [2]=1 [3]=page [4]=isLastOfWholeTransaction` + 56 B; Bulk GET: `[0]=0xE5 [1]=op [2]=1 [3]=page`.
Per-key SET: `[0]=0x65 [1]=op [2]=0 [3]=slot [4]=isLast` + data (1, 2 or 4 bytes).
All travel values are integers in units of `1/k` mm (`k` from §2.3; e.g. k=100 → 200 = 2.00 mm). u16 lists = 128×2 B LE
(GET 4 pages), u8 lists = 128 B (GET 2 pages).

| op | content | width | bulk write default fill |
|---|---|---|---|
| 0 | actuation point (行程按下) | u16 | 200 |
| 1 | release point (行程抬起) | u16 | 280 |
| 2 | Rapid-Trigger press sensitivity | u16 | 0 |
| 3 | Rapid-Trigger release sensitivity | u16 | 0 |
| 4 | DKS trigger travel | u16 | 0 |
| 5 | MT hold time (×10 ms) | u8 | 0 |
| 6 | bottom dead zone (死区) | u16 | 30 |
| 7 | **key mode**: bit7 = RT on; low 7 bits 0 normal, 2 DKS, 3 MT, 4 TGL-hold, 5 TGL-dots, 7 Snap-tap/SOCD | u8 | – |
| 8 | DKS stage modes (per-key only, 4 B) | 4×u8 | – |
| 9 | Snap-tap partner slot index | u8 | 0 |
| 10 | DKS stage modes bulk (GET 8 pages = 4×128 B: block t holds stage-key t for every slot) | – | – |
| 251 (0xFB) | top dead zone (fw ≥ 0x400) | u8 | 30 |
| 252 (0xFC) | switch type (Kv enum: 0 Gateron(高特), 1 磁玉, 2 磁玉pro, …, see `snippets\14_switch_types_Kv.js`) | u8 | – |
| 254 (0xFE) | live calibration/travel readout (GET only, 4 pages, u16) | u16 | – |

DKS stage byte = four 2-bit fields `[b1:0, b3:2, b5:4, b7:6]` (one per DKS travel phase) for the key bound in sonProfile t.
App write order (bulk): op 7 (modes) → changed u16 lists (0,1,2,3,6,4) → u8 lists (5, 9, 251) → per-key op 8 for DKS keys
→ per-key op 9 for snap pairs → sleep 500 ms → op 252 (switch types) → sleep 100 ms. `isLast` is set only on the final
packet of the whole batch. Snap-tap is symmetric: set both keys to mode 7 and op 9 cross-referencing each other.
Calibration: start = `0x1C 1`, `0x1C 0`, `0x1E 1`, then poll `0xE5 op 254` (4 pages) for live values; stop = `0x1E 0`.

### 4.6 Other options
* `RTStab` (RT stability) 0..125 step 25 → byte = value/25. "防误触" = anti-mistouch toggle. WASD swap.
* Debounce: raw byte (UI unit presumably ms).
* Sleep: seconds, ranges from device DB (§2.3).

---------------------------------------------------------------------------------------------------

## 5. 2.4 GHz dongle path (`3151:5026`) **[LIVE]**

Implemented by `DongleX68Transport` in `src/SharkBabyDrivers.Core/Devices/X68/X68Transport.cs`.
Open the config collection **FFFF/0002** and the event collection **FFFF/0001**. Windows feature buffers contain
**Report ID 0 + 64 data bytes**. The keyboard command and checksum are identical to the wired packet: there is
**no 0x0E prefix**, no `FE` setup, and no `FD` response retrieval.

### 5.1 GET transaction

1. Wait for receiver-local `F7` status byte 5 (`rf_ready`) to become nonzero before forwarding a command.
2. Send the keyboard's 64-byte GET packet directly with Report ID 0.
3. Poll `F7` with back-to-back SetFeature/GetFeature calls, **without a sleep**, until status byte 0
   (`has_response`) is nonzero. The response cache has only one slot, so delaying can lose the answer.
4. Send receiver-local `FC` and immediately GetFeature to obtain the cached 64-byte keyboard answer.
5. Repeat the same GET until an identical answer has been seen **twice**, or **three times for an all-zero answer**.
   Raw table pages have no command echo, and stale or zero replies occur; a single answer is insufficient.

Current bounds: 250 ms for each response wait, 16 attempts per GET; the pre-send idle wait is bounded at 700 ms
with 2 ms intervals. Offsets below exclude the Report ID byte. Local packets use Bit7 checksum (`F7` → `08`,
`FC` → `03` at data byte 7).

| `F7` data byte | Meaning used by the app |
|---|---|
| 0 | `has_response`: cached keyboard answer available |
| 1 | battery level, 0–100 |
| 5 | `rf_ready`: 0 = waiting for keyboard, nonzero = idle |

The corrected receiver-local command meanings are **`FD` = GET_DONGLE_ID** (`AA 55 01 00`) and
**`FE` = SET_RESPONSE_SIZE**, a one-shot SPI transmit-length override. They are not part of the app's transfer
sequence. See the related firmware analysis in
[monsgeek-akko-linux, transport and dongle-only commands](https://github.com/echtzeit-solutions/monsgeek-akko-linux/blob/master/docs/PROTOCOL.md).

### 5.2 SET completion and interference

SETs do **not** return a keyboard feature response. Send the packet directly, wait for `rf_ready`, and respect
the busy/idle events **`0F 01` / `0F 00`** on the event collection (§6). Do not wait for `has_response` after a SET.
`WriteImageVerified` rereads the image and retries differences; a successful USB write alone does not prove that
the keyboard stored the change.

SignalRGB controlling the same keyboard through the receiver generated roughly 25 busy/idle events per second
during live testing and response loss was observed. SignalRGB may remain open; if simultaneous control causes
a conflict during configuration, pause only this keyboard's control in SignalRGB. `ForeignTraffic` detects
repeated events without a recent write from this app and reports the conflict. Wake the keyboard by
typing before a test. An unanswered GET raises `DeviceAsleepException` when no foreign traffic is detected;
the UI retries connection every 3 seconds.

### 5.3 Wireless safety limits

* **Never select a profile (`0x04`) through this receiver, or on X68 V2 (id 2901) over either link.** A wireless
  attempt locked up the keyboard until it was switched off and on. A subsequent USB snapshot on 2901 also
  coincided with loss of typing and LEDs; the log records a `SelectProfile` timeout. Automatic profile selection
  is the main lead, not a definitive hardware diagnosis. The user reported stable operation with SharkBabyDrivers
  closed and SignalRGB open. `CanSwitchProfiles` is false before identification, for every dongle and for id 2901;
  profile activation is disabled accordingly. Calibration remains disabled wirelessly.
* Read key matrices for all four profiles using the profile parameter; read/write HE only for the active profile.
  `HeWasRead` distinguishes missing HE tables from actual all-zero data, so unread profiles must not be filled
  with defaults and written back. A wireless original backup, and an id 2901 backup over either link, therefore
  omit HE for inactive profiles. To work on another profile, select it physically and reread the keyboard.
* Before the first SET, query the physical active profile (`84`) and compare it with the image that was read.
  If it changed, abort before writing and require a fresh read: HE writes implicitly address the active profile.
* Never send reset (`01`), pairing (`F8`), bootloader (`7F`), flash erase (`AC`), upgrade (`BA`), or any
  `F0`–`FF` command to the **keyboard**. `F7`/`FC` above are handled locally by the receiver. Experimental keyboard
  commands must be GETs (`>=80` and `<F0`), excluding destructive commands such as `AC` and `BA`.
  Factory reset is blocked by the app over both cable and receiver, and its UI control is disabled.

---------------------------------------------------------------------------------------------------

## 6. Unsolicited vendor events (input reports on UsagePage 0xFFFF / Usage 1) **[CODE]** (`bv`, index.js 33377)
`e[0]` = report id, `t = e[1..3]`:
| t | meaning | app reaction |
|---|---|---|
| 0F 01 00 | device busy (start) | block I/O for `needSleepTime` (default 100 ms) |
| 0F 00 00 | device idle (stop) | unblock |
| 1B lo hi | live travel sample (after `0x1B 1`) | value = `t[1]/10` if k=10 else `(t[2]<<8|t[1])/k` mm |
| 01 xx xx | profile switched on keyboard | re-read profile + keys |
| 02 xx 00 | polling rate changed | |
| 03 a b | `b==8` Fn layer changed; `b==9`/`b==4` power-save; `b==0/2` OS: `a&2` → mac else win | re-read KBOption |
| 04..07 | LED effect changed on keyboard | re-read 0x87 |
| 08..0B | side-LED changed | |
| 0C xx 00 | DPI (mice) | |
| 0D 00 00 | factory reset done | |
| 13 00 00 | sleep toggled | |
| 1D | HE mode switched | |
| 80 00 00 | mic | |
Debounced 300 ms before dispatch.

---------------------------------------------------------------------------------------------------

## 7. BACKUP — how the original app reads the full state (do this before writing anything)

Read order used by the vendor app (`writeToAppState`, index.js 69843 / 68340-68460) plus what is needed for a full image.
**The sequence below documents the vendor's full-image procedure. In SharkBabyDrivers, profile-selection steps
are allowed only on identified models other than 2901 over USB. Over 2.4 GHz, and on id 2901 on either link,
skip profile selection and read HE only for the already-active profile (§5). Snapshots of 2901 send GETs only.**
1. `0x8F` id + fw, `0x80` rf fw → choose class (2370/2901), compute `k` and top-deadzone support.
2. `0x84` active profile `P0` (remember it!).
3. Global: `0x87` LED, `0x88` side LED (harmless), `0x89` KBOption, `0x91` sleep, `0x83` polling, `0x97` auto-OS,
   `0x86` debounce, `0x85` power-save/LED on-off.
4. Fn layers: `0x90` os=0 layer 0 pages 0-7; `0x90` os=1 layer 0 pages 0-7.
5. Per-key pictures: `0x8C` slot 0..4, pages 0-5.
6. For each profile p = 0..3:
   * key matrices: `0x8A` p, son 0..3, pages 0-7 (no profile switch needed — profile is a parameter);
   * HE tables are **for the active profile only**: `0x04 p` (+100 ms) then `0xE5` ops 7(2 pages), 0,1,2,3,6,4 (4 pages),
     5, 9, 251, 252 (2 pages), 10 (8 pages). (The app only reads 2/3 when some key has RT, 4/10 when DKS exists, 5 for MT,
     9 for snap — for a backup read all.)
7. Macros: collect every `[9,mode,idx,0]` from all matrices (incl. Fn) and read `0x8B idx` pages 0-3.
8. Restore active profile: `0x04 P0`.
Store raw 64-byte responses (plus decoded JSON) so a restore can replay them byte-exact with the SET counterparts
(0x0A bulk, 0x10 bulk, 0x0B, 0x0C, 0x65 bulk per op, 0x07, 0x09, 0x11, 0x03, 0x17, 0x06, 0x04).
Note: on other identified models over USB, step 6's `0x04` writes are the only writes in a backup, and the original
profile is restored in a `finally` block. A dongle backup, or an id 2901 backup over either link, sends no
profile-selection commands and contains no HE data for inactive profiles. Select another profile physically
and reread to configure its HE settings.
The app saves one `original.json` per device without replacing it; named configurations belong to `saved-profiles`
and are grouped by model.

---------------------------------------------------------------------------------------------------

## 8. Safety notes
* Never send 0x01 (factory reset), 0xAC (flash erase), 0x7F/0xF8 55 AA 55 AA (boot loader), 0xBA xx (upgrade).
* Never send keyboard commands in 0xF0–0xFF. Never select a profile before identification, over 2.4 GHz, or on
  id 2901 even over USB. The receiver-local status/cache operations in §5 do not forward commands to the keyboard.
* Respect wired delays (≥10 ms between Set/Get, ≥100 ms after SETs) and device busy/idle events. Over the dongle,
  use §5's flow control and poll `has_response` without a sleep.
* Bulk HE writes overwrite **all 128 slots** of an op — always start from a fresh read of that op.

---------------------------------------------------------------------------------------------------

## 9. Data model cheat-sheet (JS names → meaning)
`travel`=actuation, `liftTravel`=release, `fire`=Rapid Trigger on, `firePressTravel`/`fireLiftTravel`=RT sensitivities,
`deadZoneTravel`=bottom dead zone, `topDeadZoneTravel`=top dead zone, `dynamicTravel`=DKS point, `triggerModes`=DKS stage
bytes, `mt_time`=MT ms, `bindKeyOriginal`=snap partner, `轴体`=switch type, `防误触开关`=anti-mistouch, `RTStab`=RT
stability, `WASDSwap`.

---------------------------------------------------------------------------------------------------

## 10. X68 PRO HE matrix tables **[CODE]** (slot = col*6+row; only non-empty slots)

### id 2370 (`ry5088_x68_8k_002`, no F-row)
```
1 Esc  2 Tab  3 Caps  4 LShift  5 LCtrl
7 1  8 Q  9 A  10 NonUS\  13 2  14 W  15 S  16 Z  17 LWin  19 3  20 E  21 D  22 X  23 LAlt
25 4  26 R  27 F  28 C  31 5  32 T  33 G  34 V  37 6  38 Y  39 H  40 B  41 Space
43 7  44 U  45 J  46 N  49 8  50 I  51 K  52 M  55 9  56 O  57 L  58 ,  59 Fn[10,1,0,0]
61 0  62 P  63 ;  64 .  65 RCtrl  67 -  68 [  69 '  70 /  73 =  74 ]  75 NonUS#  76 RShift  77 Left
79 Backspace  80 \  81 Enter  82 Up  83 Down  85 Delete  86 PgUp  87 PgDn  89 Right
```
Fn (win = mac): 1:`` ` `` 2:[10,4] 3:[10,2] 7..73:F1..F12 (7,13,19,25,31,37,43,49,55,61,67,73) 8/14/20/26:[14,0,0/1/2/5]
9:[10,5,0] 15:[10,5,1] 17:[10,3] 65:[10,8] 76:[10,13] 77:[13,3,1] 79:[10,12] 80:[13,1,0] 81:[13,5,1] 82:[13,2,1]
83:[13,2,2] 85:Insert 86:Home 87:End 89:[13,3,2]

### id 2901 (`ry5088_x68v2_8k`, has F-row in row 0)
```
1 Esc 2 Tab 3 Caps 4 LShift 5 LCtrl 6 F1 7 1 8 Q 9 A 10 NonUS\ 11 LWin 12 F2 13 2 14 W 15 S 16 Z 17 LAlt
18 F3 19 3 20 E 21 D 22 X 24 F4 25 4 26 R 27 F 28 C 30 F5 31 5 32 T 33 G 34 V 36 F6 37 6 38 Y 39 H 40 B 41 Space
42 F7 43 7 44 U 45 J 46 N 48 F8 49 8 50 I 51 K 52 M 54 F9 55 9 56 O 57 L 58 , 60 F10 61 0 62 P 63 ; 64 .
65 Fn 66 F11 67 - 68 [ 69 ' 70 / 71 RCtrl 72 F12 73 = 74 ] 75 NonUS# 76 RShift 77 Left 79 Backspace 80 \
81 Enter 82 Up 83 Down 85 Delete 86 PgUp 87 PgDn 89 Right
```
Fn win: number row = consumer keys (7:[3,0,0x70] bright−, 13:[3,0,0x6F] bright+, 19 calc, 25 home, 31 player, 37 prev,
43 next, 49 play, 55 stop, 61 mute, 67 vol−, 73 vol+) plus the same LED/system codes as 2370. Fn mac differs (F-row
mirrored, Spotlight/Siri combos). Exact arrays: `snippets\04_x68_default_matrices.json`.
Physical layout SVG for the UI: chunk `8cc0ae07.js` (`Keyboard_66_X68_RT_On/Off`) with sub-chunks `06ca2d03.js`, `c2fc9035.js`.

---------------------------------------------------------------------------------------------------

## 11. Snippets (local analysis workspace, not versioned)
01 base protocol class (all commands) · 02/03 X68 subclasses · 04 default matrices JSON · 05 transport `iA` (checksum, retries,
delays) · 06 WebHID class (64-B feature reports) · 07 gRPC client + dongle enumeration · 08 matrix codec · 09 macro codec ·
10 special-key tables · 11 lighting layout · 12 device DB entries · 13 vendor events · 14 switch types · 15 USB filter lists ·
16 gRPC proto enums/messages · 17 DKS/MT sonProfile mapping · 18 read flow · 19 HID keycode table.
Tools used (pure Python): NSIS block dumper, 7z BCJ2 decoder, x86 xref/disasm helpers.
