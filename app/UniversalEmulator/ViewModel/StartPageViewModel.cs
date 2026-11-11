// MBox.ViewModel.StartPageViewModel, rebuilt from Nesbox.dll (constructor 0x180861d80, commands 0x180862e10/0x180862560/
// 0x180862520/0x180862e70, list builders 0x1807f5e00/0x1808626c0).
using System.Collections.ObjectModel;
using System.Windows.Input;
using MBox.Model;
using MSACL.UI;
using Windows.ApplicationModel.Resources;

namespace MBox.ViewModel
{
    public class ViewModelBase : BindableBase
    {
        protected static Library Library => App.Current.Library;
        protected static ResourceLoader Strings { get; } = ResourceLoader.GetForViewIndependentUse();
        protected static string GetString(string key) => Strings.GetString(key.Replace('/', '.'));
    }

    public class StartPageViewModel : ViewModelBase
    {
        private int _mode; // 0 = My Games, 1 = My Saves
        private string _searchText = "";
        private ObservableCollection<Rom> _roms = new();
        private bool _isLongOperationActive;
        private double _longOperationProgress;
        private string? _longOperationProgressText;
        private string? _longOperationTextProgress;

        public StartPageViewModel()
        {
            OpenCommand = new RelayCommand(_ => OpenAsync());
            MyGamesCommand = new RelayCommand(item => { _mode = 0; Roms = Games(); Select(item as HamburgerItem); });
            MySavesCommand = new RelayCommand(item => { _mode = 1; Roms = SavedGames(); Select(item as HamburgerItem); });
            SettingsCommand = new RelayCommand(_ => SettingsRequested?.Invoke(this, EventArgs.Empty));

            // Same items, order, glyphs and initial selection as the original.
            HamburgerItems = new ObservableCollection<HamburgerItem>
            {
                new() { Title = GetString("OpenButton/Label"), Symbol = "", Command = OpenCommand, IsSelect = false },
                new() { Title = GetString("MyGamesButton/Label"), Symbol = "", Command = MyGamesCommand, IsSelect = true },
                new() { Title = GetString("MySavesButton/Label"), Symbol = "", Command = MySavesCommand, IsSelect = false },
                new() { Title = GetString("SettingsButton/Label"), Symbol = "", Command = SettingsCommand, IsSelect = false },
            };
            Roms = Games();
        }

        public ObservableCollection<HamburgerItem> HamburgerItems { get; }
        public ObservableCollection<Rom> Roms { get => _roms; set => SetProperty(ref _roms, value); }

        public ICommand OpenCommand { get; }
        public ICommand MyGamesCommand { get; }
        public ICommand MySavesCommand { get; }
        public ICommand SettingsCommand { get; }

        public bool IsLongOperationActive { get => _isLongOperationActive; set => SetProperty(ref _isLongOperationActive, value); }
        public double LongOperationProgress
        {
            get => _longOperationProgress;
            set { SetProperty(ref _longOperationProgress, value); LongOperationTextProgress = string.Format("{0}%", (int)value); }
        }
        public string? LongOperationProgressText { get => _longOperationProgressText; set => SetProperty(ref _longOperationProgressText, value); }
        public string? LongOperationTextProgress { get => _longOperationTextProgress; set => SetProperty(ref _longOperationTextProgress, value); }

        public event EventHandler? SettingsRequested;
        public event EventHandler<Rom>? GameSelected;

        public string SearchText
        {
            get => _searchText;
            set { _searchText = value ?? ""; Roms = _mode == 0 ? Games() : SavedGames(); }
        }

        public void Refresh() => Roms = _mode == 0 ? Games() : SavedGames();

        // inferred: the filter calls a framework (string, string, StringComparison=1) -> bool method; StartsWith fits
        // "all games when the search box is empty". Confirm against SharedLibrary.dll's export names when available.
        private bool Matches(Rom rom) => (rom.Name ?? "").StartsWith(_searchText, StringComparison.CurrentCultureIgnoreCase);

        private ObservableCollection<Rom> Games() =>
            new(Library.AllRoms.Where(Matches).OrderBy(r => r.Name));

        private ObservableCollection<Rom> SavedGames() =>
            new(Library.Saves.Select(Library.Find).Where(r => r != null).Select(r => (Rom)new SavedRom(r!)).Where(Matches).OrderBy(r => r.Name));

        private void Select(HamburgerItem? selected)
        {
            if (selected == null) return;
            foreach (var item in HamburgerItems) item.IsSelect = item == selected;
        }

        private async void OpenAsync()
        {
            var rom = await View.GamePicker.PickAndAddAsync(this);
            if (rom != null) GameSelected?.Invoke(this, rom);
        }
    }
}
