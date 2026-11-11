// MBox.Model: the original's data types (names and members recovered from Nesbox.dll's type info).
using System.Runtime.Serialization;
using System.Windows.Input;
using MSACL.UI;

namespace MBox.Model
{
    /// <summary>A game in the library. Serialized into library.json exactly as the original did
    /// (DataContractJsonSerializer: alphabetical keys, escaped slashes, explicit nulls).</summary>
    [DataContract]
    public class Rom : BindableBase
    {
        private string? _imagePath;

        [DataMember] public virtual string? ImagePath { get => _imagePath; set => SetProperty(ref _imagePath, value); }
        [DataMember] public string? MD5 { get; set; }
        [DataMember] public string? Name { get; set; }
        /// <summary>ms-appx:/// URI for bundled games, otherwise the file name inside the game's MD5 folder.</summary>
        [DataMember] public string? Path { get; set; }
        /// <summary>Screenshot of the saved state ("" or null when there is no save).</summary>
        [DataMember] public string? SavePath { get; set; }
    }

    /// <summary>A game shown in My Saves: its picture is the save screenshot.</summary>
    public class SavedRom : Rom
    {
        public SavedRom(Rom rom)
        {
            Rom = rom;
            MD5 = rom.MD5;
            Name = rom.Name;
            Path = rom.Path;
            SavePath = rom.SavePath;
        }

        public Rom Rom { get; }
        public override string? ImagePath { get => SavePath; set { } }
    }

    public class HamburgerItem : BindableBase
    {
        private bool _isSelect;

        public string? Title { get; set; }
        public string? Symbol { get; set; }
        public ICommand? Command { get; set; }
        public bool IsSelect { get => _isSelect; set => SetProperty(ref _isSelect, value); }
    }
}
