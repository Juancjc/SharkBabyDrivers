// Attack Shark X68 PRO HE for SignalRGB, through SharkBabyDrivers.
//
// This plugin never writes to the keyboard. It hands each colour to SharkBabyDrivers over UDP on 127.0.0.1,
// and SharkBabyDrivers (the only program talking to the keyboard) applies it with the "screen colour" command
// 0x0E, between its own reads and writes. That removes the two problems of the stock plugin:
//   * it sent SetLedParam 0x07 on every frame; the keyboard stores 0x07 in flash and restarts the effect each
//     time, so the lights flickered (and the flash was rewritten ~30 times per second);
//   * it shared the keyboard's HID channel with SharkBabyDrivers with no coordination at all.
// Who owns the lighting (SignalRGB or SharkBabyDrivers) is chosen in SharkBabyDrivers; when it is
// SharkBabyDrivers, the colours sent here are simply ignored and the keyboard receives nothing from SignalRGB.
//
// The stock firmware shows ONE live colour for the whole keyboard (there is no per-key live command), so the live
// colour is reduced from the key colours (accent by default: what stands out from the background; or centre,
// average, dominant). The per-key colours are also sent, twice a
// second, so SharkBabyDrivers can store them once, on request, as a static picture (it never animates).
// SharkBabyDrivers must be running (it can stay minimised) for the colours to reach the keyboard.
//
// Installed by SharkBabyDrivers into Documents\WhirlwindFX\Plugins; it replaces the stock plugin for these ids.

export function Name() { return "Attack Shark X68 Pro HE"; }
export function Version() { return "1.2.0"; }
export function VendorId() { return 0x3151; }
// 0x5026 = 2.4G receiver, 0x502F = X68 PRO HE over the cable, 0x502D = X68 HE over the cable.
export function ProductId() { return [0x5026, 0x502F, 0x502D]; }
export function Publisher() { return "SharkBabyDrivers"; }
export function Documentation() { return "troubleshooting/attack-shark"; }
export function Size() { return [16, 6]; }
export function DeviceType() { return "keyboard"; }
export function Validate(endpoint) { return endpoint.interface === 2 && endpoint.usage_page === 0xffff; }
export function ImageUrl() { return "https://assets.signalrgb.com/devices/brands/attack-shark/keyboards/x68-pro-he.png"; }
/* global
shutdownColor:readonly
LightingMode:readonly
forcedColor:readonly
LiveColorMode:readonly
*/
export function ControllableParameters() {
	return [
		{property:"shutdownColor", group:"lighting", label:"Shutdown Color", description: "This color is applied to the device when the System, or SignalRGB is shutting down", min:"0", max:"360", type:"color", default:"#000000"},
		{property:"LightingMode", group:"lighting", label:"Lighting Mode", description: "Determines where the device's RGB comes from. Canvas will pull from the active Effect, while Forced will override it to a specific color", type:"combobox", values:["Canvas", "Forced"], default:"Canvas"},
		{property:"forcedColor", group:"lighting", label:"Forced Color", description: "The color used when 'Forced' Lighting Mode is enabled", min:"0", max:"360", type:"color", default:"#009bde"},
		{property:"LiveColorMode", group:"lighting", label:"Live Color", description: "The keyboard firmware shows a single live colour for all keys. Accent shows what stands out from the background (the wave of a wave effect), Center follows the middle of the keyboard, Average blends every key, Dominant shows the colour covering most keys", type:"combobox", values:["Accent", "Center", "Average", "Dominant"], default:"Accent"},
	];
}

const BRIDGE_HOST = "127.0.0.1";
const BRIDGE_PORT = 47851;
const HEARTBEAT_MS = 500;

// [matrix slot, name, x, y] - generated from the app's X68 V2 layout (SharkBabyDrivers.Cli signalrgb-layout).
const vKeys = [
	[6, "F1", 1, 0], [12, "F2", 2, 0], [18, "F3", 3, 0], [24, "F4", 4, 0], [30, "F5", 6, 0], [36, "F6", 7, 0], [42, "F7", 8, 0], [48, "F8", 9, 0], [54, "F9", 10, 0], [60, "F10", 11, 0], [66, "F11", 12, 0], [72, "F12", 13, 0],
	[1, "Esc", 0, 1], [7, "1", 1, 1], [13, "2", 2, 1], [19, "3", 3, 1], [25, "4", 4, 1], [31, "5", 5, 1], [37, "6", 6, 1], [43, "7", 7, 1], [49, "8", 8, 1], [55, "9", 9, 1], [61, "0", 10, 1], [67, "-", 11, 1], [73, "=", 12, 1], [79, "Backspace", 14, 1], [85, "Del", 15, 1],
	[2, "Tab", 0, 2], [8, "Q", 2, 2], [14, "W", 3, 2], [20, "E", 4, 2], [26, "R", 5, 2], [32, "T", 6, 2], [38, "Y", 7, 2], [44, "U", 8, 2], [50, "I", 9, 2], [56, "O", 10, 2], [62, "P", 11, 2], [68, "[", 12, 2], [74, "]", 13, 2], [80, "\\", 14, 2], [86, "PgUp", 15, 2],
	[3, "Caps", 0, 3], [9, "A", 2, 3], [15, "S", 3, 3], [21, "D", 4, 3], [27, "F", 5, 3], [33, "G", 6, 3], [39, "H", 7, 3], [45, "J", 8, 3], [51, "K", 9, 3], [57, "L", 10, 3], [63, ";", 11, 3], [69, "'", 12, 3], [81, "Enter", 13, 3], [87, "PgDn", 15, 3],
	[4, "Left Shift", 1, 4], [16, "Z", 2, 4], [22, "X", 3, 4], [28, "C", 4, 4], [34, "V", 5, 4], [40, "B", 6, 4], [46, "N", 7, 4], [52, "M", 8, 4], [58, ",", 9, 4], [64, ".", 10, 4], [70, "/", 11, 4], [76, "Right Shift", 13, 4], [82, "Up Arrow", 14, 4],
	[5, "Left Ctrl", 0, 5], [11, "Left Win", 1, 5], [17, "Left Alt", 3, 5], [41, "Space", 6, 5], [65, "Fn", 10, 5], [71, "Right Ctrl", 11, 5], [77, "Left Arrow", 13, 5], [83, "Down Arrow", 14, 5], [89, "Right Arrow", 15, 5],
];
const vLedNames = vKeys.map(k => k[1]);
const vLedPositions = vKeys.map(k => [k[2], k[3]]);
const KEYS_EVERY_MS = 500;

export function LedNames() { return vLedNames; }
export function LedPositions() { return vLedPositions; }

let lastColor = null;
let lastSent = 0;
let lastKeys = 0;

export function Initialize() {
	device.addFeature("udp");
	lastColor = null;
}

export function Render() {
	const now = Date.now();
	const forced = LightingMode === "Forced";
	const keys = forced ? null : vKeys.map(k => device.color(k[2], k[3]));
	const color = forced ? hexToRgb(forcedColor) : liveColor(keys);

	if (keys !== null && now - lastKeys >= KEYS_EVERY_MS) {
		sendKeys(keys);
		lastKeys = now;
	}

	const same = lastColor !== null && color[0] === lastColor[0] && color[1] === lastColor[1] && color[2] === lastColor[2];

	// Only changes go out, plus a heartbeat so SharkBabyDrivers knows SignalRGB is alive.
	if (same && now - lastSent < HEARTBEAT_MS) {
		return;
	}

	sendColor(1, color);
	lastColor = color;
	lastSent = now;
}

export function Shutdown(SystemSuspending) {
	sendColor(2, hexToRgb(SystemSuspending ? "#000000" : shutdownColor));
}

function liveColor(keys) {
	if (LiveColorMode === "Center") {
		return device.color(8, 3);
	}

	if (LiveColorMode === "Accent") {
		// Background = dominant colour; show the average of the keys that clearly differ from it (the wave),
		// or the background when nothing stands out.
		const bg = dominant(keys);
		const sum = [0, 0, 0];
		let n = 0;

		for (const c of keys) {
			if (Math.abs(c[0] - bg[0]) + Math.abs(c[1] - bg[1]) + Math.abs(c[2] - bg[2]) > 90) {
				sum[0] += c[0]; sum[1] += c[1]; sum[2] += c[2];
				n++;
			}
		}

		return n === 0 ? bg : [Math.round(sum[0] / n), Math.round(sum[1] / n), Math.round(sum[2] / n)];
	}

	if (LiveColorMode === "Dominant") {
		return dominant(keys);
	}

	const sum = [0, 0, 0];

	for (const c of keys) {
		sum[0] += c[0]; sum[1] += c[1]; sum[2] += c[2];
	}

	return [Math.round(sum[0] / keys.length), Math.round(sum[1] / keys.length), Math.round(sum[2] / keys.length)];
}

function dominant(keys) {
	// Most frequent colour after rounding to 32 levels per channel; ties go to the brightest.
	const counts = {};
	let best = keys[0];
	let bestCount = 0;

	for (const c of keys) {
		const key = (c[0] >> 3) + "," + (c[1] >> 3) + "," + (c[2] >> 3);
		const n = (counts[key] = (counts[key] || 0) + 1);

		if (n > bestCount || (n === bestCount && c[0] + c[1] + c[2] > best[0] + best[1] + best[2])) {
			best = c;
			bestCount = n;
		}
	}

	return best;
}

function sendKeys(keys) {
	const pid = device.productId();
	// "SBD1", version 1, type 3 (per-key snapshot), product id (BE), count, then [slot, R, G, B] per key
	const packet = [0x53, 0x42, 0x44, 0x31, 1, 3, (pid >> 8) & 0xff, pid & 0xff, vKeys.length];

	for (let i = 0; i < vKeys.length; i++) {
		packet.push(vKeys[i][0], keys[i][0] & 0xff, keys[i][1] & 0xff, keys[i][2] & 0xff);
	}

	udp.send(BRIDGE_HOST, BRIDGE_PORT, packet);
}

function sendColor(type, color) {
	const pid = device.productId();
	// "SBD1", version 1, type, product id (big endian), R, G, B
	const packet = [0x53, 0x42, 0x44, 0x31, 1, type, (pid >> 8) & 0xff, pid & 0xff, color[0] & 0xff, color[1] & 0xff, color[2] & 0xff];
	udp.send(BRIDGE_HOST, BRIDGE_PORT, packet);
}

function hexToRgb(hex) {
	const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
	return result ? [parseInt(result[1], 16), parseInt(result[2], 16), parseInt(result[3], 16)] : [0, 0, 0];
}
