using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using SharkBabyDrivers.Core.Storage;
using SharkBabyDrivers.Core.Updates;
using SharkBabyDrivers.Localization;

namespace SharkBabyDrivers.Services;

/// <summary>Only public stable releases from the project's own repository are accepted.</summary>
public sealed class AppUpdater : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private bool _busy;
    public AppRelease? AvailableRelease { get; private set; }
    public bool IsBusy => _busy;
    public event Action? StateChanged;
    private string? _windowTitle;
    private string Pending => Path.Combine(AppPaths.Root, "update-news.json");
    private bool English => Loc.I.Language == "en";
    private string T(string pt, string en) => English ? en : pt;

    public AppUpdater()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SharkBabyDrivers/" + App.Version);

    }

    public async void Start()
    {
        if (App.IsPreview) return;
        ShowNews();

        await CheckAsync();
    }



    public void ShowNews()
    {
        if (App.Current.MainWindow is not { IsVisible: true }) return;
        try
        {
            if (!File.Exists(Pending)) return;
            var news = JsonSerializer.Deserialize<News>(File.ReadAllText(Pending));
            if (news is null || news.Version != App.Version) return;
            ShowText(T("Atualização concluída", "Update complete") + " — v" + news.Version, news.Notes);
            File.Delete(Pending);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    public async Task CheckAsync(bool manual = false, bool install = false)
    {
        if (_busy || App.IsPreview) return;
        _busy = true;
        StateChanged?.Invoke();
        var accepted = false;
        try
        {
            var release = install ? AvailableRelease : null;
            if (release is null)
            {
                var json = await _http.GetStringAsync("https://api.github.com/repos/Juancjc/SharkBabyDrivers/releases/latest");
                release = AppRelease.Parse(json, App.Version);
            }
            AvailableRelease = release;
            StateChanged?.Invoke();
            if (release is null)
            {
                if (manual) MessageBox.Current(T("Você já está na versão mais recente disponível.", "You have the latest available version."), "SharkBabyDrivers", MessageBoxImage.Information);
                return;
            }
            if (!install)
            {
                if (manual) App.ShowMainWindow();
                return;
            }
            if (!App.Confirm(T($"A versão {release.Version} está disponível. Baixar e atualizar agora?\n\nO programa será fechado e aberto novamente. Seus perfis serão preservados.", $"Version {release.Version} is available. Download and update now?\n\nThe app will close and reopen. Your profiles will be preserved."))) return;
            accepted = true;
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable not found.");
            // Developer multi-file output cannot be replaced with a single-file release safely.
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "SharkBabyDrivers.dll")))
                throw new InvalidOperationException(T("Use a versão instalada ou portátil para atualizar pelo programa.", "Use the installed or portable release to update from the app."));
            _windowTitle = App.Current.MainWindow.Title;
            App.Current.MainWindow.Title = T("Baixando atualização…", "Downloading update…");
            var folder = Path.Combine(Path.GetTempPath(), "SharkBabyDrivers-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var zip = Path.Combine(folder, "release.zip");
            await using (var input = await _http.GetStreamAsync(release.Download))
            await using (var output = File.Create(zip)) await input.CopyToAsync(output);
            await using (var input = File.OpenRead(zip))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(input)).Equals(release.Digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(T("O download falhou na verificação de integridade. Tente novamente.", "Download integrity verification failed. Try again."));
            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.GetEntry("SharkBabyDrivers.exe") ?? throw new InvalidDataException("Executable missing from update.");
                entry.ExtractToFile(Path.Combine(folder, "new.exe"));
            }
            if (App.Current.MainWindow is MainWindow window && !window.ConfirmLeave()) return;
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(Path.Combine(folder, "news.json"), JsonSerializer.Serialize(new News(release.Version, release.Notes)));
            var script = Path.Combine(folder, "apply.ps1");
            File.WriteAllText(script, ApplyScript, new UTF8Encoding(true));
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-ParentId", Environment.ProcessId.ToString(), "-Target", executable, "-News", Pending }) start.ArgumentList.Add(arg);
            using var helper = Process.Start(start) ?? throw new IOException("Could not start updater.");
            App.ExitForUpdate();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            if (manual || accepted) MessageBox.Current(T("Não foi possível atualizar. Você pode tentar novamente pelo menu da bandeja.\n\n", "Could not update. Try again from the tray menu.\n\n") + ex.Message, "SharkBabyDrivers", MessageBoxImage.Warning);
        }
        finally
        {
            if (_windowTitle is not null && App.Current.MainWindow is { } main) main.Title = _windowTitle;
            _windowTitle = null;
            _busy = false;
            StateChanged?.Invoke();
        }
    }

    private static void ShowText(string title, string text)
    {
        var box = new System.Windows.Controls.TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Margin = new Thickness(16) };
        new Window { Title = title, Width = 650, Height = 460, Owner = App.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = box }.ShowDialog();
    }

    public void Dispose() { _http.Dispose(); }
    public sealed record News(string Version, string Notes);

    internal const string ApplyScript = """
param([int]$ParentId, [string]$Target, [string]$News)
$ErrorActionPreference = 'Stop'
$backup = "$Target.update-backup"
$replaced = $false
try {
    $parent = Get-Process -Id $ParentId -ErrorAction SilentlyContinue
    if ($parent -and -not $parent.WaitForExit(60000)) { throw 'The app did not exit.' }
    Copy-Item -LiteralPath $Target -Destination $backup -Force
    $replaced = $true
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'new.exe') -Destination $Target -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'news.json') -Destination $News -Force
    Start-Process -FilePath $Target
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    foreach ($file in @('new.exe', 'release.zip', 'news.json')) {
        Remove-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Force -ErrorAction SilentlyContinue
    }
} catch {
    if ($replaced) { Copy-Item -LiteralPath $backup -Destination $Target -Force -ErrorAction SilentlyContinue }
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show("Update failed. Your previous version was preserved. / Falha ao atualizar. A versão anterior foi preservada.`n$($_.Exception.Message)", 'SharkBabyDrivers')
    if (Test-Path -LiteralPath $Target) { Start-Process -FilePath $Target }
}
""";
}
