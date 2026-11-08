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
    private readonly DispatcherTimer _trimTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly Font _menuFont = new("Segoe UI", 10f);
    private readonly Font _titleFont = new("Segoe UI", 12f, FontStyle.Bold);
    private readonly Font _sectionFont = new("Segoe UI", 8f, FontStyle.Bold);
    private readonly Icon _baseIcon;
    private readonly Bitmap _brandImage;
    private Icon? _drawn;
    private string _drawnKey = "";
    private bool _hintShown;
    private string _lastState = "";
    private int _ticks;
    private bool _disposed;

    public TrayIcon(System.Windows.Window window, MainVm vm)
    {
        _window = window;
        _vm = vm;
        using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"))!.Stream)
        using (var loaded = new Icon(stream, new Size(32, 32))) _baseIcon = (Icon)loaded.Clone();
        _brandImage = _baseIcon.ToBitmap();
        _icon = new Forms.NotifyIcon { Icon = _baseIcon, Visible = true, Text = "SharkBabyDrivers" };
        _icon.ContextMenuStrip = new Forms.ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(),
            Font = _menuFont,
            BackColor = TrayMenuRenderer.Background,
            ForeColor = TrayMenuRenderer.Text,
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Forms.Padding(6, 7, 6, 7),
            ImageScalingSize = new Size(24, 24),
            DropShadowEnabled = true,
        };
        _icon.ContextMenuStrip.Opening += MenuOpening;
        _icon.MouseClick += IconMouseClick;
        _icon.MouseDoubleClick += IconDoubleClick;
        window.IsVisibleChanged += WindowVisibilityChanged;
        _vm.Lighting.Changed += Update;
        _timer.Tick += TimerTick;
        _trimTimer.Tick += TrimTick;
        _timer.Start();
        if (!App.IsPreview) Autostart.Refresh();
        Update();
        if (App.StartedInTray) TrimSoon();
    }

    /// <summary>
    /// With only the tray icon up, hands the memory the hidden window is not using back to Windows (it is paged back
    /// in if the window is opened again), so the app stays light while it only relays the keyboard colours.
    /// </summary>
    private void TrimSoon()
    {
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    private void TrimTick(object? sender, EventArgs e)
    {
        _trimTimer.Stop();
        if (_disposed || _window.IsVisible) return;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        SetProcessWorkingSetSize(self.Handle, -1, -1);
    }

    private void MenuOpening(object? sender, System.ComponentModel.CancelEventArgs e) => BuildMenu();
    private void IconMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left) App.ShowMainWindow();
    }
    private void IconDoubleClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left) App.ShowMainWindow();
    }
    private void TimerTick(object? sender, EventArgs e)
    {
        Update();
        if (++_ticks % 10 == 0) _vm.Lighting.Trace("tick");
    }
    private void WindowVisibilityChanged(object? sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_window.IsVisible) _trimTimer.Stop();
        else TrimSoon();
        if (_window.IsVisible || App.ExitRequested || _hintShown) return;
        _hintShown = true;
        _icon.ShowBalloonTip(4000, "SharkBabyDrivers", Loc.T("tray_hidden"), Forms.ToolTipIcon.Info);
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    private void Update()
    {
        if (_disposed) return;
        var lighting = _vm.Lighting;
        var status = lighting.Describe();
        var text = $"SharkBabyDrivers{(App.IsPreview ? " · " + Loc.T("tray_preview") : "")}\n{lighting.ConnectionText}\n{status}";
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
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawIcon(_baseIcon, new Rectangle(0, 0, 32, 32));
            using var ring = new SolidBrush(TrayMenuRenderer.Background);
            using var fill = new SolidBrush(color);
            g.FillEllipse(ring, 21, 21, 11, 11);
            g.FillEllipse(fill, 23, 23, 7, 7);
        }
        var h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        while (menu.Items.Count > 0)
        {
            var item = menu.Items[0];
            menu.Items.RemoveAt(0);
            item.Dispose();
        }
        menu.Items.Add(Loc.I.Language == "en" ? "Check for updates" : "Verificar atualizações", null, async (_, _) => await ((App)System.Windows.Application.Current).Updater.CheckAsync(manual: true));
        var lighting = _vm.Lighting;

        menu.Items.Add(new Forms.ToolStripLabel("SharkBabyDrivers", _brandImage)
        {
            Font = _titleFont,
            ForeColor = TrayMenuRenderer.Text,
            Padding = new Forms.Padding(8, 6, 14, 1),
            ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = Forms.TextImageRelation.ImageBeforeText,
        });
        var subtitle = (App.IsPreview ? Loc.T("tray_preview") + "  ·  " : "") + "v" + App.Version + "  ·  " + lighting.ConnectionText;
        menu.Items.Add(new Forms.ToolStripLabel(subtitle)
        {
            ForeColor = TrayMenuRenderer.Muted,
            Padding = new Forms.Padding(10, 0, 14, 3),
        });
        menu.Items.Add(new Forms.ToolStripLabel(lighting.Describe())
        {
            ForeColor = lighting.State is "signal" or "app" ? TrayMenuRenderer.Accent : TrayMenuRenderer.Muted,
            Padding = new Forms.Padding(10, 0, 14, 7),
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        AddSection(menu, Loc.T("tray_section_lighting"));

        var signal = new Forms.ToolStripMenuItem(Loc.T("tray_light_signal")) { Checked = lighting.SignalRgbOwns, Enabled = !App.IsPreview };
        signal.Click += (_, _) => SetOwner(true);
        var app = new Forms.ToolStripMenuItem(Loc.T("tray_light_app")) { Checked = !lighting.SignalRgbOwns, Enabled = !App.IsPreview };
        app.Click += (_, _) => SetOwner(false);
        menu.Items.Add(signal);
        menu.Items.Add(app);
        menu.Items.Add(new Forms.ToolStripSeparator());
        AddSection(menu, Loc.T("tray_section_app"));

        var autostart = new Forms.ToolStripMenuItem(Loc.T("tray_autostart")) { Checked = !App.IsPreview && Autostart.Enabled, Enabled = !App.IsPreview };
        autostart.Click += (_, _) =>
        {
            try { Autostart.Enabled = !Autostart.Enabled; }
            catch (Exception ex) { App.ReportError(ex); }
        };
        menu.Items.Add(autostart);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var open = new Forms.ToolStripMenuItem(Loc.T("tray_open"));
        open.Click += (_, _) => App.ShowMainWindow();
        menu.Items.Add(open);
        var minimize = new Forms.ToolStripMenuItem(Loc.T("tray_minimize")) { Enabled = _window.IsVisible };
        minimize.Click += (_, _) =>
        {
            if (_window is MainWindow main) main.MinimizeToTray();
            else _window.Hide();
        };
        menu.Items.Add(minimize);
        var exit = new Forms.ToolStripMenuItem(Loc.T("tray_exit"));
        exit.ForeColor = Color.FromArgb(255, 128, 137);
        exit.Click += (_, _) => App.RequestExit();
        menu.Items.Add(exit);

        foreach (Forms.ToolStripItem item in menu.Items)
            if (item is Forms.ToolStripMenuItem) item.Padding = new Forms.Padding(10, 7, 18, 7);
    }

    private void AddSection(Forms.ContextMenuStrip menu, string text) => menu.Items.Add(new Forms.ToolStripLabel(text)
    {
        Font = _sectionFont,
        ForeColor = TrayMenuRenderer.Muted,
        Padding = new Forms.Padding(10, 7, 14, 4),
    });

    private void SetOwner(bool signal)
    {
        if (App.IsPreview) return;
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
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= TimerTick;
        _trimTimer.Stop();
        _trimTimer.Tick -= TrimTick;
        _window.IsVisibleChanged -= WindowVisibilityChanged;
        _vm.Lighting.Changed -= Update;
        _icon.Visible = false;
        var menu = _icon.ContextMenuStrip;
        if (menu is not null) menu.Opening -= MenuOpening;
        _icon.MouseClick -= IconMouseClick;
        _icon.MouseDoubleClick -= IconDoubleClick;
        _icon.Dispose();
        menu?.Dispose();
        _drawn?.Dispose();
        _baseIcon.Dispose();
        _brandImage.Dispose();
        _menuFont.Dispose();
        _titleFont.Dispose();
        _sectionFont.Dispose();
    }
}

/// <summary>The tray keeps the same graphite and cherry-red palette as the WPF window.</summary>
internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    internal static readonly Color Background = Color.FromArgb(20, 22, 28);
    internal static readonly Color Text = Color.FromArgb(245, 246, 250);
    internal static readonly Color Muted = Color.FromArgb(151, 158, 177);
    internal static readonly Color Accent = Color.FromArgb(240, 68, 82);
    private static readonly Color Border = Color.FromArgb(48, 52, 64);
    private static readonly Color Selected = Color.FromArgb(53, 31, 40);

    internal TrayMenuRenderer() : base(new TrayColors()) { RoundedEdges = false; }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) => e.Graphics.Clear(Background);

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var bounds = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var brush = new SolidBrush(Selected);
        e.Graphics.FillRectangle(brush, bounds);
        using var accent = new SolidBrush(Accent);
        e.Graphics.FillRectangle(accent, 4, 6, 2, e.Item.Height - 12);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? e.Item.ForeColor : Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Border);
        e.Graphics.DrawLine(pen, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2);
    }

    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        var rectangle = e.ImageRectangle;
        var center = new Point(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Accent);
        e.Graphics.FillEllipse(fill, center.X - 7, center.Y - 7, 14, 14);
        using var check = new Pen(Color.White, 1.7f);
        e.Graphics.DrawLines(check, new Point[] { new(center.X - 3, center.Y), new(center.X - 1, center.Y + 2), new(center.X + 3, center.Y - 2) });
    }

    private sealed class TrayColors : Forms.ProfessionalColorTable
    {
        public override Color MenuBorder => Border;
        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuItemSelected => Selected;
        public override Color MenuItemBorder => Selected;
    }
}
