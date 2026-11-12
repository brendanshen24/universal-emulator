// Port of Microsoft.Toolkit.Uwp.UI.Controls.HamburgerMenu from Windows Community Toolkit v1.3.0 (MIT License,
// Copyright (c) Microsoft), the version the original app shipped. Same properties, defaults and click behaviour.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.Toolkit.Uwp.UI.Controls
{
    [TemplatePart(Name = "HamburgerButton", Type = typeof(Button))]
    [TemplatePart(Name = "ButtonsListView", Type = typeof(ListViewBase))]
    [TemplatePart(Name = "OptionsListView", Type = typeof(ListViewBase))]
    public partial class HamburgerMenu : ContentControl
    {
        private Button? _hamburgerButton;
        private ListViewBase? _buttonsListView;
        private ListViewBase? _optionsListView;

#if DEBUG
        internal int TemplateItemCount => _buttonsListView?.Items.Count ?? 0;
#endif

        public HamburgerMenu()
        {
            DefaultStyleKey = typeof(HamburgerMenu);
        }

        protected override void OnApplyTemplate()
        {
            if (_hamburgerButton != null) _hamburgerButton.Click -= HamburgerButton_Click;
            if (_buttonsListView != null) _buttonsListView.ItemClick -= ButtonsListView_ItemClick;
            if (_optionsListView != null) _optionsListView.ItemClick -= OptionsListView_ItemClick;

            _hamburgerButton = (Button)GetTemplateChild("HamburgerButton");
            _buttonsListView = (ListViewBase)GetTemplateChild("ButtonsListView");
            _optionsListView = (ListViewBase)GetTemplateChild("OptionsListView");

            if (_hamburgerButton != null) _hamburgerButton.Click += HamburgerButton_Click;
            if (_buttonsListView != null) _buttonsListView.ItemClick += ButtonsListView_ItemClick;
            if (_optionsListView != null) _optionsListView.ItemClick += OptionsListView_ItemClick;

            base.OnApplyTemplate();
        }

        public event ItemClickEventHandler? ItemClick;
        public event ItemClickEventHandler? OptionsItemClick;

        private void HamburgerButton_Click(object sender, RoutedEventArgs e) => IsPaneOpen = !IsPaneOpen;

        private void ButtonsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (_optionsListView != null) _optionsListView.SelectedIndex = -1;
            ItemClick?.Invoke(this, e);
        }

        private void OptionsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (_buttonsListView != null) _buttonsListView.SelectedIndex = -1;
            OptionsItemClick?.Invoke(this, e);
        }

        public static readonly DependencyProperty OpenPaneLengthProperty = DependencyProperty.Register(nameof(OpenPaneLength), typeof(double), typeof(HamburgerMenu), new PropertyMetadata(240.0));
        public static readonly DependencyProperty PanePlacementProperty = DependencyProperty.Register(nameof(PanePlacement), typeof(SplitViewPanePlacement), typeof(HamburgerMenu), new PropertyMetadata(SplitViewPanePlacement.Left));
        public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(nameof(DisplayMode), typeof(SplitViewDisplayMode), typeof(HamburgerMenu), new PropertyMetadata(SplitViewDisplayMode.CompactInline));
        public static readonly DependencyProperty CompactPaneLengthProperty = DependencyProperty.Register(nameof(CompactPaneLength), typeof(double), typeof(HamburgerMenu), new PropertyMetadata(48.0));
        public static readonly DependencyProperty PaneBackgroundProperty = DependencyProperty.Register(nameof(PaneBackground), typeof(Brush), typeof(HamburgerMenu), new PropertyMetadata(null));
        public static readonly DependencyProperty IsPaneOpenProperty = DependencyProperty.Register(nameof(IsPaneOpen), typeof(bool), typeof(HamburgerMenu), new PropertyMetadata(false));
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(object), typeof(HamburgerMenu), new PropertyMetadata(null));
        public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(HamburgerMenu), new PropertyMetadata(null));
        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(HamburgerMenu), new PropertyMetadata(null));
        public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(HamburgerMenu), new PropertyMetadata(-1));
        public static readonly DependencyProperty OptionsItemsSourceProperty = DependencyProperty.Register(nameof(OptionsItemsSource), typeof(object), typeof(HamburgerMenu), new PropertyMetadata(null));
        public static readonly DependencyProperty OptionsItemTemplateProperty = DependencyProperty.Register(nameof(OptionsItemTemplate), typeof(DataTemplate), typeof(HamburgerMenu), new PropertyMetadata(null));
        public static readonly DependencyProperty OptionsVisibilityProperty = DependencyProperty.Register(nameof(OptionsVisibility), typeof(Visibility), typeof(HamburgerMenu), new PropertyMetadata(Visibility.Visible));
        public static readonly DependencyProperty HamburgerWidthProperty = DependencyProperty.Register(nameof(HamburgerWidth), typeof(double), typeof(HamburgerMenu), new PropertyMetadata(48.0));
        public static readonly DependencyProperty HamburgerHeightProperty = DependencyProperty.Register(nameof(HamburgerHeight), typeof(double), typeof(HamburgerMenu), new PropertyMetadata(48.0));
        public static readonly DependencyProperty HamburgerMarginProperty = DependencyProperty.Register(nameof(HamburgerMargin), typeof(Thickness), typeof(HamburgerMenu), new PropertyMetadata(default(Thickness)));
        public static readonly DependencyProperty HamburgerMenuTemplateProperty = DependencyProperty.Register(nameof(HamburgerMenuTemplate), typeof(DataTemplate), typeof(HamburgerMenu), new PropertyMetadata(null));

        public double OpenPaneLength { get => (double)GetValue(OpenPaneLengthProperty); set => SetValue(OpenPaneLengthProperty, value); }
        public SplitViewPanePlacement PanePlacement { get => (SplitViewPanePlacement)GetValue(PanePlacementProperty); set => SetValue(PanePlacementProperty, value); }
        public SplitViewDisplayMode DisplayMode { get => (SplitViewDisplayMode)GetValue(DisplayModeProperty); set => SetValue(DisplayModeProperty, value); }
        public double CompactPaneLength { get => (double)GetValue(CompactPaneLengthProperty); set => SetValue(CompactPaneLengthProperty, value); }
        public Brush PaneBackground { get => (Brush)GetValue(PaneBackgroundProperty); set => SetValue(PaneBackgroundProperty, value); }
        public bool IsPaneOpen { get => (bool)GetValue(IsPaneOpenProperty); set => SetValue(IsPaneOpenProperty, value); }
        public object ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
        public DataTemplate ItemTemplate { get => (DataTemplate)GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
        public object SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
        public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
        public object OptionsItemsSource { get => GetValue(OptionsItemsSourceProperty); set => SetValue(OptionsItemsSourceProperty, value); }
        public DataTemplate OptionsItemTemplate { get => (DataTemplate)GetValue(OptionsItemTemplateProperty); set => SetValue(OptionsItemTemplateProperty, value); }
        public Visibility OptionsVisibility { get => (Visibility)GetValue(OptionsVisibilityProperty); set => SetValue(OptionsVisibilityProperty, value); }
        public double HamburgerWidth { get => (double)GetValue(HamburgerWidthProperty); set => SetValue(HamburgerWidthProperty, value); }
        public double HamburgerHeight { get => (double)GetValue(HamburgerHeightProperty); set => SetValue(HamburgerHeightProperty, value); }
        public Thickness HamburgerMargin { get => (Thickness)GetValue(HamburgerMarginProperty); set => SetValue(HamburgerMarginProperty, value); }
        public DataTemplate HamburgerMenuTemplate { get => (DataTemplate)GetValue(HamburgerMenuTemplateProperty); set => SetValue(HamburgerMenuTemplateProperty, value); }
    }
}
