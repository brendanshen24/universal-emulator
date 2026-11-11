// The original app's helper library (MSACL.UI / MSACL.UI.UWP), merged into Nesbox.dll at compile time.
// Only the parts the app uses, with the same type and member names.
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace MSACL.UI
{
    [System.Runtime.Serialization.DataContract]
    public abstract class BindableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException("execute");
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute == null ? null : _ => canExecute()) { }

        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

namespace MSACL.UI.Converters
{
    public class BoolToValueConverter<T> : IValueConverter
    {
        public T? TrueValue { get; set; }
        public T? FalseValue { get; set; }

        public object? Convert(object value, Type targetType, object parameter, string language) =>
            value is true ? TrueValue : FalseValue;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            Equals(value, TrueValue);
    }

    public class BoolToVisibilityConverter : BoolToValueConverter<Microsoft.UI.Xaml.Visibility> { }

    public class BoolToBrushConverter : BoolToValueConverter<Brush> { }
}
