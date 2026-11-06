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

    /// <summary>True once the user chose Exit (closing the window only hides it to the tray).</summary>
    public static bool ExitRequested { get; private set; }
    /// <summary>Started by Windows at sign-in (or with --tray): stay in the tray, light, until the window is opened.</summary>
    public static bool StartedInTray { get; } = Environment.GetCommandLineArgs().Contains("--tray", StringComparer.OrdinalIgnoreCase);
    /// <summary>Isolated UI preview: separate settings and instance, with simulated devices only.</summary>
    public static bool IsPreview { get; } = Environment.GetCommandLineArgs().Contains("--preview", StringComparer.OrdinalIgnoreCase);

    private EventWaitHandle? _showSignal;
    private Services.TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (IsPreview) AppPaths.Root = Path.Combine(AppContext.BaseDirectory, "preview-data");
        using var identity = WindowsIdentity.GetCurrent();
        var sid = (identity.User?.Value ?? Environment.UserName) + (IsPreview ? "-preview" : "");
        _instanceMutex = new Mutex(true, @"Local\SharkBabyDrivers-" + sid, out var firstInstance);
        if (!firstInstance)
        {
            // This handle does not own the existing mutex.
            _instanceMutex.Dispose();
            _instanceMutex = null;
            // Already running (maybe only in the tray): ask it to show its window and leave quietly.
            if (EventWaitHandle.TryOpenExisting(@"Local\SharkBabyDrivers-show-" + sid, out var show))
            {
                using (show) if (!StartedInTray) show.Set();
                Shutdown();
                return;
            }
        }
        if (!firstInstance || (!IsPreview && HasLegacyInstance()))
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

        var window = new MainWindow();
        if (IsPreview) window.Title += " · " + Loc.T("tray_preview");
        MainWindow = window;
        _tray = new Services.TrayIcon(window, window.Vm);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SharkBabyDrivers-show-" + sid);
        var signal = _showSignal;
        new Thread(() =>
        {
            try { while (signal.WaitOne()) Dispatcher.BeginInvoke(ShowMainWindow); }
            catch (ObjectDisposedException) { }
        }) { IsBackground = true, Name = "show-window" }.Start();
        if (StartedInTray) window.PrepareHidden();
        else window.Show();
    }

    public static void ShowMainWindow()
    {
        if (Current.MainWindow is MainWindow main)
        {
            main.RestoreFromTray();
            return;
        }
        if (Current.MainWindow is not Window w) return;
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    /// <summary>Leaves the app for real (tray menu "Exit").</summary>
    public static void RequestExit()
    {
        if (Current.MainWindow is MainWindow w && !w.ConfirmLeave()) return;
        ExitRequested = true;
        Current.MainWindow?.Close();
        Current.Shutdown();
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
        _tray?.Dispose();
        _showSignal?.Dispose();
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
        LogMessage(ex.ToString());
    }

    internal static void LogMessage(string message)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(AppPaths.Root);
                var info = new FileInfo(AppPaths.Log);
                if (info.Exists && info.Length > 1_000_000) info.Delete();
                File.AppendAllText(AppPaths.Log, $"[{DateTime.Now:O}] {message}\n\n");
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
