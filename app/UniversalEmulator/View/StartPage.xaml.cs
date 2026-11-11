using MBox.Model;
using MBox.ViewModel;
using Microsoft.Toolkit.Uwp.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MBox.View
{
    public sealed partial class StartPage : CommonPage
    {
        public StartPage()
        {
            this.InitializeComponent();
            ViewModel.SettingsRequested += (s, e) => SettingsView.IsPaneOpen = true;
            ViewModel.GameSelected += (s, rom) => Play(rom);
        }

        public StartPageViewModel ViewModel => (StartPageViewModel)DataContext;

        public static readonly DependencyProperty IsNarrowModeProperty =
            DependencyProperty.Register(nameof(IsNarrowMode), typeof(bool), typeof(StartPage), new PropertyMetadata(false));

        public bool IsNarrowMode { get => (bool)GetValue(IsNarrowModeProperty); set => SetValue(IsNarrowModeProperty, value); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.Refresh();
        }

        private void HamburgerElement_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = (HamburgerItem)e.ClickedItem;
            item.Command?.Execute(item);
        }

        private void ItemGridView_ItemClick(object sender, ItemClickEventArgs e) => Play((Rom)e.ClickedItem);

        private void Play(Rom rom) => Frame.Navigate(typeof(EmulatorPage), rom is SavedRom saved ? new EmulatorPage.Launch(saved.Rom, true) : new EmulatorPage.Launch(rom, false));

        private void Settings_CloseClicked(object? sender, EventArgs e) => SettingsView.IsPaneOpen = false;

        private void SearchBoxSuggestions_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) =>
            ViewModel.SearchText = sender.Text;

        protected override void OnKeyUp(Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.Handled)
                return;

            if (e.Key == Windows.System.VirtualKey.GamepadY)
            {
                SearchBoxSuggestions.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.GamepadView)
            {
                HamburgerElement.IsPaneOpen = !HamburgerElement.IsPaneOpen;
                Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        }
    }
}
