using MBox.Model;
using MBox.ViewModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MBox.View
{
    public sealed partial class SavesPage : CommonPage
    {
        public SavesPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public SavesPageViewModel ViewModel => (SavesPageViewModel)DataContext;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ViewModel.Refresh();
            if (FindName("ItemGridView") is ListViewBase grid)
            {
                grid.ItemClick -= ItemGridView_ItemClick;
                grid.ItemClick += ItemGridView_ItemClick;
                grid.SelectionChanged -= ItemGridView_SelectionChanged;
                grid.SelectionChanged += ItemGridView_SelectionChanged;
            }
            if (FindName("BackButton") is Button backButton)
            {
                backButton.Click -= Back_Click;
                backButton.Click += Back_Click;
            }
        }

        private void ItemGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SavedRom saved)
                Frame.Navigate(typeof(EmulatorPage), new EmulatorPage.Launch(saved.Rom, true));
        }

        private void ItemGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListViewBase grid)
                return;

            ViewModel.SelectedItems.Clear();
            foreach (var item in grid.SelectedItems)
                ViewModel.SelectedItems.Add(item);
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame?.CanGoBack == true)
                Frame.GoBack();
        }
    }
}
