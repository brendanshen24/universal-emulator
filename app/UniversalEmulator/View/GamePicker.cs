using System.Runtime.InteropServices.WindowsRuntime;
using MBox.Model;
using MBox.ViewModel;
using MBoxPanels;
using Windows.Storage.Pickers;

namespace MBox.View
{
    public static class GamePicker
    {
        private static readonly string[] RomExtensions =
        {
            ".nes", ".gen", ".smd", ".smc", ".sfc", ".bin", ".gbc", ".gba", ".gb",
        };

        private static bool IsRomExtension(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            return RomExtensions.Any(r => string.Equals(r, ext, StringComparison.OrdinalIgnoreCase));
        }

        public static async Task<Rom?> PickAndAddAsync(StartPageViewModel vm)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
            };
            foreach (var ext in RomExtensions)
                picker.FileTypeFilter.Add(ext);
            picker.FileTypeFilter.Add(".7z");

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return null;

            vm.IsLongOperationActive = true;
            vm.LongOperationProgress = 0;
            try
            {
                if (string.Equals(file.FileType, ".7z", StringComparison.OrdinalIgnoreCase))
                {
                    var added = await AddSevenZipAsync(file);
                    return added.FirstOrDefault();
                }
                var rom = await AddStorageFileAsync(file);
                vm.LongOperationProgress = 100;
                return rom;
            }
            finally
            {
                vm.IsLongOperationActive = false;
            }
        }

        public static async Task<IReadOnlyList<Rom>> PickMultipleAndAddAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
            };
            foreach (var ext in RomExtensions)
                picker.FileTypeFilter.Add(ext);
            picker.FileTypeFilter.Add(".7z");

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0)
                return Array.Empty<Rom>();

            var added = new List<Rom>();
            foreach (var file in files)
            {
                if (string.Equals(file.FileType, ".7z", StringComparison.OrdinalIgnoreCase))
                    added.AddRange(await AddSevenZipAsync(file));
                else
                    added.Add(await AddStorageFileAsync(file));
            }

            return added;
        }

        private static async Task<Rom> AddStorageFileAsync(Windows.Storage.StorageFile file)
        {
            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
            var library = ((App)Microsoft.UI.Xaml.Application.Current).Library;
            return await library.AddAsync(file.Name, buffer.ToArray());
        }

        private static async Task<List<Rom>> AddSevenZipAsync(Windows.Storage.StorageFile file)
        {
            var result = new List<Rom>();
            try
            {
                var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
                using var archive = new SevenZipWrapper(buffer.ToArray());
                var library = ((App)Microsoft.UI.Xaml.Application.Current).Library;
                foreach (var entry in archive.list())
                {
                    if (!IsRomExtension(entry))
                        continue;

                    byte[] data = archive.extract(entry);
                    if (data.Length == 0)
                        continue;

                    string entryName = Path.GetFileName(entry.Replace('\\', '/'));
                    var rom = await library.AddAsync(entryName, data);
                    result.Add(rom);
                }
            }
            catch
            {
                await AppHelpers.ShowMessageAsync(
                    AppHelpers.GetString("ArchiveCorruptedMessage", "Failed to extract archive."));
            }
            return result;
        }

    }
}
