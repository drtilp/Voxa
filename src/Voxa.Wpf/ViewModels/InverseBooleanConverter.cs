using System.Globalization;
using System.Windows.Data;

namespace Voxa.Wpf.ViewModels;

/// <summary>true → false: e.g. disable a button while something runs.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public static InverseBooleanConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
