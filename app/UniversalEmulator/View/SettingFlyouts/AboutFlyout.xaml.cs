using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MBox.View.SettingFlyouts
{
    public sealed partial class AboutFlyout : UserControl
    {
        public AboutFlyout()
        {
            InitializeComponent();
        }

        private async void SupportLink_Click(object sender, RoutedEventArgs e)
        {
            await AppHelpers.SendFeedbackEmailAsync();
        }
    }
}
