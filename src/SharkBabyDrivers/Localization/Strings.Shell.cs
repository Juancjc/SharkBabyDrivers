namespace SharkBabyDrivers.Localization;

internal static partial class Strings
{
    private static Dictionary<string, string> ShellPt => new()
    {
        ["shell_devices"] = "SEUS DISPOSITIVOS",
        ["shell_workspace"] = "CONTROLE DO DISPOSITIVO",
        ["shell_connected"] = "Pronto para configurar",
        ["shell_loading"] = "Conectando",
        ["shell_attention"] = "Verificar conexão",
        ["shell_background_title"] = "Sempre por perto",
        ["shell_background_hint"] = "Feche a janela para continuar na bandeja.",
        ["shell_apply_hint"] = "Você ajusta. Só gravamos ao aplicar.",
        ["shell_brand_tagline"] = "Pequeno no nome. Seu setup no controle.",
        ["shell_empty_hint"] = "Seu próximo ajuste começa por aqui.",
    };

    private static Dictionary<string, string> ShellEn => new()
    {
        ["shell_devices"] = "YOUR DEVICES",
        ["shell_workspace"] = "DEVICE CONTROLS",
        ["shell_connected"] = "Ready to configure",
        ["shell_loading"] = "Connecting",
        ["shell_attention"] = "Check connection",
        ["shell_background_title"] = "Always within reach",
        ["shell_background_hint"] = "Close the window to keep running in the tray.",
        ["shell_apply_hint"] = "Make it yours. Save when you apply.",
        ["shell_brand_tagline"] = "Small name. Full control of your setup.",
        ["shell_empty_hint"] = "Your next adjustment starts here.",
    };
}
