// The game library: library.json plus one folder per game (named by the ROM's MD5) holding the ROM,
// its _thumb.png, save.bin and the <guid>_save.png screenshot. Same files and layout as the original's LocalState.
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using Windows.Storage;

namespace MBox.Model
{
    [DataContract]
    public class Library
    {
        public const string FileName = "library.json";
        public const string BackupName = "library.json.bak";
        public const string DefaultUri = "ms-appx:///Assets/Roms/library.json";
        public const string StateName = "save.bin";
        public const string ThumbSuffix = "_thumb.png";
        public const string SaveSuffix = "_save.png";

        [DataMember] public List<Rom> AllRoms { get; set; } = new();
        [DataMember] public List<string> RecentlyAdded { get; set; } = new();
        [DataMember] public List<string> RecentlyPlayed { get; set; } = new();
        [DataMember] public List<string> Saves { get; set; } = new();

        private static readonly DataContractJsonSerializer Serializer = new(typeof(Library));

        public static string Folder => ApplicationData.Current.LocalFolder.Path;

        public static string GameFolder(string md5) => System.IO.Path.Combine(Folder, md5);

        /// <summary>Loads library.json, falling back to library.json.bak, then to the bundled default library.</summary>
        public static async Task<Library> LoadAsync()
        {
            foreach (var name in new[] { FileName, BackupName })
            {
                try
                {
                    using var stream = File.OpenRead(System.IO.Path.Combine(Folder, name));
                    if (Serializer.ReadObject(stream) is Library library) return library;
                }
                catch { }
            }
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(DefaultUri));
            using (var stream = await file.OpenStreamForReadAsync())
            {
                var library = (Library)Serializer.ReadObject(stream)!;
                await library.SaveAsync();
                return library;
            }
        }

        /// <summary>Writes library.json, keeping the previous version as library.json.bak.</summary>
        public Task SaveAsync()
        {
            Directory.CreateDirectory(Folder);
            string path = System.IO.Path.Combine(Folder, FileName);
            if (File.Exists(path)) File.Copy(path, System.IO.Path.Combine(Folder, BackupName), true);
            using (var stream = File.Create(path)) Serializer.WriteObject(stream, this);
            return Task.CompletedTask;
        }

        public Rom? Find(string? md5) => AllRoms.FirstOrDefault(r => r.MD5 == md5);

        /// <summary>The original names a game by its file name up to the first dot ("Mr. Mary 2.nes" -> "Mr").</summary>
        public static string NameOf(string fileName) => fileName.Split('.')[0];

        public static string Md5Of(byte[] data) => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

        /// <summary>Adds a game (or returns the existing one with the same MD5), copying the ROM into its folder.</summary>
        public async Task<Rom> AddAsync(string fileName, byte[] data)
        {
            string md5 = Md5Of(data);
            var rom = Find(md5);
            if (rom == null)
            {
                Directory.CreateDirectory(GameFolder(md5));
                await File.WriteAllBytesAsync(System.IO.Path.Combine(GameFolder(md5), fileName), data);
                rom = new Rom { MD5 = md5, Name = NameOf(fileName), Path = fileName, ImagePath = "", SavePath = "" };
                AllRoms.Add(rom);
            }
            RecentlyAdded.Remove(md5);
            RecentlyAdded.Insert(0, md5);
            await SaveAsync();
            return rom;
        }

        public async Task<byte[]> ReadRomAsync(Rom rom)
        {
            if (rom.Path!.StartsWith("ms-appx:"))
            {
                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(rom.Path));
                using var stream = await file.OpenStreamForReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                return memory.ToArray();
            }
            return await File.ReadAllBytesAsync(System.IO.Path.Combine(GameFolder(rom.MD5!), rom.Path));
        }

        public byte[]? ReadState(Rom rom)
        {
            string path = System.IO.Path.Combine(GameFolder(rom.MD5!), StateName);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        public async Task SaveStateAsync(Rom rom, byte[] stateBytes, byte[]? screenshotPng)
        {
            if (string.IsNullOrEmpty(rom.MD5) || stateBytes.Length == 0)
                return;

            var stored = Find(rom.MD5) ?? rom;
            string folder = GameFolder(stored.MD5!);
            Directory.CreateDirectory(folder);

            await File.WriteAllBytesAsync(System.IO.Path.Combine(folder, StateName), stateBytes);

            if (!string.IsNullOrEmpty(stored.SavePath))
                TryDelete(stored.SavePath);

            if (screenshotPng is { Length: > 0 })
            {
                string shotPath = System.IO.Path.Combine(folder, $"{Guid.NewGuid()}{SaveSuffix}");
                await File.WriteAllBytesAsync(shotPath, screenshotPng);
                stored.SavePath = shotPath;
                rom.SavePath = shotPath;
            }

            Saves.Remove(stored.MD5!);
            Saves.Insert(0, stored.MD5!);
            await SaveAsync();
        }

        public async Task EnsureThumbnailAsync(Rom rom, Func<Task<byte[]>> capturePngAsync)
        {
            if (string.IsNullOrEmpty(rom.MD5) || string.IsNullOrEmpty(rom.Path))
                return;

            var stored = Find(rom.MD5) ?? rom;
            if (!string.IsNullOrEmpty(stored.ImagePath) &&
                (stored.ImagePath.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase) || File.Exists(stored.ImagePath)))
            {
                return;
            }

            string fileName = stored.Path!.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase)
                ? stored.Path.Split('/').Last()
                : stored.Path;
            string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            string folder = GameFolder(stored.MD5!);
            Directory.CreateDirectory(folder);

            string thumbPath = System.IO.Path.Combine(folder, $"{stem}{ThumbSuffix}");
            if (!File.Exists(thumbPath))
            {
                byte[] png = await capturePngAsync();
                if (png.Length > 0)
                    await File.WriteAllBytesAsync(thumbPath, png);
            }

            if (File.Exists(thumbPath))
            {
                stored.ImagePath = thumbPath;
                rom.ImagePath = thumbPath;
                await SaveAsync();
            }
        }

        public async Task MarkPlayedAsync(Rom rom)
        {
            RecentlyPlayed.Remove(rom.MD5!);
            RecentlyPlayed.Insert(0, rom.MD5!);
            await SaveAsync();
        }

        public async Task DeleteAsync(IEnumerable<Rom> roms)
        {
            foreach (var rom in roms.ToList())
            {
                AllRoms.RemoveAll(r => r.MD5 == rom.MD5);
                RecentlyAdded.Remove(rom.MD5!);
                RecentlyPlayed.Remove(rom.MD5!);
                Saves.Remove(rom.MD5!);
                try { if (!rom.Path!.StartsWith("ms-appx:")) Directory.Delete(GameFolder(rom.MD5!), true); } catch { }
            }
            await SaveAsync();
        }

        public async Task DeleteSavesAsync(IEnumerable<Rom> roms)
        {
            foreach (var rom in roms.ToList())
            {
                var stored = Find(rom.MD5) ?? rom;
                Saves.Remove(stored.MD5!);
                TryDelete(System.IO.Path.Combine(GameFolder(stored.MD5!), StateName));
                if (!string.IsNullOrEmpty(stored.SavePath)) TryDelete(stored.SavePath);
                stored.SavePath = "";
            }
            await SaveAsync();
        }

        private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
    }
}
