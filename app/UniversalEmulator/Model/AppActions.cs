using MBox.Model;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Resources;
using Windows.System;

namespace MBox
{
    internal static class AppHelpers
    {
        private static readonly Lazy<ResourceLoader?> Strings = new(() =>
        {
            try
            {
                return ResourceLoader.GetForViewIndependentUse();
            }
            catch
            {
                return null;
            }
        });

        public static string GetString(string resourceKey, string fallback = "")
        {
            try
            {
                var value = Strings.Value?.GetString(resourceKey);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
            catch
            {
                // ResourceLoader lookup can fail if key is missing.
            }
            return fallback;
        }

        public static async Task ShowMessageAsync(string message, string? title = null)
        {
            var xamlRoot = App.MainWindow?.Content?.XamlRoot;
            if (xamlRoot == null)
                return;

            var dialog = new ContentDialog
            {
                Title = title ?? "Universal Emulator",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = xamlRoot,
            };
            await dialog.ShowAsync();
        }

        public static async Task ShowRateDialogIfNeededAsync()
        {
            if (!AppSettings.IsShowRateNeed)
                return;

            var xamlRoot = App.MainWindow?.Content?.XamlRoot;
            if (xamlRoot == null)
                return;

            AppSettings.IsShowRateNeed = false;

            var message = GetString("RateAppMessage", "Enjoying Universal Emulator? Please rate it!");
            var rateText = GetString("RateButton", "Rate");
            var reportText = GetString("ReportProblemButton", "Report a problem");
            var cancelText = GetString("CancelButton", "Cancel");

            var dialog = new ContentDialog
            {
                Title = "Universal Emulator",
                Content = message,
                PrimaryButtonText = rateText,
                SecondaryButtonText = reportText,
                CloseButtonText = cancelText,
                XamlRoot = xamlRoot,
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await OpenReviewAsync();
            else if (result == ContentDialogResult.Secondary)
                await SendFeedbackEmailAsync();
        }

        public static async Task OpenReviewAsync()
        {
            try
            {
                string pfn = Package.Current.Id.FamilyName;
                await Launcher.LaunchUriAsync(new Uri($"ms-windows-store:REVIEW?PFN={pfn}"));
            }
            catch
            {
                // Ignore if Store protocol is unavailable on desktop/macOS.
            }
        }

        public static async Task SendFeedbackEmailAsync()
        {
            try
            {
                string subject = Uri.EscapeDataString(GetString("EmailTitleProblemReport", "Universal Emulator problem report"));
                string body = Uri.EscapeDataString(GetString("EmailBodyProblemReport", ""));
                await Launcher.LaunchUriAsync(new Uri($"mailto:support@nesbox.com?subject={subject}&body={body}"));
            }
            catch
            {
                // Ignore if no mail client is registered.
            }
        }
    }
}
