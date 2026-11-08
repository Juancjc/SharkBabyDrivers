namespace SharkBabyDrivers.Localization;

internal static partial class Strings
{
    private static Dictionary<string, string> ShellPt => new()
    {
        ["reset_device"] = "Resetar dispositivo",
        ["reset_profile"] = "Resetar perfil atual",
        ["reset_all"] = "Resetar todos os dispositivos",
        ["reset_device_confirm"] = "Restaurar este dispositivo pelo backup da primeira conexão? As alterações pendentes serão descartadas. Um backup de recuperação será salvo. Os perfis salvos não serão apagados. Isso não reinicia o hardware nem equivale a um reset de fábrica.",
        ["reset_profile_confirm"] = "Restaurar o perfil ativo pelo backup original? No teclado, restaura as teclas e os ajustes HE disponíveis desse perfil; nos mouses, restaura as configurações e limpa as personalizações locais do X11. Alterações pendentes serão descartadas. Os perfis salvos serão mantidos.",
        ["reset_all_confirm"] = "Restaurar todos os dispositivos conectados pelo backup original de cada um? Alterações pendentes serão descartadas. Backups de recuperação e perfis salvos serão mantidos. Isso não reinicia fisicamente os dispositivos.",
        ["reset_unavailable"] = "O dispositivo precisa estar pronto, sem operação em andamento e com backup original disponível.",
        ["reset_done"] = "Configuração restaurada e relida do dispositivo.",
        ["reset_failed"] = "Não foi possível concluir a restauração. Verifique a conexão e clique em Tentar novamente.",
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
