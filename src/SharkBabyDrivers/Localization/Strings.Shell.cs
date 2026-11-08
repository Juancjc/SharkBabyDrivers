namespace SharkBabyDrivers.Localization;

internal static partial class Strings
{
    private static Dictionary<string, string> ShellPt => new()
    {
        ["reset_device"] = "Resetar dispositivo",
        ["reset_profile"] = "Resetar perfil atual",
        ["reset_all"] = "Resetar todos os dispositivos",
        ["reset_device_confirm"] = "Restaurar este dispositivo pelo backup da primeira conex�o? As altera��es pendentes ser�o descartadas. Um backup de recupera��o ser� salvo. Os perfis salvos n�o ser�o apagados. Isso n�o reinicia o hardware nem equivale a um reset de f�brica.",
        ["reset_profile_confirm"] = "Restaurar o perfil ativo pelo backup original? No teclado, restaura as teclas e os ajustes HE dispon�veis desse perfil; nos mouses, restaura as configura��es e limpa as personaliza��es locais do X11. Altera��es pendentes ser�o descartadas. Os perfis salvos ser�o mantidos.",
        ["reset_all_confirm"] = "Restaurar todos os dispositivos conectados pelo backup original de cada um? Altera��es pendentes ser�o descartadas. Backups de recupera��o e perfis salvos ser�o mantidos. Isso n�o reinicia fisicamente os dispositivos.",
        ["reset_unavailable"] = "O dispositivo precisa estar pronto, sem opera��o em andamento e com backup original dispon�vel.",
        ["reset_done"] = "Configura��o restaurada e relida do dispositivo.",
        ["reset_failed"] = "N�o foi poss�vel concluir a restaura��o. Verifique a conex�o e clique em Tentar novamente.",
        ["shell_devices"] = "SEUS DISPOSITIVOS",
        ["shell_workspace"] = "CONTROLE DO DISPOSITIVO",
        ["shell_connected"] = "Pronto para configurar",
        ["shell_loading"] = "Conectando",
        ["shell_attention"] = "Verificar conex�o",
        ["shell_background_title"] = "Sempre por perto",
        ["shell_background_hint"] = "Feche a janela para continuar na bandeja.",
        ["shell_apply_hint"] = "Voc� ajusta. S� gravamos ao aplicar.",
        ["shell_brand_tagline"] = "Pequeno no nome. Seu setup no controle.",
        ["shell_empty_hint"] = "Seu pr�ximo ajuste come�a por aqui.",
    };

    private static Dictionary<string, string> ShellEn => new()
    {
        ["reset_device"] = "Reset device",
        ["reset_profile"] = "Reset current profile",
        ["reset_all"] = "Reset all devices",
        ["reset_device_confirm"] = "Restore this device from its first-connection backup? Pending edits will be discarded. A recovery backup will be saved. Saved profiles are kept. This does not reboot hardware or perform a factory reset.",
        ["reset_profile_confirm"] = "Restore the active profile from the original backup? Keyboards restore keys and available HE settings for that profile; mice restore settings and clear local X11 customizations. Pending edits will be discarded. Saved profiles are kept.",
        ["reset_all_confirm"] = "Restore all connected devices from their original backups? Pending edits will be discarded. Recovery backups and saved profiles are kept. This does not physically reboot devices.",
        ["reset_unavailable"] = "The device must be ready, idle and have an original backup.",
        ["reset_done"] = "Settings restored and read back from the device.",
        ["reset_failed"] = "Restore could not complete. Check the connection and click Retry.",
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
