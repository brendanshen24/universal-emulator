using System.Globalization;
using System.Text.Json;
using GameController;
using MBoxPanels;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;

namespace MBox.Model
{
    public static class AppSettings
    {
        private const string KeySoundEnabled = "IsSoundEnabled";
        private const string KeyBlurEnabled = "IsBlurEnabled";
        private const string KeyFullScreenEnabled = "IsFullScreenEnabled";
        private const string KeyVirtualGamePadEnabled = "IsVirtualGamePadEnabled";
        private const string KeyControllerSensivity = "ControllerSensivity";
        private const string KeyLaunchCount = "LaunchCount";
        private const string KeyShowRateNeed = "IsShowRateNeed";
        private const string KeyKeyBindings = "KeyBindings";
        private const string KeyGamepadPrefix = "pad";
        private const string KeyScreenPositionPrefix = "screen_position";

        private static Dictionary<ControlKey, VirtualKey>? _keyboardBindings;
        private static Dictionary<ControlKey, ControllerKey>? _gamepadBindings;

        private static ApplicationDataContainer Settings => ApplicationData.Current.LocalSettings;

        public static bool IsSoundEnabled
        {
            get => GetValue(KeySoundEnabled, true);
            set => SetValue(KeySoundEnabled, value);
        }

        public static bool IsBlurEnabled
        {
            get => GetValue(KeyBlurEnabled, false);
            set => SetValue(KeyBlurEnabled, value);
        }

        public static bool IsFullScreenEnabled
        {
            get => GetValue(KeyFullScreenEnabled, false);
            set => SetValue(KeyFullScreenEnabled, value);
        }

        public static bool IsVirtualGamePadEnabled
        {
            get => GetValue(KeyVirtualGamePadEnabled, false);
            set => SetValue(KeyVirtualGamePadEnabled, value);
        }

        public static double ControllerSensivity
        {
            get => GetValue(KeyControllerSensivity, 0.5);
            set => SetValue(KeyControllerSensivity, value);
        }

        public static int LaunchCount
        {
            get => GetValue(KeyLaunchCount, 0);
            set => SetValue(KeyLaunchCount, value);
        }

        public static bool IsShowRateNeed
        {
            get => GetValue(KeyShowRateNeed, false);
            set => SetValue(KeyShowRateNeed, value);
        }

        public static Dictionary<ControlKey, VirtualKey> CreateDefaultKeyboardBindings() => new()
        {
            [ControlKey.Up] = VirtualKey.Up,
            [ControlKey.Down] = VirtualKey.Down,
            [ControlKey.Left] = VirtualKey.Left,
            [ControlKey.Right] = VirtualKey.Right,
            [ControlKey.A] = VirtualKey.A,
            [ControlKey.B] = VirtualKey.S,
            [ControlKey.C] = VirtualKey.D,
            [ControlKey.X] = VirtualKey.Z,
            [ControlKey.Y] = VirtualKey.X,
            [ControlKey.Z] = VirtualKey.C,
            [ControlKey.L] = VirtualKey.D,
            [ControlKey.R] = VirtualKey.C,
            [ControlKey.Mode] = VirtualKey.Space,
            [ControlKey.Select] = VirtualKey.Space,
            [ControlKey.Start] = VirtualKey.Enter,
        };

        public static Dictionary<ControlKey, ControllerKey> CreateDefaultGamepadBindings() => new()
        {
            [ControlKey.A] = ControllerKey.A,
            [ControlKey.B] = ControllerKey.B,
            [ControlKey.C] = ControllerKey.LEFT_TRIGGER,
            [ControlKey.X] = ControllerKey.X,
            [ControlKey.Y] = ControllerKey.Y,
            [ControlKey.Z] = ControllerKey.RIGHT_TRIGGER,
            [ControlKey.L] = ControllerKey.LEFT_TRIGGER,
            [ControlKey.R] = ControllerKey.RIGHT_TRIGGER,
            [ControlKey.Mode] = ControllerKey.LEFT_SHOULDER,
            [ControlKey.Select] = ControllerKey.LEFT_SHOULDER,
            [ControlKey.Start] = ControllerKey.RIGHT_SHOULDER,
        };

        public static IReadOnlyDictionary<ControlKey, VirtualKey> KeyboardBindings
        {
            get
            {
                if (_keyboardBindings != null)
                    return _keyboardBindings;

                _keyboardBindings = LoadKeyboardBindings();
                return _keyboardBindings;
            }
        }

        public static IReadOnlyDictionary<ControlKey, ControllerKey> GamepadBindings
        {
            get
            {
                if (_gamepadBindings != null)
                    return _gamepadBindings;

                _gamepadBindings = LoadGamepadBindings();
                return _gamepadBindings;
            }
        }

        public static VirtualKey GetKeyboardKey(ControlKey control) =>
            KeyboardBindings.TryGetValue(control, out var key) ? key : CreateDefaultKeyboardBindings()[control];

        public static void SetKeyboardKey(ControlKey control, VirtualKey key)
        {
            EnsureLoaded();
            _keyboardBindings![control] = key;
            SaveKeyboardBindings(_keyboardBindings);
        }

        public static ControllerKey GetGamepadKey(ControlKey control) =>
            GamepadBindings.TryGetValue(control, out var key) ? key : CreateDefaultGamepadBindings().GetValueOrDefault(control);

        public static void SetGamepadKey(ControlKey control, ControllerKey key)
        {
            EnsureLoaded();
            _gamepadBindings![control] = key;
            SaveGamepadBinding(control, key);
        }

        public static void ResetBindingsToDefaults()
        {
            _keyboardBindings = CreateDefaultKeyboardBindings();
            _gamepadBindings = CreateDefaultGamepadBindings();
            SaveKeyboardBindings(_keyboardBindings);
            foreach (var kv in _gamepadBindings)
                SaveGamepadBinding(kv.Key, kv.Value);
        }

        public static IEnumerable<ControlKey> GetBoundControlKeys(VirtualKey key)
        {
            EnsureLoaded();
            foreach (var kv in _keyboardBindings!)
            {
                if (kv.Value == key)
                    yield return kv.Key;
            }
        }

        public static int BuildControllerMask(Controller controller)
        {
            var state = controller.GetState();
            if (!state.connected)
                return 0;

            double sens = ControllerSensivity;
            int axisThreshold = (int)(sens * 32767.0);
            int triggerThreshold = (int)(sens * 255.0);

            uint buttons = state.buttons;
            if (state.dpad_down || state.LeftThumbY < -axisThreshold)
                buttons |= (uint)ControllerKey.DPAD_DOWN;
            if (state.dpad_up || state.LeftThumbY > axisThreshold)
                buttons |= (uint)ControllerKey.DPAD_UP;
            if (state.dpad_left || state.LeftThumbX < -axisThreshold)
                buttons |= (uint)ControllerKey.DPAD_LEFT;
            if (state.dpad_right || state.LeftThumbX > axisThreshold)
                buttons |= (uint)ControllerKey.DPAD_RIGHT;
            if (state.LeftTrigger > triggerThreshold)
                buttons |= (uint)ControllerKey.LEFT_TRIGGER;
            if (state.RightTrigger > triggerThreshold)
                buttons |= (uint)ControllerKey.RIGHT_TRIGGER;

            int mask = 0;
            if ((buttons & (uint)ControllerKey.DPAD_UP) != 0) mask |= 1 << (int)ControlKey.Up;
            if ((buttons & (uint)ControllerKey.DPAD_DOWN) != 0) mask |= 1 << (int)ControlKey.Down;
            if ((buttons & (uint)ControllerKey.DPAD_LEFT) != 0) mask |= 1 << (int)ControlKey.Left;
            if ((buttons & (uint)ControllerKey.DPAD_RIGHT) != 0) mask |= 1 << (int)ControlKey.Right;

            EnsureLoaded();
            foreach (var kv in _gamepadBindings!)
            {
                uint ckey = (uint)kv.Value;
                if (ckey != 0 && (buttons & ckey) == ckey)
                    mask |= 1 << (int)kv.Key;
            }

            return mask;
        }

        public static void SaveControlPosition(string prefix, string name, Rect rect)
        {
            string key = $"{KeyScreenPositionPrefix}{prefix}_{name}";
            string val = string.Join("*",
                rect.X.ToString(CultureInfo.InvariantCulture),
                rect.Y.ToString(CultureInfo.InvariantCulture),
                rect.Width.ToString(CultureInfo.InvariantCulture),
                rect.Height.ToString(CultureInfo.InvariantCulture));
            SetValue(key, val);
        }

        public static bool TryLoadControlPosition(string prefix, string name, out Rect rect)
        {
            rect = default;
            string key = $"{KeyScreenPositionPrefix}{prefix}_{name}";
            if (Settings.Values.TryGetValue(key, out var raw) && raw is string s)
            {
                var parts = s.Split('*');
                if (parts.Length >= 4 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double w) &&
                    double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
                {
                    rect = new Rect(x, y, w, h);
                    return true;
                }
            }
            return false;
        }

        public static void IncrementLaunchCountAndCheckRate()
        {
            int next = LaunchCount + 1;
            LaunchCount = next;
            if (next == 4)
                IsShowRateNeed = true;
        }

        private static void EnsureLoaded()
        {
            _keyboardBindings ??= LoadKeyboardBindings();
            _gamepadBindings ??= LoadGamepadBindings();
        }

        private static Dictionary<ControlKey, VirtualKey> LoadKeyboardBindings()
        {
            var dict = CreateDefaultKeyboardBindings();
            if (Settings.Values.TryGetValue(KeyKeyBindings, out var raw))
            {
                if (raw is string json)
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<Dictionary<int, int>>(json);
                        if (parsed != null)
                        {
                            foreach (var kv in parsed)
                                dict[(ControlKey)kv.Key] = (VirtualKey)kv.Value;
                        }
                    }
                    catch
                    {
                        // Ignore malformed saved settings and fall back to defaults.
                    }
                }
                else if (raw is ApplicationDataCompositeValue composite)
                {
                    foreach (var kv in composite)
                    {
                        if (Enum.TryParse<ControlKey>(kv.Key, out var ck) && kv.Value is int vk)
                            dict[ck] = (VirtualKey)vk;
                    }
                }
            }
            return dict;
        }

        private static void SaveKeyboardBindings(Dictionary<ControlKey, VirtualKey> dict)
        {
            var serializable = new Dictionary<int, int>(dict.Count);
            foreach (var kv in dict)
                serializable[(int)kv.Key] = (int)kv.Value;
            SetValue(KeyKeyBindings, JsonSerializer.Serialize(serializable));
        }

        private static Dictionary<ControlKey, ControllerKey> LoadGamepadBindings()
        {
            var dict = CreateDefaultGamepadBindings();
            foreach (var control in Enum.GetValues<ControlKey>())
            {
                if (control < ControlKey.Start)
                    continue;

                string key = KeyGamepadPrefix + (int)control;
                if (Settings.Values.TryGetValue(key, out var raw) && raw is int ck)
                    dict[control] = (ControllerKey)ck;
            }
            return dict;
        }

        private static void SaveGamepadBinding(ControlKey control, ControllerKey key) =>
            SetValue(KeyGamepadPrefix + (int)control, (int)key);

        private static T GetValue<T>(string key, T defaultValue)
        {
            if (Settings.Values.TryGetValue(key, out var raw) && raw is T typed)
                return typed;
            return defaultValue;
        }

        private static void SetValue(string key, object value) =>
            Settings.Values[key] = value;
    }
}
