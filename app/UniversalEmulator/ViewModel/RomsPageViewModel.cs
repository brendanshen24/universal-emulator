using System.Collections.ObjectModel;
using System.Windows.Input;
using MBox.Model;
using MBox.View;
using MSACL.UI;

namespace MBox.ViewModel
{
    public sealed class RomsPageViewModel : ViewModelBase
    {
        public RomsPageViewModel()
        {
            SelectedItems = new ObservableCollection<object>();
            Roms = new ObservableCollection<Rom>();
            AddCommand = new RelayCommand(async () => await AddAsync());
            DeleteCommand = new RelayCommand(async () => await DeleteAsync());
            Refresh();
        }

        public ObservableCollection<Rom> Roms { get; }

        public ObservableCollection<object> SelectedItems { get; }

        public ICommand AddCommand { get; }

        public ICommand DeleteCommand { get; }

        public void Refresh()
        {
            var library = ((App)Microsoft.UI.Xaml.Application.Current).Library;
            Roms.Clear();
            foreach (var rom in library.AllRoms.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
                Roms.Add(rom);
        }

        private async Task AddAsync()
        {
            await GamePicker.PickMultipleAndAddAsync(this);
            Refresh();
        }

        private async Task DeleteAsync()
        {
            var targets = SelectedItems.OfType<Rom>().ToList();
            if (targets.Count == 0)
                return;

            var library = ((App)Microsoft.UI.Xaml.Application.Current).Library;
            await library.DeleteAsync(targets);
            SelectedItems.Clear();
            Refresh();
        }
    }
}
