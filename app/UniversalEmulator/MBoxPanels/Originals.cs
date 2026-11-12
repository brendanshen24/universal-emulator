// Access to the original Store binaries (NesboxPanels.dll, GameController.dll) on Windows.
// MBoxPanels.Original.dll and its dependencies sit in the "Original" folder next to the exe and are loaded into
// their own AssemblyLoadContext (they bring the Windows SDK projections, which the Uno app must not see).
// Everywhere else, or if anything is missing, the app falls back to its rebuilt core (mboxpanels / libmboxpanels).
// Set UE_REBUILT_CORE=1 to force the rebuilt core on Windows.
using System.Reflection;
using System.Runtime.Loader;
using MBoxPanels.Contracts;

namespace MBoxPanels
{
    internal static class Originals
    {
        private static IOriginalComponents? _instance;
        private static bool _tried;

        public static IOriginalComponents? Instance
        {
            get
            {
                if (!_tried)
                {
                    _tried = true;
                    _instance = TryLoad();
                }
                return _instance;
            }
        }

        private static IOriginalComponents? TryLoad()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("UE_REBUILT_CORE") == "1")
                return null;

            // Next to the exe (a single-file publish runs from a temp extraction folder, so not AppContext.BaseDirectory).
            string? dir = new[] { Path.GetDirectoryName(Environment.ProcessPath), AppContext.BaseDirectory }
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => Path.Combine(d!, "Original"))
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "MBoxPanels.Original.dll")));
            if (dir == null)
                return null;
            string path = Path.Combine(dir, "MBoxPanels.Original.dll");

            try
            {
                var assembly = new OriginalLoadContext(dir).LoadFromAssemblyPath(path);
                var type = assembly.GetType("MBoxPanels.Original.OriginalComponents", throwOnError: true)!;
                return (IOriginalComponents)Activator.CreateInstance(type)!;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Original components unavailable, using rebuilt core: {e}");
                return null;
            }
        }

        private sealed class OriginalLoadContext(string dir) : AssemblyLoadContext("Original")
        {
            protected override Assembly? Load(AssemblyName name)
            {
                if (name.Name == typeof(IOriginalComponents).Assembly.GetName().Name)
                    return null; // shared with the app, so the interfaces are the same types
                string candidate = Path.Combine(dir, name.Name + ".dll");
                return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
            }
        }
    }
}
