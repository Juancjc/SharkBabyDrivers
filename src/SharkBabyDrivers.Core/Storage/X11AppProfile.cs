using SharkBabyDrivers.Core.Devices.X11;

namespace SharkBabyDrivers.Core.Storage;

/// <summary>
/// The parts of the X11 configuration the firmware cannot report back (buttons and macros).
/// Kept per device so the app always knows what it last wrote.
/// </summary>
public sealed class X11AppProfile
{
    public sealed class ActionDto
    {
        public byte Code { get; set; }
        public byte P1 { get; set; }
        public byte P2 { get; set; }
        public ButtonAction ToAction() => new(Code, P1, P2);
        public static ActionDto From(ButtonAction a) => new() { Code = a.Code, P1 = a.P1, P2 = a.P2 };
    }

    public sealed class StepDto
    {
        public byte Key { get; set; }
        public bool Pressed { get; set; }
        public int DelayMs { get; set; }
    }

    public sealed class MacroDto
    {
        public string Name { get; set; } = "";
        public MacroPlayMode Mode { get; set; } = MacroPlayMode.RepeatCount;
        public int Repeat { get; set; } = 1;
        public List<StepDto> Steps { get; set; } = new();

        public MacroDefinition ToDefinition() =>
            new(Name, Steps.Select(s => new MacroStep(s.Key, s.Pressed, s.DelayMs)).ToList(), Mode, Repeat);
    }

    /// <summary>18 slots of report 0x08.</summary>
    public List<ActionDto> Buttons { get; set; } = X11Buttons.FactoryDefaults().Select(ActionDto.From).ToList();
    public List<MacroDto> Macros { get; set; } = new();
    /// <summary>Physical button (slot index) → macro name.</summary>
    public Dictionary<int, string> ButtonMacros { get; set; } = new();
    /// <summary>False until the app has written buttons to this mouse at least once.</summary>
    public bool ButtonsWritten { get; set; }

    /// <summary>Optional full config (hex reports) for profile export/import.</summary>
    public Dictionary<string, string>? Reports { get; set; }

    private static string PathFor(string deviceKey) => Path.Combine(AppPaths.Profiles, AppPaths.SafeName(deviceKey) + ".json");

    public static X11AppProfile Load(string deviceKey) =>
        JsonStore.LoadOrDefault(PathFor(deviceKey), () => new X11AppProfile());

    public void Save(string deviceKey) => JsonStore.Save(PathFor(deviceKey), this);

    public X11AppProfile Clone() =>
        System.Text.Json.JsonSerializer.Deserialize<X11AppProfile>(System.Text.Json.JsonSerializer.Serialize(this, JsonStore.Options), JsonStore.Options)!;
}
