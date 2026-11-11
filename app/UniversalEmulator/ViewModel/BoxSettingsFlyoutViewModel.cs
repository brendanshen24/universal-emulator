using System.Runtime.InteropServices;

namespace MBox.ViewModel
{
    public sealed class BoxSettingsFlyoutViewModel : ViewModelBase
    {
        private bool _isKeyboardSettingsVisible =
            !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();

        public bool IsKeyboardSettingsVisible
        {
            get => _isKeyboardSettingsVisible;
            set => SetProperty(ref _isKeyboardSettingsVisible, value);
        }
    }
}
