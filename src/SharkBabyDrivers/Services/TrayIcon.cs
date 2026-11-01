using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SharkBabyDrivers.Localization;
using SharkBabyDrivers.ViewModels;
using Forms = System.Windows.Forms;

namespace SharkBabyDrivers.Services;

/// <summary>
/// Tray icon: shows who drives the keyboard lighting, the connection in use and whether SignalRGB's colours are
/// arriving (a dot in the icon takes the current colour). Its menu switches the owner, toggles "start with Windows",
/// opens the window or exits. Closing the window only hides it here.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Window _window;
    private readonly MainVm _vm;
    private readonly Forms.NotifyIcon _icon;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Icon _baseIcon;
    private Icon? _drawn;
    private string _drawnKey = "";
    private bool _hintShown;
    private string _lastState = "";

    public TrayIcon(System.Windows.Window window, MainVm vm)
    {
        _window = window;
        _vm = vm;
        var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"))!.Stream;
        _baseIcon = new Icon(stream, new Size(16, 16));
        _icon = new Forms.NotifyIcon { Icon = _baseIcon, Visible = true, Text = "SharkBabyDrivers" };
        _icon.ContextMenuStrip = new Forms.ContextMenuStrip();
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) App.ShowMainWindow(); };
        window.IsVisibleChanged += (_, _) =>
        {
            if (!window.IsVisible) TrimSoon();
            if (window.IsVisible || App.ExitRequested || _hintShown) return;
            _hintShown = true;
            _icon.ShowBalloonTip(4000, "SharkBabyDrivers", Loc.T("tray_hidden"), Forms.ToolTipIcon.Info);
        };
        _vm.Lighting.Changed += Update;
        var ticks = 0;
        _timer.Tick += (_, _) => { Update(); if (++ticks % 10 == 0) _vm.Lighting.Trace("tick"); };
        _timer.Start();
        Autostart.Refresh();
        Update();
        if (App.StartedInTray) TrimSoon();
    }

    /// <summary>
    /// With only the tray icon up, hands the memory the hidden window is not using back to Windows (it is paged back
    /// in if the window is opened again), so the app stays light while it only relays the keyboard colours.
    /// </summary>
    private void TrimSoon()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (_window.IsVisible) return;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            SetProcessWorkingSetSize(self.Handle, -1, -1);
        };
        t.Start();
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    private void Update()
    {
        var lighting = _vm.Lighting;
        var status = lighting.Describe();
        var text = $"SharkBabyDrivers\n{lighting.ConnectionText}\n{status}";
        _icon.Text = text.Length > 127 ? text[..127] : text;

        // Owner switches are worth a notification (they happen in the background, e.g. right after sign-in).
        if (lighting.State is "signal" or "app" && _lastState is "switching_to_signal" or "switching_to_app")
            _icon.ShowBalloonTip(3000, "SharkBabyDrivers", status, Forms.ToolTipIcon.Info);
        _lastState = lighting.State;

        Color? dot = null;
        if (lighting.SignalRgbOwns)
            dot = lighting.Receiving && lighting.LastColor is { } c ? Color.FromArgb(c.R, c.G, c.B) : Color.Gray;
        var key = dot?.ToArgb().ToString() ?? "none";
        if (key == _drawnKey) return;
        _drawnKey = key;
        var old = _drawn;
        _drawn = dot is { } d ? WithDot(d) : null;
        _icon.Icon = _drawn ?? _baseIcon;
        old?.Dispose();
    }

    private Icon WithDot(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawIcon(_baseIcon, new Rectangle(0, 0, 16, 16));
            using var ring = new SolidBrush(Color.FromArgb(20, 20, 20));
            using var fill = new SolidBrush(color);
            g.FillEllipse(ring, 8, 8, 8, 8);
            g.FillEllipse(fill, 9, 9, 6, 6);
        }
        var h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();
        var lighting = _vm.Lighting;

        var title = new Forms.ToolStripMenuItem("SharkBabyDrivers " + App.Version) { Enabled = false };
        title.Font = new Font(title.Font, FontStyle.Bold);
        menu.Items.Add(title);
        menu.Items.Add(new Forms.ToolStripMenuItem(lighting.ConnectionText) { Enabled = false });
        menu.Items.Add(new Forms.ToolStripMenuItem(lighting.Describe()) { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());

        var signal = new Forms.ToolStripMenuItem(Loc.T("tray_light_signal")) { Checked = lighting.SignalRgbOwns };
        signal.Click += (_, _) => SetOwner(true);
        var app = new Forms.ToolStripMenuItem(Loc.T("tray_light_app")) { Checked = !lighting.SignalRgbOwns };
        app.Click += (_, _) => SetOwner(false);
        menu.Items.Add(signal);
        menu.Items.Add(app);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var autostart = new Forms.ToolStripMenuItem(Loc.T("tray_autostart")) { Checked = Autostart.Enabled };
        autostart.Click += (_, _) =>
        {
            try { Autostart.Enabled = !Autostart.Enabled; }
            catch (Exception ex) { App.ReportError(ex); }
        };
        menu.Items.Add(autostart);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var open = new Forms.ToolStripMenuItem(Loc.T("tray_open"));
        open.Font = new Font(open.Font, FontStyle.Bold);
        open.Click += (_, _) => App.ShowMainWindow();
        menu.Items.Add(open);
        var exit = new Forms.ToolStripMenuItem(Loc.T("tray_exit"));
        exit.Click += (_, _) => App.RequestExit();
        menu.Items.Add(exit);
    }

    private void SetOwner(bool signal)
    {
        if (signal && SignalRgbPlugin.State() != PluginState.Installed)
        {
            try { SignalRgbPlugin.Install(); _icon.ShowBalloonTip(4000, "SharkBabyDrivers", Loc.T("signal_plugin_installed"), Forms.ToolTipIcon.Info); }
            catch (Exception ex) { App.ReportError(ex); }
        }
        _vm.Lighting.SetOwner(signal);
        Update();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _timer.Stop();
        _vm.Lighting.Changed -= Update;
        _icon.Visible = false;
        _icon.Dispose();
        _drawn?.Dispose();
        _baseIcon.Dispose();
    }
}
