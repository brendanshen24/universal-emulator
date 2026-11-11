using System.Runtime.InteropServices.WindowsRuntime;
using MBox.Model;
using MBox.ViewModel;
using MBoxPanels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MBox.View
{
    public static class GamePicker
    {
        private static readonly string[] RomExtensions =
        {
            ".nes", ".gen", ".smd", ".smc", ".bin", ".gbc", ".gba", ".gb",
        };

        private static bool IsRomExtension(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            return RomExtensions.Any(r => string.Equals(r, ext, StringComparison.OrdinalIgnoreCase));
        }

        public static async Task<Rom?> PickAndAddAsync(ViewModelBase? vm = null)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
            };
            foreach (var ext in RomExtensions)
                picker.FileTypeFilter.Add(ext);

            InitializePickerWindow(picker);
            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return null;

            return await AddStorageFileAsync(file, vm);
        }

        public static async Task<IReadOnlyList<Rom>> PickMultipleAndAddAsync(ViewModelBase? vm = null)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
            };
            foreach (var ext in RomExtensions)
                picker.FileTypeFilter.Add(ext);
            picker.FileTypeFilter.Add(".7z");

            InitializePickerWindow(picker);
            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0)
                return Array.Empty<Rom>();

            var added = new List<Rom>();
            SetLongOperation(vm, isActive: true, progress: 0);

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    if (string.Equals(file.FileType, ".7z", StringComparison.OrdinalIgnoreCase))
                    {
                        var archiveAdded = await AddSevenZipAsync(file);
                        added.AddRange(archiveAdded);
                    }
                    else
                    {
                        var rom = await AddStorageFileAsync(file, null);
                        if (rom != null)
                            added.Add(rom);
                    }

                    SetLongOperation(vm, progress: (int)((i + 1) * 100.0 / files.Count));
                }
            }
            finally
            {
                SetLongOperation(vm, isActive: false);
            }

            return added;
        }

        private static async Task<Rom?> AddStorageFileAsync(Windows.Storage.StorageFile file, ViewModelBase? vm)
        {
            SetLongOperation(vm, isActive: true, progress: 0);

            try
            {
                var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
                byte[] bytes = buffer.ToArray();
                var library = ((App)Microsoft.UI.Xaml.Application.Current).Library;
                var rom = await library.AddAsync(file.Name, bytes);
                await library.SaveAsync();
                SetLongOperation(vm, progress: 100);
                return rom;
            }
            finally
            {
                SetLongOperation(vm, isActive: false);
            }
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
                if (result.Count > 0)
                    await library.SaveAsync();
            }
            catch
            {
                await AppHelpers.ShowMessageAsync(
                    AppHelpers.GetString("ArchiveCorruptedMessage", "Failed to extract archive."));
            }
            return result;
        }

        private static void SetLongOperation(ViewModelBase? vm, bool? isActive = null, int? progress = null)
        {
            if (vm == null)
                return;

            var type = vm.GetType();
            if (isActive.HasValue)
                type.GetProperty("IsLongOperationActive")?.SetValue(vm, isActive.Value);
            if (progress.HasValue)
                type.GetProperty("LongOperationProgress")?.SetValue(vm, progress.Value);
        }

        private static void InitializePickerWindow(FileOpenPicker picker)
        {
            if (App.MainWindow != null)
            {
                try
                {
                    IntPtr hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                    InitializeWithWindow.Initialize(picker, hwnd);
                }
                catch
                {
                    // Non-Win32 targets (macOS/Linux Skia) handle FileOpenPicker without HWND.
                }
            }
        }
    }
}
