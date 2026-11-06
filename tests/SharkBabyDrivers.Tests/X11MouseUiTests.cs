using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using SharkBabyDrivers.Core.Devices.X11;
using SharkBabyDrivers.Core.Storage;
using SharkBabyDrivers.Infrastructure;
using SharkBabyDrivers.Localization;
using SharkBabyDrivers.ViewModels;
using SharkBabyDrivers.Views;
using static SharkBabyDrivers.Core.Devices.X11.X11Protocol;

namespace SharkBabyDrivers.Tests;

[CollectionDefinition("Mouse WPF", DisableParallelization = true)]
public sealed class MouseWpfCollection { }

/// <summary>Exercises the actual selectors, including new actions absent from the preset list.</summary>
[Collection("Mouse WPF")]
public sealed class X11MouseUiTests
{
    [Fact]
    public async Task Custom_keys_and_modifiers_stay_visible_after_editing_language_and_profile_load()
    {
        var phase = "starting STA thread";
        var phases = new ConcurrentQueue<string>();
        void Progress(string value) { Volatile.Write(ref phase, value); phases.Enqueue(value); }
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Exception? failure = null;
            try
            {
                // Never construct SharkBabyDrivers.App: its startup can enumerate real devices.
                Progress("constructing plain Application");
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Progress("loading theme resources");
                AddResources(app);
                app.Dispatcher.UnhandledException += (_, e) => { failure ??= e.Exception; e.Handled = true; app.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); };
                app.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { await ExerciseMouseEditor(Progress); }
                    catch (Exception ex) { failure = ex; }
                    finally { Progress("shutting down Application"); app.Shutdown(); app.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                }));
                Progress("starting dispatcher");
                Dispatcher.Run();
                Progress("dispatcher stopped");
            }
            catch (Exception ex) { failure = ex; }
            if (failure is null) completed.TrySetResult();
            else completed.TrySetException(failure);
        }) { IsBackground = true, Name = "X11 mouse UI regression" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completed.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"WPF regression timed out while {Volatile.Read(ref phase)}.\nCompleted stages:\n{string.Join("\n", phases)}", ex);
        }
    }

    private static async Task ExerciseMouseEditor(Action<string> progress)
    {
        var previousRoot = AppPaths.Root;
        var previousLanguage = Loc.I.Language;
        var temporaryParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SharkBabyDrivers-ui-tests"));
        var temporaryRoot = Path.Combine(temporaryParent, Guid.NewGuid().ToString("N"));
        Window? host = null;
        try
        {
            progress("preparing isolated mouse and view");
            AppPaths.Root = temporaryRoot;
            Loc.I.Language = "pt-BR";
            using var mouse = X11Device.CreatePreview();
            using var vm = new X11Vm(mouse);
            var view = new X11View { DataContext = vm };
            host = new Window { Content = view, Width = 1000, Height = 800, ShowActivated = false, ShowInTaskbar = false };
            progress("showing inactive host");
            host.Show();
            progress("initializing preview mouse");
            await vm.InitializeAsync();
            progress("settling initial bindings");
            await Settle(view);
            Assert.True(vm.IsReady);
            Assert.Contains(Descendants(view).OfType<Button>(), button => ReferenceEquals(button.Command, vm.ImportProfileCommand) && button.IsVisible);

            var control = ButtonAction.Key(KeyModifiers.Ctrl, 0);
            var shift = ButtonAction.Key(KeyModifiers.Shift, 0);
            await EditShortcut(vm, view, X11Button.Forward, control, "Ctrl", progress);
            await EditShortcut(vm, view, X11Button.Backward, shift, "Shift", progress);
            await AssertSelection(view, vm, X11Button.Forward, control, "Ctrl", progress);

            await EditShortcut(vm, view, X11Button.Forward, ButtonAction.Key(KeyModifiers.Ctrl | KeyModifiers.Shift, 0), "Ctrl + Shift", progress);
            await EditShortcut(vm, view, X11Button.Forward, ButtonAction.Key(KeyModifiers.None, 0x04), "A", progress);
            await EditShortcut(vm, view, X11Button.Forward, ButtonAction.Key(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x16), "Ctrl + Shift + S", progress);
            await EditShortcut(vm, view, X11Button.Forward, ButtonAction.Key(KeyModifiers.Ctrl, 0x04), "Ctrl + A", progress);
            await EditShortcut(vm, view, X11Button.Forward, control, "Ctrl", progress);

            // Saving/loading follows the real profile commands; no file picker or device write is involved.
            vm.NewProfileName = "Modifier editing";
            progress("saving new isolated profile");
            vm.SaveProfileCommand.Execute(null);
            progress("saved profile; restoring preset actions");
            var saved = Assert.Single(vm.SavedProfiles);
            Assert.Same(saved, vm.SelectedSavedProfile);
            Assert.True(saved.IsSelected);
            Assert.Equal(saved.Name, vm.NewProfileName);
            var originalSavedFile = File.ReadAllText(saved.Profile.Path);
            await ChoosePreset(view, vm, X11Button.Forward, "forward", progress);
            await ChoosePreset(view, vm, X11Button.Backward, "backward", progress);
            progress("loading saved modifier profile");
            vm.LoadProfileCommand.Execute(saved);
            progress("settling loaded profile");
            await Settle(view);
            await AssertSelection(view, vm, X11Button.Forward, control, "Ctrl", progress);
            await AssertSelection(view, vm, X11Button.Backward, shift, "Shift", progress);
            Assert.Equal(saved.Name, vm.SelectedSavedProfile?.Name);
            Assert.Equal(saved.Name, vm.NewProfileName);

            progress("editing and saving a named copy from the panel");
            vm.Stages[0].Dpi = 1200;
            vm.NewProfileName = "Modifier editing copy";
            vm.SaveProfileCommand.Execute(null);
            Assert.Equal(2, vm.SavedProfiles.Count);
            Assert.Equal("Modifier editing copy", vm.SelectedSavedProfile?.Name);
            Assert.Equal("Modifier editing copy", vm.NewProfileName);
            Assert.Equal(originalSavedFile, File.ReadAllText(saved.Profile.Path));
            Assert.Equal(1200, vm.Working.GetStageDpi(0));

            progress("starting a new profile copy without resetting editor settings");
            var beforeNewDpi = vm.Working.DpiFrame();
            vm.NewProfileCommand.Execute(null);
            Assert.Null(vm.SelectedSavedProfile);
            Assert.False(vm.HasSelectedSavedProfile);
            Assert.Equal("", vm.NewProfileName);
            Assert.Equal(beforeNewDpi, vm.Working.DpiFrame());
            Assert.Equal(control, vm.Profile.Buttons[(int)X11Button.Forward].ToAction());
            Assert.Equal(shift, vm.Profile.Buttons[(int)X11Button.Backward].ToAction());
            vm.NewProfileName = "New modifier copy";
            vm.SaveProfileCommand.Execute(null);
            Assert.Equal(3, vm.SavedProfiles.Count);
            Assert.Equal("New modifier copy", vm.SelectedSavedProfile?.Name);

            progress("reloading the original profile after creating copies");
            vm.LoadProfileCommand.Execute(vm.SavedProfiles.Single(item => item.Name == saved.Name));
            await Settle(view);
            Assert.Equal(800, vm.Working.GetStageDpi(0));
            await AssertSelection(view, vm, X11Button.Forward, control, "Ctrl", progress);
            await AssertSelection(view, vm, X11Button.Backward, shift, "Shift", progress);

            progress("switching to English");
            Loc.I.Language = "en";
            await Settle(view);
            await AssertSelection(view, vm, X11Button.Forward, control, "Ctrl", progress);
            await AssertSelection(view, vm, X11Button.Backward, shift, "Shift", progress);
            progress("switching to Portuguese");
            Loc.I.Language = "pt-BR";
            await Settle(view);
            await AssertSelection(view, vm, X11Button.Forward, control, "Ctrl", progress);

            progress("discarding custom actions");
            vm.DiscardCommand.Execute(null);
            progress("settling discarded actions");
            await Settle(view);
            Assert.Equal(X11Actions.Forward, vm.Profile.Buttons[(int)X11Button.Forward].ToAction());
            Assert.Equal(X11Actions.Backward, vm.Profile.Buttons[(int)X11Button.Backward].ToAction());
            await AssertSelection(view, vm, X11Button.Forward, X11Actions.Forward, "Avançar", progress);
            await AssertSelection(view, vm, X11Button.Backward, X11Actions.Backward, "Voltar", progress);
            Assert.Null(vm.SelectedSavedProfile);
            Assert.Equal("", vm.NewProfileName);
            Assert.False(vm.IsDirty);
            Assert.False(vm.Profile.ButtonsWritten);
            Assert.Equal(800, mouse.ReadConfig().GetStageDpi(0));
            progress("all editor assertions passed");

            await ExerciseDpiEvents(vm, mouse, saved, progress);
            await ExerciseProfileRecovery(host, progress);
        }
        finally
        {
            progress("closing inactive host");
            host?.Close();
            progress("restoring language and data root");
            Loc.I.Language = previousLanguage;
            AppPaths.Root = previousRoot;
            var resolved = Path.GetFullPath(temporaryRoot);
            if (resolved.StartsWith(temporaryParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
            progress("isolated data cleaned");
        }
    }

    private static async Task ExerciseDpiEvents(X11Vm vm, X11Device mouse, SavedProfileVm saved, Action<string> progress)
    {
        progress("checking physical DPI events preserve a loaded draft");
        vm.LoadProfileCommand.Execute(saved);
        var targetStage = vm.Working.ActiveStage;
        var physical = mouse.ReadConfig();
        physical.ActiveStage = 2;
        mouse.WriteConfig(physical, null);
        Assert.True(vm.IsDirty);
        Assert.Equal(targetStage, vm.Working.ActiveStage);

        var busyEventSent = false;
        var checkingBusyEvent = false;
        var busyStageRefreshes = 0;
        foreach (var stage in vm.Stages)
            stage.PropertyChanged += (_, _) => { if (checkingBusyEvent) busyStageRefreshes++; };
        System.ComponentModel.PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName != nameof(vm.Busy) || !vm.Busy || busyEventSent) return;
            busyEventSent = true;
            checkingBusyEvent = true;
            try
            {
                physical.ActiveStage = 3;
                mouse.WriteConfig(physical, null);
            }
            finally { checkingBusyEvent = false; }
        };
        vm.PropertyChanged += handler;
        try { await ApplyVm(vm); }
        finally { vm.PropertyChanged -= handler; }
        Assert.True(busyEventSent);
        Assert.Equal(0, busyStageRefreshes);
        Assert.Equal(targetStage, vm.Working.ActiveStage);

        progress("following physical DPI changes while an unrelated lighting edit is pending");
        vm.LightColor = "#123456";
        physical = mouse.ReadConfig();
        physical.ActiveStage = 2;
        mouse.WriteConfig(physical, null);
        Assert.True(vm.IsDirty);
        Assert.Equal(2, vm.Working.ActiveStage);

        progress("preserving an explicitly selected DPI stage until it is applied");
        vm.Stages[0].IsActive = true;
        physical.ActiveStage = 3;
        mouse.WriteConfig(physical, null);
        Assert.True(vm.IsDirty);
        Assert.Equal(1, vm.Working.ActiveStage);
    }

    private static async Task ExerciseProfileRecovery(Window host, Action<string> progress)
    {
        progress("preparing isolated fault-capable mouse editor");
        var transport = new ScriptedX11Transport();
        using var mouse = new X11Device(transport, "x11-ui-recovery") { Settle = TimeSpan.Zero, WakeTimeout = TimeSpan.Zero };
        using var vm = new X11Vm(mouse);
        var view = new X11View { DataContext = vm };
        host.Content = view;
        await vm.InitializeAsync();
        await Settle(view);
        var tabs = Assert.Single(Descendants(view).OfType<TabControl>());
        Assert.True(tabs.IsEnabled);
        var defaults = transport.Config;
        vm.NewProfileName = "Recovery defaults";
        vm.SaveProfileCommand.Execute(null);
        var defaultProfile = vm.SavedProfiles.Single(item => item.Name == "Recovery defaults");

        vm.Stages[0].Dpi = 1200;
        vm.LightMode = X11LightMode.Static;
        vm.LightColor = "#AB12CD";
        vm.PollingHz = 500;
        vm.Assign(X11Button.Forward, vm.ActionOptions.Single(option => option.Id == "copy"));
        vm.NewMacroCommand.Execute(null);
        var macro = vm.SelectedMacro!;
        macro.Add(0x04, true, 20);
        macro.Add(0x04, false, 20);
        vm.Assign(X11Button.Backward, vm.ActionOptions.Single(option => option.MacroName == macro.Name));
        vm.NewProfileName = "Recovery target";
        vm.SaveProfileCommand.Execute(null);
        var saved = vm.SavedProfiles.Single(item => item.Name == "Recovery target");
        var namedProfileFile = File.ReadAllText(saved.Profile.Path);
        var target = vm.Working.Clone();
        await ApplyVm(vm);
        Assert.False(vm.IsDirty);
        Assert.True(vm.Profile.ButtonsWritten);

        progress("restoring only the side-button mappings changed by another mouse application");
        var customButtons = X11Buttons.BuildReport(vm.Profile.Buttons.Select(button => button.ToAction()).ToList());
        transport.SetFeature(X11Buttons.BuildReport(X11Buttons.FactoryDefaults()));
        Assert.True(target.SameAs(transport.Config));
        transport.Sent.Clear();
        vm.LoadProfileCommand.Execute(saved);
        await ApplyVm(vm);
        Assert.Equal(customButtons, transport.Buttons);
        Assert.True(target.SameAs(transport.Config));
        AssertConfigMacroButtonOrder(transport);
        Assert.Equal(namedProfileFile, File.ReadAllText(saved.Profile.Path));

        progress("restoring known side-button mappings when applying an unrelated lighting edit");
        transport.SetFeature(X11Buttons.BuildReport(X11Buttons.FactoryDefaults()));
        Assert.True(target.SameAs(transport.Config));
        vm.LightColor = "#CD34AB";
        var lightingEdit = vm.Working.Clone();
        transport.Sent.Clear();
        await ApplyVm(vm);
        Assert.Equal(customButtons, transport.Buttons);
        Assert.True(lightingEdit.SameAs(transport.Config));
        AssertConfigMacroButtonOrder(transport, RidDpi, RidLight);

        progress("restoring the same named profile after physical configuration loss");
        var drifted = defaults.Clone();
        drifted.SetStageDpi(0, 3200);
        drifted.PollingHz = 125;
        transport.ReplaceConfig(drifted);
        transport.SetFeature(X11Buttons.BuildReport(X11Buttons.FactoryDefaults()));
        transport.MacroPages.Clear();
        transport.Sent.Clear();
        vm.LoadProfileCommand.Execute(saved);
        await ApplyVm(vm);
        Assert.True(target.SameAs(transport.Config));
        Assert.Equal(X11Buttons.BuildReport(vm.Profile.Buttons.Select(button => button.ToAction()).ToList()), transport.Buttons);
        AssertConfigMacroButtonOrder(transport);
        Assert.Equal(3, transport.MacroPages.Count);
        Assert.Equal(namedProfileFile, File.ReadAllText(saved.Profile.Path));

        progress("applying a normal edit against actual state instead of its stale baseline");
        transport.ReplaceConfig(drifted);
        vm.AngleSnap = !vm.AngleSnap;
        var edited = vm.Working.Clone();
        transport.Sent.Clear();
        await ApplyVm(vm);
        Assert.True(edited.SameAs(transport.Config));
        AssertConfigMacroButtonOrder(transport);

        progress("reapplying default button mappings even when the named profile is unchanged");
        vm.LoadProfileCommand.Execute(defaultProfile);
        await ApplyVm(vm);
        transport.Sent.Clear();
        vm.LoadProfileCommand.Execute(defaultProfile);
        await ApplyVm(vm);
        Assert.True(defaults.SameAs(transport.Config));
        Assert.Equal(X11Buttons.BuildReport(X11Buttons.FactoryDefaults()), transport.Buttons);
        Assert.Equal(new byte[] { RidDpi, RidLight, RidPoll, RidButtons }, transport.Writes.Select(report => report[0]));

        progress("discarding a loaded profile cancels its pending full restoration");
        vm.LoadProfileCommand.Execute(saved);
        vm.DiscardCommand.Execute(null);
        vm.PollingHz = 500;
        transport.Sent.Clear();
        await ApplyVm(vm);
        Assert.Equal(new byte[] { RidPoll, RidButtons }, transport.Writes.Select(report => report[0]));

        progress("keeping a sleeping mouse's pending restoration available for retry");
        vm.LoadProfileCommand.Execute(saved);
        var persistedBeforeFailure = PersistedProfile(mouse);
        var waitingEvents = 0;
        mouse.WaitingForWake += () => Interlocked.Increment(ref waitingEvents);
        mouse.WakeTimeout = TimeSpan.FromMilliseconds(20);
        transport.Awake = false;
        transport.Sent.Clear();
        await Assert.ThrowsAsync<MouseAsleepException>(() => ApplyVm(vm));
        await Settle(view);
        Assert.True(waitingEvents > 0);
        Assert.True(vm.IsReady);
        Assert.True(vm.IsDirty);
        Assert.False(vm.Busy);
        Assert.Empty(transport.Writes);
        Assert.Equal(persistedBeforeFailure, PersistedProfile(mouse));
        Assert.Equal(namedProfileFile, File.ReadAllText(saved.Profile.Path));
        transport.Awake = true;
        mouse.WakeTimeout = TimeSpan.Zero;
        transport.Sent.Clear();
        await ApplyVm(vm);
        AssertConfigMacroButtonOrder(transport);
        Assert.True(target.SameAs(transport.Config));
        Assert.False(vm.IsDirty);

        progress("rejecting a final readback mismatch without replacing the previous applied profile");
        vm.LoadProfileCommand.Execute(defaultProfile);
        persistedBeforeFailure = PersistedProfile(mouse);
        transport.AcceptWrite = report =>
        {
            if (report[0] == RidButtons)
            {
                var lost = transport.Config;
                lost.PollingHz = 125;
                transport.ReplaceConfig(lost);
            }
            return true;
        };
        await Assert.ThrowsAsync<IOException>(() => ApplyVm(vm));
        Assert.True(vm.IsDirty);
        Assert.True(vm.IsReady);
        Assert.False(vm.Busy);
        Assert.Equal(persistedBeforeFailure, PersistedProfile(mouse));
        Assert.Equal(namedProfileFile, File.ReadAllText(saved.Profile.Path));
        transport.AcceptWrite = null;
        transport.Sent.Clear();
        await ApplyVm(vm);
        Assert.True(defaults.SameAs(transport.Config));
        Assert.Equal(new byte[] { RidDpi, RidLight, RidPoll, RidButtons }, transport.Writes.Select(report => report[0]));

        progress("disabling all mouse tabs while a restoration is being written");
        vm.LoadProfileCommand.Execute(saved);
        var writeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.AcceptWrite = report =>
        {
            if (report[0] == RidDpi && writeEntered.TrySetResult()) releaseWrite.Task.GetAwaiter().GetResult();
            return true;
        };
        var applying = ApplyVm(vm);
        try
        {
            await writeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Settle(view);
            Assert.True(vm.Busy);
            Assert.False(vm.CanWrite);
            Assert.False(tabs.IsEnabled);
        }
        finally { releaseWrite.TrySetResult(); }
        await applying;
        transport.AcceptWrite = null;
        await Settle(view);
        Assert.False(vm.Busy);
        Assert.True(tabs.IsEnabled);
        Assert.True(target.SameAs(transport.Config));
        progress("all mouse recovery assertions passed");
    }

    private static Task ApplyVm(X11Vm vm) =>
        (Task)typeof(DeviceVm).GetMethod("ApplyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null)!;

    private static string PersistedProfile(X11Device mouse) =>
        JsonSerializer.Serialize(X11AppProfile.Load(mouse.DeviceKey), JsonStore.Options);

    private static void AssertConfigMacroButtonOrder(ScriptedX11Transport transport, params byte[] configReports)
    {
        if (configReports.Length == 0) configReports = [RidDpi, RidLight, RidPoll];
        var ids = transport.Writes.Select(report => report[0]).ToArray();
        Assert.Equal(configReports, ids[..configReports.Length]);
        Assert.All(ids[configReports.Length..^1], rid => Assert.Equal(RidMacro, rid));
        Assert.Contains(RidMacro, ids);
        Assert.Equal(RidButtons, ids[^1]);
    }

    private static async Task EditShortcut(X11Vm vm, X11View view, X11Button button, ButtonAction action, string text, Action<string> progress)
    {
        progress($"assigning {button}: {text}");
        vm.CaptureShortcut = _ => action;
        vm.EditShortcut(button);
        progress($"settling {button}: {text}");
        await Settle(view);
        await AssertSelection(view, vm, button, action, text, progress);
        progress($"verified {button}: {text}");
    }

    private static async Task AssertSelection(X11View view, X11Vm vm, X11Button button, ButtonAction action, string text, Action<string> progress)
    {
        var buttonVm = vm.Buttons.Single(b => b.Button == button);
        var selector = Descendants(view).OfType<ComboBox>().Single(c => ReferenceEquals(c.DataContext, buttonVm));
        Assert.Equal(action, vm.Profile.Buttons[(int)button].ToAction());
        Assert.Equal(text, buttonVm.AssignmentText);
        // The closed picker renders the model's assignment directly. Its selection is only a choice draft.
        Assert.Contains(Descendants(selector).OfType<TextBlock>(), block => block.IsVisible && block.Text.Contains(text, StringComparison.Ordinal));
        var dirtyBeforeOpening = vm.IsDirty;
        progress($"opening {button} action menu for {text}");
        selector.IsDropDownOpen = true;
        await Settle(view);
        Assert.Equal(buttonVm.SelectedActionId, selector.SelectedValue);
        Assert.IsType<ActionOption>(selector.SelectedItem);
        progress($"closing {button} action menu without changing selection");
        selector.IsDropDownOpen = false;
        await Settle(view);
        Assert.Equal(action, vm.Profile.Buttons[(int)button].ToAction());
        Assert.Equal(dirtyBeforeOpening, vm.IsDirty);
    }

    private static async Task ChoosePreset(X11View view, X11Vm vm, X11Button button, string presetId, Action<string> progress)
    {
        var buttonVm = vm.Buttons.Single(b => b.Button == button);
        var selector = Descendants(view).OfType<ComboBox>().Single(c => ReferenceEquals(c.DataContext, buttonVm));
        progress($"opening {button} action menu to choose {presetId}");
        selector.IsDropDownOpen = true;
        await Settle(view);
        var option = vm.ActionOptions.Single(item => item.Id == presetId);
        selector.SetCurrentValue(ComboBox.SelectedItemProperty, option);
        progress($"committing {button} menu choice {presetId}");
        selector.IsDropDownOpen = false;
        await Settle(view);
        Assert.Equal(option.Action, vm.Profile.Buttons[(int)button].ToAction());
    }

    private static async Task Settle(FrameworkElement view)
    {
        await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        view.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void AddResources(Application app)
    {
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SharkBabyDrivers;component/Themes/Dark.xaml") });
        app.Resources.Add("Eq", new EqualsConverter());
        app.Resources.Add("Not", new NotConverter());
        app.Resources.Add("VisIf", new VisibleIfConverter());
        app.Resources.Add("VisIfNot", new VisibleIfConverter { Invert = true });
        app.Resources.Add("HexBrush", new HexToBrushConverter());
        app.Resources.Add("Fmt", new FormatConverter());
    }
}
