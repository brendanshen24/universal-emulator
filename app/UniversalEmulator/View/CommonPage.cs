using MBox.Model;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MBox.View
{
    public class CommonPage : Page
    {
        private static bool _checkedRatePrompt;

        static CommonPage() => AppSettings.IncrementLaunchCountAndCheckRate();

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (!_checkedRatePrompt)
            {
                _checkedRatePrompt = true;
                await AppHelpers.ShowRateDialogIfNeededAsync();
            }
        }
    }
}
