using System.Numerics;
using GameController;
using MBox.Model;
using MBox.ViewModel;
using MBoxPanels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MBox.View.SettingFlyouts
{
    public sealed partial class BoxSettingsFlyout : UserControl
    {
        public event EventHandler? CloseClicked;

        private readonly Controller _controller = new(0);
        private DispatcherTimer? _padPollTimer;
        private ControlKey? _focusedGamepadControl;
        private TextBox? _focusedGamepadBox;

        private readonly (string ElementName, ControlKey Key)[] _keyboardBoxes =
        {
            ("UpTextBox", ControlKey.Up),
            ("DownTextBox", ControlKey.Down),
            ("LeftTextBox", ControlKey.Left),
            ("RightTextBox", ControlKey.Right),
            ("ATextBox", ControlKey.A),
            ("BTextBox", ControlKey.B),
            ("CTextBox", ControlKey.C),
            ("XTextBox", ControlKey.X),
            ("YTextBox", ControlKey.Y),
            ("ZTextBox", ControlKey.Z),
            ("LTextBox", ControlKey.L),
            ("RTextBox", ControlKey.R),
            ("StartTextBox", ControlKey.Start),
            ("SelectTextBox", ControlKey.Select),
        };

        private readonly (string ElementName, ControlKey Key)[] _gamepadBoxes =
        {
            ("GamepadATextBox", ControlKey.A),
            ("GamepadBTextBox", ControlKey.B),
            ("GamepadCTextBox", ControlKey.C),
            ("GamepadXTextBox", ControlKey.X),
            ("GamepadYTextBox", ControlKey.Y),
            ("GamepadZTextBox", ControlKey.Z),
            ("GamepadLTextBox", ControlKey.L),
            ("GamepadRTextBox", ControlKey.R),
            ("GamepadStartTextBox", ControlKey.Start),
            ("GamepadSelectTextBox", ControlKey.Select),
        };

        private bool _wired;

        public BoxSettingsFlyout()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public BoxSettingsFlyoutViewModel ViewModel => (BoxSettingsFlyoutViewModel)DataContext;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            WireControls();
            RefreshBindingLabels();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopGamepadPolling();
        }

        private void WireControls()
        {
            if (_wired)
                return;
            _wired = true;

            foreach (var (name, key) in _keyboardBoxes)
            {
                if (FindName(name) is TextBox tb)
                {
                    tb.IsReadOnly = true;
                    tb.PreviewKeyDown += (_, args) => OnKeyboardBoxKeyDown(tb, key, args);
                    tb.KeyDown += (_, args) => OnKeyboardBoxKeyDown(tb, key, args);
                }
            }

            foreach (var (name, key) in _gamepadBoxes)
            {
                if (FindName(name) is TextBox tb)
                {
                    tb.IsReadOnly = true;
                    tb.GotFocus += (_, _) =>
                    {
                        _focusedGamepadControl = key;
                        _focusedGamepadBox = tb;
                        StartGamepadPolling();
                    };
                    tb.LostFocus += (_, _) =>
                    {
                        if (ReferenceEquals(_focusedGamepadBox, tb))
                        {
                            _focusedGamepadControl = null;
                            _focusedGamepadBox = null;
                            StopGamepadPolling();
                        }
                    };
                }
            }

            if (FindName("ResetToDefaults") is Button resetBtn)
            {
                resetBtn.Click += (_, _) =>
                {
                    AppSettings.ResetBindingsToDefaults();
                    RefreshBindingLabels();
                };
            }

            if (FindName("CloseButton") is Button closeBtn)
            {
                closeBtn.Click += (_, _) => CloseClicked?.Invoke(this, EventArgs.Empty);
            }
        }

        private static void OnKeyboardBoxKeyDown(TextBox tb, ControlKey control, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Tab || e.Key == VirtualKey.Shift ||
                e.Key == VirtualKey.LeftShift || e.Key == VirtualKey.RightShift)
                return;

            e.Handled = true;
            AppSettings.SetKeyboardKey(control, e.Key);
            if (control == ControlKey.Select)
                AppSettings.SetKeyboardKey(ControlKey.Mode, e.Key);
            tb.Text = FormatVirtualKey(e.Key);
        }

        private void StartGamepadPolling()
        {
            if (_padPollTimer == null)
            {
                _padPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _padPollTimer.Tick += OnPollGamepad;
            }
            _padPollTimer.Start();
        }

        private void StopGamepadPolling()
        {
            _padPollTimer?.Stop();
        }

        private void OnPollGamepad(object? sender, object e)
        {
            if (_focusedGamepadControl is not ControlKey control || _focusedGamepadBox == null)
                return;

            var state = _controller.GetState();
            if (!state.connected)
                return;

            uint mask = state.buttons;
            int trigThreshold = (int)(AppSettings.ControllerSensivity * 255.0);
            if (state.LeftTrigger > trigThreshold)
                mask |= (uint)ControllerKey.LEFT_TRIGGER;
            if (state.RightTrigger > trigThreshold)
                mask |= (uint)ControllerKey.RIGHT_TRIGGER;

            // Match Nesbox.dll: ignore D-Pad and only accept single button press.
            if (mask == 0 || (mask & 0xFu) != 0)
                return;
            if (BitOperations.PopCount(mask) != 1)
                return;

            var ckey = (ControllerKey)mask;
            AppSettings.SetGamepadKey(control, ckey);
            if (control == ControlKey.Select)
                AppSettings.SetGamepadKey(ControlKey.Mode, ckey);
            _focusedGamepadBox.Text = ckey.ToString();
        }

        private void RefreshBindingLabels()
        {
            foreach (var (name, key) in _keyboardBoxes)
            {
                if (FindName(name) is TextBox tb)
                    tb.Text = FormatVirtualKey(AppSettings.GetKeyboardKey(key));
            }

            foreach (var (name, key) in _gamepadBoxes)
            {
                if (FindName(name) is TextBox tb)
                    tb.Text = AppSettings.GetGamepadKey(key).ToString();
            }
        }

        private static string FormatVirtualKey(VirtualKey key) => key.ToString();
    }
}
