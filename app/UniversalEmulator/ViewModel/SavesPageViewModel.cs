using System.Collections.ObjectModel;
using System.Windows.Input;
using MBox.Model;
using MSACL.UI;

namespace MBox.ViewModel
{
    public sealed class SavesPageViewModel : ViewModelBase
    {
        public SavesPageViewModel()
        {
            SelectedItems = new ObservableCollection<object>();
            Roms = new ObservableCollection<SavedRom>();
            DeleteCommand = new RelayCommand(async () => await DeleteAsync());
            Refresh();
        }

        public ObservableCollection<SavedRom> Roms { get; }

        public ObservableCollection<object> SelectedItems { get; }

        public ICommand DeleteCommand { get; }

        public void Refresh()
        {
            var library = ((App)Microsoft.UI.Xaml.Application.Current).Library;
            Roms.Clear();
            foreach (var md5 in library.Saves)
            {
                var rom = library.Find(md5);
                if (rom != null)
                    Roms.Add(new SavedRom(rom));
            }
        }

        private async Task DeleteAsync()
        {
            var targets = SelectedItems.OfType<SavedRom>().ToList();
            if (targets.Count == 0)
                return;

            var library = ((App)Microsoft.UI.Xaml.Application.Current).Library;
            await library.DeleteSavesAsync(targets);
            SelectedItems.Clear();
            Refresh();
        }
    }
}
