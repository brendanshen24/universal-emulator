// MSACL.UI.UWP.CustomTriggers: the adaptive-layout triggers StartPage.xaml uses.
using Microsoft.UI.Xaml;
using Windows.Graphics.Display;

namespace MSACL.UI.UWP.CustomTriggers
{
    /// <summary>Active when the screen is at least MinDiagonalSizeInInches.</summary>
    public class DiagonalSizeTrigger : StateTriggerBase, ICustomTrigger
    {
        public double MinDiagonalSizeInInches
        {
            get => _min;
            set { _min = value; Update(); }
        }

        private double _min;

        public DiagonalSizeTrigger() => Update();

        public bool IsActive { get; private set; }

        private void Update()
        {
            double? diagonal = null;
            try { diagonal = DisplayInformation.GetForCurrentView().DiagonalSizeInInches; } catch { }
            // shortcut: a desktop screen that reports no size is treated as large, as every PC monitor the original ran on was
            IsActive = (diagonal ?? double.MaxValue) >= _min;
            SetActive(IsActive);
        }
    }

    /// <summary>Active when the window is at least MinWidth x MinHeight.</summary>
    public class WindowSizeTrigger : StateTriggerBase, ICustomTrigger
    {
        public double MinWidth { get => _minWidth; set { _minWidth = value; Update(); } }
        public double MinHeight { get => _minHeight; set { _minHeight = value; Update(); } }

        private double _minWidth, _minHeight;

        public WindowSizeTrigger()
        {
            if (MBox.App.MainWindow is { } window) window.SizeChanged += (s, e) => Update();
            Update();
        }

        public bool IsActive { get; private set; }

        private void Update()
        {
            var bounds = MBox.App.MainWindow?.Bounds ?? default;
            IsActive = bounds.Width >= _minWidth && bounds.Height >= _minHeight;
            SetActive(IsActive);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? Changed;
    }

    /// <summary>Active when every trigger in Triggers is active.</summary>
    public class CompositeTrigger : StateTriggerBase
    {
        public List<ICustomTrigger> Triggers { get; } = new();

        public CompositeTrigger()
        {
            if (MBox.App.MainWindow is { } window) window.SizeChanged += (s, e) => Update();
            // members are added after construction by the XAML loader
            DispatcherQueueHelper.Post(Update);
        }

        private void Update() => SetActive(Triggers.All(t => t.IsActive));
    }

    public interface ICustomTrigger
    {
        bool IsActive { get; }
    }

    internal static class DispatcherQueueHelper
    {
        public static void Post(Action action) =>
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => action());
    }
}
