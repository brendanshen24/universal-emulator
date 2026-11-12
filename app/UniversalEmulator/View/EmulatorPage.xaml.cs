using GameController;
using GamePad.Logic;
using MBox.Model;
using MBox.ViewModel;
using MBoxPanels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using Windows.UI.Core;

namespace MBox.View
{
    public sealed class BoolToGamepadEditingStatusConverter : IValueConverter
    {
        public PadStatus TrueValue { get; set; } = PadStatus.Editing;
        public PadStatus FalseValue { get; set; } = PadStatus.Playing;

        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is true ? TrueValue : FalseValue;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            value is PadStatus status && Equals(status, TrueValue);
    }

    // Preserves the exact XAML typo "EmulatorToGamePadeModeConverter".
    public sealed class EmulatorToGamePadeModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is EmulatorType emu)
            {
                return emu switch
                {
                    EmulatorType.Snes => PadMode.Snes,
                    EmulatorType.Sega => PadMode.Sega,
                    EmulatorType.Gba => PadMode.Gba,
                    _ => PadMode.Nes,
                };
            }
            return PadMode.Nes;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }

    public sealed partial class EmulatorPage : CommonPage
    {
        public sealed record Launch(Rom Rom, bool LoadSave);

        private readonly Controller _controller = new(0);
        private bool _eventsHooked;
        private DispatcherTimer? _controllerMenuTimer;
        private bool _prevMenuDown;

        public EmulatorPage()
        {
            InitializeComponent();
        }

        public EmulatorPageViewModel ViewModel => (EmulatorPageViewModel)DataContext;

#if DEBUG
        [System.Runtime.InteropServices.DllImport("user32", ExactSpelling = true)]
        private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);

        internal static async Task RunSmokeTestAsync(Frame frame, string log)
        {
            void Report(string message) => File.AppendAllText(log, message + Environment.NewLine);
            try
            {
                await Task.Delay(250);
                if (OperatingSystem.IsWindows() && App.MainWindow is { } window)
                {
                    const uint WmGetIcon = 0x7f;
                    if (Uno.UI.Xaml.WindowHelper.GetNativeWindow(window) is not Uno.UI.NativeElementHosting.Win32NativeWindow native ||
                        SendMessageW(native.Hwnd, WmGetIcon, 0, 0) == 0 ||
                        SendMessageW(native.Hwnd, WmGetIcon, 1, 0) == 0)
                        throw new InvalidOperationException("The desktop host did not apply the package logo to its window.");
                    Report("Window has small and large icons.");
                }
                if (frame.Content is not StartPage start ||
                    start.FindName("HamburgerElement") is not Microsoft.Toolkit.Uwp.UI.Controls.HamburgerMenu { TemplateItemCount: 4 })
                    throw new InvalidOperationException("The original hamburger template did not bind all four menu items.");

                var library = App.Current.Library;
                var games = library.AllRoms.Where(rom =>
                    rom.Name is "Chase" or "Airwolf 92 (Public domain)" or "Top Gear (USA)").ToArray();
                if (games.Length < 2)
                    throw new InvalidOperationException("Could not find the NES and SNES smoke-test games.");

                foreach (var game in games)
                {
                    string romName = game.Path!.Split('/').Last();
                    Report($"Opening {romName}");
                    frame.Navigate(typeof(EmulatorPage));
                    var page = (EmulatorPage)frame.Content;
                    var panel = page.EmulatorPanel;
                    if (panel.UsesOriginal && !page._controller.UsesOriginal)
                        throw new InvalidOperationException("The original game controller could not be loaded.");
                    var pad = page._controller.GetState();
                    Report($"Original controller={page._controller.UsesOriginal}, connected={pad.connected}");
                    panel.LoadRom(romName, await library.ReadRomAsync(game));
                    await Task.Delay(1200);
                    Report($"Original={panel.UsesOriginal} State={panel.State} Type={panel.LoadedEmulator}");
                    if (panel.State != EmulatorStateType.RomLoaded)
                        throw new InvalidOperationException("ROM failed to load.");
                    panel.SetControl(ControlKey.Start, true);
                    await Task.Delay(100);
                    panel.SetControl(ControlKey.Start, false);
                    byte[] save = panel.SaveState();
                    using var png = new MemoryStream();
                    bool visibleFrame = false;
                    for (int attempt = 0; attempt < 12 && !visibleFrame; attempt++)
                    {
                        png.SetLength(0);
                        await panel.SaveSnapshotToStream(png);
                        using var snapshot = SkiaSharp.SKBitmap.Decode(png.ToArray());
                        visibleFrame = snapshot != null && snapshot.Pixels.Any(pixel =>
                            pixel.Red != 0 || pixel.Green != 0 || pixel.Blue != 0);
                        if (!visibleFrame)
                            await Task.Delay(500);
                    }
                    Report($"State bytes={save.Length}, PNG bytes={png.Length}");
                    if (save.Length == 0 || !visibleFrame)
                        throw new InvalidOperationException("Save was empty or the game never rendered a visible frame.");
                    panel.LoadState(save);
                    page.ShowBar();
                    if (page.PageAppBar.Visibility != Visibility.Visible || panel.IsControllerInputEnabled)
                        throw new InvalidOperationException("Opening the command bar did not switch input modes.");
                    await Task.Delay(250);
                    page.HideBar();
                    if (page.PageAppBar.Visibility != Visibility.Collapsed || !panel.IsControllerInputEnabled)
                        throw new InvalidOperationException("Closing the command bar did not restore gameplay input.");
                    frame.GoBack();
                    await Task.Delay(300);
                    if (frame.Content is not StartPage)
                        throw new InvalidOperationException("Returning to library failed.");
                }
                Report("PASS");
            }
            catch (Exception e)
            {
                Report(e.ToString());
            }
            finally
            {
                App.MainWindow?.Close();
            }
        }
#endif

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            WireUiEvents();

            ViewModel.AttachPanel(EmulatorPanel);
            ViewModel.FullScreenChanged += ViewModel_FullScreenChanged;
            EmulatorPanel.AddController(_controller);
            EmulatorPanel.StateChanged += Emulator_StateChanged;

            if (ViewModel.IsFullScreenEnabled)
                ApplyFullScreen(true);
            else
                ApplyFullScreen(false);

            UpdateSoundIcon();
            UpdateFullScreenIcon();

            Launch? launch = e.Parameter as Launch;
            if (launch == null && e.Parameter is Rom romParam)
                launch = new Launch(romParam, false);

            if (launch != null)
            {
                bool loaded = await ViewModel.LoadRomAsync(launch.Rom, launch.LoadSave);
                if (!loaded)
                {
                    NavigateBack();
                    return;
                }
            }

            StartControllerMenuMonitor();
            Focus(FocusState.Programmatic);
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            StopControllerMenuMonitor();
            ViewModel.FullScreenChanged -= ViewModel_FullScreenChanged;
            EmulatorPanel.StateChanged -= Emulator_StateChanged;

            EmulatorPanel.UnloadRom();

            if (ViewModel.IsFullScreenEnabled)
                ApplyFullScreen(false);

            base.OnNavigatingFrom(e);
        }

        private void WireUiEvents()
        {
            if (_eventsHooked)
                return;
            _eventsHooked = true;

            KeyDown += EmulatorPage_KeyDown;
            KeyUp += EmulatorPage_KeyUp;

            GamePad.ControlsChanged += (_, mask) =>
            {
                if (ViewModel.ProcessGameController && !ViewModel.IsSetupGamepadMode)
                    EmulatorPanel.SetControls(mask);
            };
            GamePad.ExitSetupRequested += (_, _) => ViewModel.IsSetupGamepadMode = false;

            if (FindName("Back") is AppBarButton backBtn)
                backBtn.Click += async (_, _) => await NavigateBackWithSaveAsync();
            if (FindName("Save") is AppBarButton saveBtn)
                saveBtn.Click += async (_, _) => await SaveWithAnimationAsync();
            if (FindName("Load") is AppBarButton loadBtn)
                loadBtn.Click += async (_, _) =>
                {
                    await ViewModel.LoadStateAsync();
                    HideBar();
                };
            if (FindName("SetupGamePad") is AppBarToggleButton setupBtn)
                setupBtn.Click += (_, _) => HideBar();
            if (FindName("Settings") is AppBarButton settingsBtn)
                settingsBtn.Click += (_, _) =>
                {
                    SettingsView.IsPaneOpen = true;
                    HideBar();
                };

            ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(EmulatorPageViewModel.IsSoundEnabled))
                    UpdateSoundIcon();
                else if (args.PropertyName == nameof(EmulatorPageViewModel.IsFullScreenEnabled))
                    UpdateFullScreenIcon();
            };
        }

        private void Emulator_StateChanged(object? sender, EmulatorStateType state)
        {
            // Keep UI state synced when panel transitions between loaded/unloaded/error.
        }

        private void ViewModel_FullScreenChanged(object? sender, bool isFullScreen)
        {
            ApplyFullScreen(isFullScreen);
            UpdateFullScreenIcon();
        }

        private void ApplyFullScreen(bool enable)
        {
            try
            {
                var appWindow = App.MainWindow?.AppWindow;
                if (appWindow != null)
                {
                    appWindow.SetPresenter(enable
                        ? AppWindowPresenterKind.FullScreen
                        : AppWindowPresenterKind.Default);
                }
            }
            catch
            {
                // Window presenter switches might not be supported on every platform host.
            }
        }

        private void UpdateSoundIcon()
        {
            if (FindName("Sound") is AppBarToggleButton btn)
            {
                var symbol = ViewModel.IsSoundEnabled ? Symbol.Volume : Symbol.Mute;
                if (btn.Icon is SymbolIcon sym)
                    sym.Symbol = symbol;
                else
                    btn.Icon = new SymbolIcon(symbol);
            }
        }

        private void UpdateFullScreenIcon()
        {
            if (FindName("FullScreen") is AppBarToggleButton btn)
            {
                var glyph = ViewModel.IsFullScreenEnabled ? "\uE1D9" : "\uE1D8";
                if (btn.Icon is FontIcon fi)
                    fi.Glyph = glyph;
                else
                    btn.Icon = new FontIcon { Glyph = glyph };
            }
        }

        private void ShowBar()
        {
            if (FindName("PageAppBar") is FrameworkElement bar)
            {
                bar.Visibility = Visibility.Visible;
                ViewModel.ProcessGameController = false;
                EmulatorPanel.IsControllerInputEnabled = false;
                EmulatorPanel.SetControls(0);
                if (FindName("Back") is Control firstBtn)
                    firstBtn.Focus(FocusState.Programmatic);
            }
        }

        private void HideBar()
        {
            if (FindName("PageAppBar") is FrameworkElement bar)
            {
                bar.Visibility = Visibility.Collapsed;
                ViewModel.ProcessGameController = true;
                EmulatorPanel.IsControllerInputEnabled = true;
                Focus(FocusState.Programmatic);
            }
        }

        private async Task SaveWithAnimationAsync()
        {
            if (await ViewModel.SaveStateAsync())
            {
                HideBar();
                if (Resources.TryGetValue("SaveAnimation", out var obj) && obj is Storyboard sb)
                    sb.Begin();
            }
        }

        private async Task NavigateBackWithSaveAsync()
        {
            await ViewModel.EnsureThumbnailAsync();
            NavigateBack();
        }

        private void NavigateBack()
        {
            if (Frame?.CanGoBack == true)
                Frame.GoBack();
        }

        private void EmulatorPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var key = e.Key;

            bool altDown = false;
            try
            {
                var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
                altDown = (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
            }
            catch
            {
                // Ignore if keyboard source query isn't implemented on current host.
            }

            if (altDown && key == VirtualKey.Enter)
            {
                ViewModel.IsFullScreenEnabled = !ViewModel.IsFullScreenEnabled;
                e.Handled = true;
                return;
            }

            var bar = FindName("PageAppBar") as FrameworkElement;
            bool isBarCollapsed = bar == null || bar.Visibility == Visibility.Collapsed;

            if (isBarCollapsed)
            {
                if (key == VirtualKey.F2)
                {
                    _ = SaveWithAnimationAsync();
                    e.Handled = true;
                    return;
                }
                if (key == VirtualKey.F3)
                {
                    _ = ViewModel.LoadStateAsync();
                    e.Handled = true;
                    return;
                }
                if (key == VirtualKey.Back || key == VirtualKey.Escape ||
                    key == VirtualKey.GamepadMenu || key == VirtualKey.GamepadView ||
                    key == VirtualKey.GoBack)
                {
                    if (ViewModel.IsSetupGamepadMode)
                        ViewModel.IsSetupGamepadMode = false;
                    else
                        ShowBar();
                    e.Handled = true;
                    return;
                }

                if (ViewModel.ProcessGameController && !ViewModel.IsSetupGamepadMode)
                {
                    foreach (var ck in AppSettings.GetBoundControlKeys(key))
                        EmulatorPanel.SetControl(ck, true);
                    e.Handled = true;
                }
            }
            else
            {
                if (key == VirtualKey.Back || key == VirtualKey.GamepadB || key == VirtualKey.GoBack)
                {
                    _ = NavigateBackWithSaveAsync();
                    e.Handled = true;
                    return;
                }
                if (key == VirtualKey.Escape || key == VirtualKey.GamepadMenu || key == VirtualKey.GamepadView)
                {
                    HideBar();
                    e.Handled = true;
                    return;
                }
            }
        }

        private void EmulatorPage_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            foreach (var ck in AppSettings.GetBoundControlKeys(e.Key))
                EmulatorPanel.SetControl(ck, false);
        }

        private void StartControllerMenuMonitor()
        {
            _controllerMenuTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _controllerMenuTimer.Tick += (_, _) =>
            {
                var st = _controller.GetState();
                bool menuDown = st.connected && (st.start && st.back || st.back);
                if (menuDown && !_prevMenuDown)
                {
                    var bar = FindName("PageAppBar") as FrameworkElement;
                    if (bar == null || bar.Visibility == Visibility.Collapsed)
                        ShowBar();
                    else
                        HideBar();
                }
                _prevMenuDown = menuDown;
            };
            _controllerMenuTimer.Start();
        }

        private void StopControllerMenuMonitor()
        {
            _controllerMenuTimer?.Stop();
            _controllerMenuTimer = null;
        }

        private void Settings_CloseClicked(object? sender, EventArgs e) =>
            SettingsView.IsPaneOpen = false;
    }
}
