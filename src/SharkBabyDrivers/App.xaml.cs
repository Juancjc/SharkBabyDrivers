using System.Reflection;
using System.Windows;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Threading;
using SharkBabyDrivers.Core.Storage;
using SharkBabyDrivers.Localization;

namespace SharkBabyDrivers;

public partial class App : Application
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0";

    private static readonly object LogLock = new();
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        using var identity = WindowsIdentity.GetCurrent();
        _instanceMutex = new Mutex(true, @"Local\SharkBabyDrivers-" + (identity.User?.Value ?? Environment.UserName), out var firstInstance);
        if (!firstInstance)
        {
            // This handle does not own the existing mutex.
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        if (!firstInstance || HasLegacyInstance())
        {
            ReleaseInstanceMutex();
            var english = AppSettings.Load().Language == "en";
            System.Windows.MessageBox.Show(
                english
                    ? "SharkBabyDrivers is already open. Use the existing window or close it before opening another copy. Two copies running together can prevent the keyboard from connecting."
                    : "O SharkBabyDrivers já está aberto. Use a janela existente ou feche-a antes de abrir outra cópia. Duas cópias em execução podem impedir a conexão com o teclado.",
                "SharkBabyDrivers", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log(a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log(a.Exception); a.SetObserved(); };
        base.OnStartup(e);
    }

    private static bool HasLegacyInstance()
    {
        // Installed versions predating the mutex must also be kept away from the HID connection.
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
            using (process)
                try
                {
                    if (process.Id != current.Id && process.SessionId == current.SessionId) return true;
                }
                catch (InvalidOperationException) { /* The other process exited during discovery. */ }
        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseInstanceMutex();
        base.OnExit(e);
    }

    private void ReleaseInstanceMutex()
    {
        if (_instanceMutex is null) return;
        _instanceMutex.ReleaseMutex();
        _instanceMutex.Dispose();
        _instanceMutex = null;
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportError(e.Exception);
        e.Handled = true;
    }

    public static void Log(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(AppPaths.Root);
                var info = new FileInfo(AppPaths.Log);
                if (info.Exists && info.Length > 1_000_000) info.Delete();
                File.AppendAllText(AppPaths.Log, $"[{DateTime.Now:O}] {ex}\n\n");
            }
        }
        catch
        {
            // logging must never crash the app
        }
    }

    public static void ReportError(Exception ex)
    {
        Log(ex);
        var message = ex is AggregateException { InnerException: { } inner } ? inner.Message : ex.Message;
        MessageBox.Current(message, Loc.T("error"), MessageBoxImage.Warning);
    }

    public static bool Confirm(string text) =>
        MessageBox.Current(text, Loc.T("confirm"), MessageBoxImage.Question, yesNo: true);
}

/// <summary>MessageBox owned by the main window (keeps it centered and on top of the app).</summary>
internal static class MessageBox
{
    public static bool Current(string text, string caption, MessageBoxImage image, bool yesNo = false)
    {
        var owner = Application.Current?.MainWindow;
        var buttons = yesNo ? MessageBoxButton.YesNo : MessageBoxButton.OK;
        var r = owner is { IsLoaded: true }
            ? System.Windows.MessageBox.Show(owner, text, caption, buttons, image)
            : System.Windows.MessageBox.Show(text, caption, buttons, image);
        return r is MessageBoxResult.Yes or MessageBoxResult.OK;
    }
}
