namespace SharkBabyDrivers.Core.Devices.X11;

/// <summary>What a mouse button does: 3 bytes on the wire (action, param1, param2).</summary>
public readonly record struct ButtonAction(byte Code, byte P1 = 0, byte P2 = 0)
{
    public const byte KeyboardCode = 0x11;
    public const byte MacroCode = 0x12;

    public bool IsKeyboard => Code == KeyboardCode;
    public bool IsMacro => Code == MacroCode;

    public static ButtonAction Key(KeyModifiers mods, byte hidUsage) => new(KeyboardCode, (byte)mods, hidUsage);
    public static ButtonAction Macro(int slotId) => new(MacroCode, 0, (byte)slotId);
}

[Flags]
public enum KeyModifiers : byte
{
    None = 0,
    Ctrl = 0x01,
    Shift = 0x02,
    Alt = 0x04,
    Win = 0x08,
}

public enum ActionGroup { Mouse, Dpi, Media, Browser, Shortcut, Advanced }

/// <summary>One entry of the button-assignment menu (same entries as the official software).</summary>
public sealed record ActionPreset(string Id, ActionGroup Group, ButtonAction Action);

public static class X11Actions
{
    // Codes from the official software's assignment table (AttackShark.exe @0x4EAA18) and OpenSharkX11.
    public static readonly ButtonAction Disabled = new(0x01);
    public static readonly ButtonAction Left = new(0x02);
    public static readonly ButtonAction Right = new(0x03);
    public static readonly ButtonAction Middle = new(0x04);
    public static readonly ButtonAction Backward = new(0x05);
    public static readonly ButtonAction Forward = new(0x06);
    public static readonly ButtonAction DpiCycle = new(0x0D);
    public static readonly ButtonAction ScrollUp = new(0x09);
    public static readonly ButtonAction ScrollDown = new(0x0A);

    public static readonly IReadOnlyList<ActionPreset> Presets =
    [
        new("left_click", ActionGroup.Mouse, Left),
        new("right_click", ActionGroup.Mouse, Right),
        new("middle_click", ActionGroup.Mouse, Middle),
        new("forward", ActionGroup.Mouse, Forward),
        new("backward", ActionGroup.Mouse, Backward),
        new("double_click", ActionGroup.Mouse, new(0x07)),
        new("fire_button", ActionGroup.Mouse, new(0x08)),
        new("scroll_up", ActionGroup.Mouse, ScrollUp),
        new("scroll_down", ActionGroup.Mouse, ScrollDown),
        new("tilt_left", ActionGroup.Mouse, new(0x0B)),
        new("tilt_right", ActionGroup.Mouse, new(0x0C)),
        new("disabled", ActionGroup.Mouse, Disabled),

        new("dpi_cycle", ActionGroup.Dpi, DpiCycle),
        new("dpi_up", ActionGroup.Dpi, new(0x0E)),
        new("dpi_down", ActionGroup.Dpi, new(0x0F)),
        new("easy_aim", ActionGroup.Dpi, new(0x10, 0x00, 0x03)),

        new("media_player", ActionGroup.Media, new(0x15)),
        new("play_pause", ActionGroup.Media, new(0x18)),
        new("media_stop", ActionGroup.Media, new(0x19)),
        new("previous_track", ActionGroup.Media, new(0x16)),
        new("next_track", ActionGroup.Media, new(0x17)),
        new("volume_up", ActionGroup.Media, new(0x1B)),
        new("volume_down", ActionGroup.Media, new(0x1C)),
        new("mute", ActionGroup.Media, new(0x1A)),

        new("browser_home", ActionGroup.Browser, new(0x25)),
        new("browser_favorites", ActionGroup.Browser, ButtonAction.Key(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x12)),
        new("browser_forward", ActionGroup.Browser, new(0x20)),
        new("browser_backward", ActionGroup.Browser, new(0x21)),
        new("browser_stop", ActionGroup.Browser, new(0x22)),
        new("browser_refresh", ActionGroup.Browser, new(0x24)),
        new("browser_search", ActionGroup.Browser, new(0x26)),
        new("email", ActionGroup.Browser, new(0x1E)),
        new("calculator", ActionGroup.Browser, new(0x1D)),
        new("my_computer", ActionGroup.Browser, new(0x23)),

        new("cut", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Ctrl, 0x1B)),
        new("copy", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Ctrl, 0x06)),
        new("paste", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Ctrl, 0x19)),
        new("undo", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Ctrl, 0x1D)),
        new("redo", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Ctrl, 0x1C)),
        new("open", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Ctrl, 0x12)),
        new("save", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Ctrl, 0x16)),
        new("find", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Ctrl, 0x09)),
        new("select_all", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Ctrl, 0x04)),
        new("print", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Ctrl, 0x13)),
        new("close_window", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Alt, 0x3D)),
        new("swap_windows", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Alt, 0x2B)),
        new("show_desktop", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Win, 0x07)),
        new("run_command", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Win, 0x15)),
        new("lock_pc", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Win, 0x0F)),
        new("screen_capture", ActionGroup.Shortcut, ButtonAction.Key(KeyModifiers.Win | KeyModifiers.Shift, 0x16)),
    ];

    public static ActionPreset? FindPreset(ButtonAction action) =>
        Presets.FirstOrDefault(p => p.Action == action);
}

/// <summary>The six physical buttons of the X11 and the slot each occupies in report 0x08.</summary>
public enum X11Button
{
    Left = 0,
    Right = 1,
    Middle = 2,
    Dpi = 5,
    Forward = 6,
    Backward = 7,
}

public static class X11Buttons
{
    public const int Slots = 18;

    public static readonly X11Button[] Physical =
        [X11Button.Left, X11Button.Right, X11Button.Middle, X11Button.Forward, X11Button.Backward, X11Button.Dpi];

    /// <summary>Factory assignment per slot (unused slots are "disabled", wheel slots 16/17 are scroll up/down).</summary>
    public static ButtonAction[] FactoryDefaults()
    {
        var slots = Enumerable.Repeat(X11Actions.Disabled, Slots).ToArray();
        slots[(int)X11Button.Left] = X11Actions.Left;
        slots[(int)X11Button.Right] = X11Actions.Right;
        slots[(int)X11Button.Middle] = X11Actions.Middle;
        slots[(int)X11Button.Dpi] = X11Actions.DpiCycle;
        slots[(int)X11Button.Forward] = X11Actions.Forward;
        slots[(int)X11Button.Backward] = X11Actions.Backward;
        slots[16] = X11Actions.ScrollUp;
        slots[17] = X11Actions.ScrollDown;
        return slots;
    }

    /// <summary>Report 0x08: 08 3B 01, 18 × (code,p1,p2), 16-bit BE sum of bytes 3..56.</summary>
    public static byte[] BuildReport(IReadOnlyList<ButtonAction> slots)
    {
        if (slots.Count != Slots) throw new ArgumentException($"{Slots} slots esperados", nameof(slots));
        var r = new byte[X11Protocol.LenButtons];
        r[0] = X11Protocol.RidButtons;
        r[1] = X11Protocol.LenButtons;
        r[2] = 0x01;
        for (var i = 0; i < Slots; i++)
        {
            r[3 + i * 3] = slots[i].Code;
            r[4 + i * 3] = slots[i].P1;
            r[5 + i * 3] = slots[i].P2;
        }
        X11Protocol.SealButtons(r);
        return r;
    }

    /// <summary>Macro id used by the firmware for a button: slot index + 1.</summary>
    public static int MacroSlotId(X11Button button) => (int)button + 1;
}
