using MBox.Model;
using MBoxPanels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace GamePad.Logic
{
    public enum PadStatus
    {
        Playing = 0,
        Editing = 1,
    }

    public enum PadMode
    {
        Nes = 0,
        Snes = 1,
        Sega = 2,
        Gba = 3,
    }
}

namespace GamePad
{
    using GamePad.Logic;

    public sealed partial class GamePadControl : UserControl
    {
        public static readonly DependencyProperty AccentColorProperty =
            DependencyProperty.Register(nameof(AccentColor), typeof(Brush), typeof(GamePadControl),
                new PropertyMetadata(new SolidColorBrush(Microsoft.UI.Colors.OrangeRed), OnAccentColorChanged));

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(nameof(Mode), typeof(PadMode), typeof(GamePadControl),
                new PropertyMetadata(PadMode.Nes, OnModeChanged));

        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register(nameof(Status), typeof(PadStatus), typeof(GamePadControl),
                new PropertyMetadata(PadStatus.Playing, OnStatusChanged));

        public event EventHandler<int>? ControlsChanged;
        public event EventHandler? ExitSetupRequested;

        private readonly Dictionary<uint, ControlKey[]> _activePointers = new();
        private readonly Dictionary<string, FrameworkElement> _draggableElements = new();
        private (FrameworkElement Element, Point StartPointer, TranslateTransform Transform, Point StartOffset)? _dragState;

        private static readonly (string Name, ControlKey Key)[] ButtonElements =
        {
            ("Select_ButtonGrid", ControlKey.Select),
            ("Mode_ButtonGrid", ControlKey.Mode),
            ("Start_ButtonGrid", ControlKey.Start),
            ("A_ButtonGrid", ControlKey.A),
            ("B_ButtonGrid", ControlKey.B),
            ("C_ButtonGrid", ControlKey.C),
            ("X_ButtonGrid", ControlKey.X),
            ("Y_ButtonGrid", ControlKey.Y),
            ("Z_ButtonGrid", ControlKey.Z),
            ("L_ButtonGrid", ControlKey.L),
            ("R_ButtonGrid", ControlKey.R),
        };

        public GamePadControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public Brush AccentColor
        {
            get => (Brush)GetValue(AccentColorProperty);
            set => SetValue(AccentColorProperty, value);
        }

        public PadMode Mode
        {
            get => (PadMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        public PadStatus Status
        {
            get => (PadStatus)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            WireElements();
            ApplyModeVisualState(Mode);
            ApplyStatusVisualState(Status);
            LoadPositions();
        }

        private static void OnAccentColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GamePadControl ctrl && e.NewValue is Brush brush)
                ctrl.ApplyAccentBrush(brush);
        }

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GamePadControl ctrl && e.NewValue is PadMode mode)
            {
                ctrl.ApplyModeVisualState(mode);
                ctrl.LoadPositions();
            }
        }

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GamePadControl ctrl && e.NewValue is PadStatus status)
            {
                if (status != PadStatus.Editing && e.OldValue is PadStatus oldStatus && oldStatus == PadStatus.Editing)
                    ctrl.SavePositions();

                ctrl._activePointers.Clear();
                ctrl._dragState = null;
                ctrl.EmitControlMask();
                ctrl.ApplyStatusVisualState(status);
            }
        }

        private void ApplyAccentBrush(Brush brush)
        {
            if (Resources.TryGetValue("BorderBrush", out var _) && brush is SolidColorBrush scb)
                Resources["BorderBrush"] = new SolidColorBrush(scb.Color);
        }

        private void ApplyModeVisualState(PadMode mode)
        {
            VisualStateManager.GoToState(this, mode.ToString(), false);
        }

        private void ApplyStatusVisualState(PadStatus status)
        {
            VisualStateManager.GoToState(this, status.ToString(), false);
            if (FindName("BlackBorder") is UIElement blackBorder)
                blackBorder.Visibility = status == PadStatus.Editing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void WireElements()
        {
            if (_draggableElements.Count > 0)
                return;

            if (FindName("Joy_ControllerGrid") is FrameworkElement joy)
            {
                _draggableElements["Joy_Controller"] = joy;
                HookTouchElement(joy, isDpad: true, ControlKey.Up);
            }

            foreach (var (elementName, controlKey) in ButtonElements)
            {
                if (FindName(elementName) is FrameworkElement fe)
                {
                    string prefix = elementName.Replace("Grid", string.Empty);
                    _draggableElements[prefix] = fe;
                    HookTouchElement(fe, isDpad: false, controlKey);
                }
            }

            if (FindName("BlackBorder") is FrameworkElement blackBorder)
            {
                blackBorder.DoubleTapped += (_, _) =>
                {
                    if (Status == PadStatus.Editing)
                        ExitSetupRequested?.Invoke(this, EventArgs.Empty);
                };
            }
        }

        private void HookTouchElement(FrameworkElement element, bool isDpad, ControlKey singleKey)
        {
            element.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                element.CapturePointer(e.Pointer);

                if (Status == PadStatus.Editing)
                {
                    var tx = EnsureTranslateTransform(element);
                    var pt = e.GetCurrentPoint(this).Position;
                    _dragState = (element, pt, tx, new Point(tx.X, tx.Y));
                    return;
                }

                if (isDpad)
                    UpdateDpadPointer(e.Pointer.PointerId, element, e.GetCurrentPoint(element).Position);
                else
                {
                    _activePointers[e.Pointer.PointerId] = new[] { singleKey };
                    EmitControlMask();
                }
            };

            element.PointerMoved += (_, e) =>
            {
                if (Status == PadStatus.Editing)
                {
                    if (_dragState is { } drag && ReferenceEquals(drag.Element, element))
                    {
                        var cur = e.GetCurrentPoint(this).Position;
                        drag.Transform.X = drag.StartOffset.X + (cur.X - drag.StartPointer.X);
                        drag.Transform.Y = drag.StartOffset.Y + (cur.Y - drag.StartPointer.Y);
                        e.Handled = true;
                    }
                    return;
                }

                if (isDpad && _activePointers.ContainsKey(e.Pointer.PointerId))
                {
                    e.Handled = true;
                    UpdateDpadPointer(e.Pointer.PointerId, element, e.GetCurrentPoint(element).Position);
                }
            };

            void ReleasePointer(PointerRoutedEventArgs e)
            {
                element.ReleasePointerCapture(e.Pointer);

                if (Status == PadStatus.Editing)
                {
                    if (_dragState is { } drag && ReferenceEquals(drag.Element, element))
                    {
                        _dragState = null;
                        SavePositions();
                    }
                    return;
                }

                if (_activePointers.Remove(e.Pointer.PointerId))
                    EmitControlMask();
            }

            element.PointerReleased += (_, e) => ReleasePointer(e);
            element.PointerCanceled += (_, e) => ReleasePointer(e);
            element.PointerCaptureLost += (_, e) => ReleasePointer(e);
        }

        private void UpdateDpadPointer(uint pointerId, FrameworkElement dpad, Point pos)
        {
            double cx = dpad.ActualWidth * 0.5;
            double cy = dpad.ActualHeight * 0.5;
            if (cx <= 0 || cy <= 0)
                return;

            double dx = pos.X - cx;
            double dy = pos.Y - cy;
            double deadzoneX = cx * 0.25;
            double deadzoneY = cy * 0.25;

            var keys = new List<ControlKey>(2);
            if (dy < -deadzoneY) keys.Add(ControlKey.Up);
            if (dy > deadzoneY) keys.Add(ControlKey.Down);
            if (dx < -deadzoneX) keys.Add(ControlKey.Left);
            if (dx > deadzoneX) keys.Add(ControlKey.Right);

            _activePointers[pointerId] = keys.ToArray();
            EmitControlMask();
        }

        private void EmitControlMask()
        {
            int mask = 0;
            foreach (var list in _activePointers.Values)
            {
                foreach (var k in list)
                    mask |= 1 << (int)k;
            }
            ControlsChanged?.Invoke(this, mask);
        }

        private static TranslateTransform EnsureTranslateTransform(FrameworkElement element)
        {
            if (element.RenderTransform is TranslateTransform tt)
                return tt;

            tt = new TranslateTransform();
            element.RenderTransform = tt;
            return tt;
        }

        public void LoadPositions()
        {
            if (_draggableElements.Count == 0)
                return;

            string prefix = Mode.ToString();
            foreach (var (name, fe) in _draggableElements)
            {
                if (AppSettings.TryLoadControlPosition(prefix, name, out var rect))
                {
                    var tx = EnsureTranslateTransform(fe);
                    tx.X = rect.X;
                    tx.Y = rect.Y;
                }
                else if (fe.RenderTransform is TranslateTransform tx)
                {
                    tx.X = 0;
                    tx.Y = 0;
                }
            }
        }

        public void SavePositions()
        {
            if (_draggableElements.Count == 0)
                return;

            string prefix = Mode.ToString();
            foreach (var (name, fe) in _draggableElements)
            {
                var tx = fe.RenderTransform as TranslateTransform;
                double x = tx?.X ?? 0;
                double y = tx?.Y ?? 0;
                AppSettings.SaveControlPosition(prefix, name, new Rect(x, y, fe.ActualWidth, fe.ActualHeight));
            }
        }
    }
}
