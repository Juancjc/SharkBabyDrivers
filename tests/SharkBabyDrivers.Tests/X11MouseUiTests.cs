using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Concurrent;
using SharkBabyDrivers.Core.Devices.X11;
using SharkBabyDrivers.Core.Storage;
using SharkBabyDrivers.Infrastructure;
using SharkBabyDrivers.Localization;
using SharkBabyDrivers.ViewModels;
using SharkBabyDrivers.Views;

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
