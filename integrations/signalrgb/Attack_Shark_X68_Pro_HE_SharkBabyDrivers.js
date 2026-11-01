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
// SharkBabyDrivers must be running (it can stay minimised) for the colours to reach the keyboard.
//
// Installed by SharkBabyDrivers into Documents\WhirlwindFX\Plugins; it replaces the stock plugin for these ids.

export function Name() { return "Attack Shark X68 Pro HE"; }
export function Version() { return "1.0.0"; }
export function VendorId() { return 0x3151; }
// 0x5026 = 2.4G receiver, 0x502F = X68 PRO HE over the cable, 0x502D = X68 HE over the cable.
export function ProductId() { return [0x5026, 0x502F, 0x502D]; }
export function Publisher() { return "SharkBabyDrivers"; }
export function Documentation() { return "troubleshooting/attack-shark"; }
export function Size() { return [1, 1]; }
export function DeviceType() { return "keyboard"; }
export function Validate(endpoint) { return endpoint.interface === 2 && endpoint.usage_page === 0xffff; }
export function ImageUrl() { return "https://assets.signalrgb.com/devices/brands/attack-shark/keyboards/x68-pro-he.png"; }
/* global
shutdownColor:readonly
LightingMode:readonly
forcedColor:readonly
*/
export function ControllableParameters() {
	return [
		{property:"shutdownColor", group:"lighting", label:"Shutdown Color", description: "This color is applied to the device when the System, or SignalRGB is shutting down", min:"0", max:"360", type:"color", default:"#000000"},
		{property:"LightingMode", group:"lighting", label:"Lighting Mode", description: "Determines where the device's RGB comes from. Canvas will pull from the active Effect, while Forced will override it to a specific color", type:"combobox", values:["Canvas", "Forced"], default:"Canvas"},
		{property:"forcedColor", group:"lighting", label:"Forced Color", description: "The color used when 'Forced' Lighting Mode is enabled", min:"0", max:"360", type:"color", default:"#009bde"},
	];
}

const BRIDGE_HOST = "127.0.0.1";
const BRIDGE_PORT = 47851;
const HEARTBEAT_MS = 500;

const vLedNames = ["Keyboard"];
const vLedPositions = [[0, 0]];

export function LedNames() { return vLedNames; }
export function LedPositions() { return vLedPositions; }

let lastColor = null;
let lastSent = 0;

export function Initialize() {
	device.addFeature("udp");
	lastColor = null;
}

export function Render() {
	const color = LightingMode === "Forced" ? hexToRgb(forcedColor) : device.color(0, 0);
	const now = Date.now();
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
