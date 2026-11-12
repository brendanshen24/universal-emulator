// MBox.App, the original's entry point (AppxManifest EntryPoint="MBox.App").
using MBox.Model;
using MBox.View;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MBox;

public partial class App : Application
{
    public App()
    {
        FixMacBundleResourcePathBase();
        this.InitializeComponent();
    }

    private static void FixMacBundleResourcePathBase()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        // When launched via Finder/LaunchServices as a single-file .app bundle,
        // Uno's macOS host points StorageFile.ResourcePathBase to "<extractDir>/Resources",
        // whereas extracted assets live directly inside AppContext.BaseDirectory.
        var prop = typeof(Windows.Storage.StorageFile).GetProperty(
            "ResourcePathBase",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (prop?.GetValue(null) is string resourceBase &&
            !string.IsNullOrEmpty(resourceBase) &&
            !Directory.Exists(resourceBase))
        {
            string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.Exists(Path.Combine(baseDir, "Assets")))
                prop.SetValue(null, baseDir);
        }
    }

    public static Window? MainWindow { get; private set; }

    /// <summary>The game library (the original kept it on the App object too).</summary>
    public Library Library { get; private set; } = new();

    public static new App Current => (App)Application.Current;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window { Title = (string)Resources["AppName"] };
        Library = await Library.LoadAsync();

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            MainWindow.Content = rootFrame;
            rootFrame.NavigationFailed += OnNavigationFailed;
        }
        if (rootFrame.Content == null) rootFrame.Navigate(typeof(StartPage), args.Arguments);
        MainWindow.Activate();
#if DEBUG
        if (Environment.GetEnvironmentVariable("UE_SMOKE_LOG") is { } smokeLog)
            await EmulatorPage.RunSmokeTestAsync(rootFrame, smokeLog);
#endif
    }

    void OnNavigationFailed(object sender, NavigationFailedEventArgs e) =>
        throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");

    public static void InitializeLogging() { }
}
