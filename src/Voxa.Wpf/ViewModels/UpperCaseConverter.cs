using System.Globalization;
using System.Windows.Data;

namespace Voxa.Wpf.ViewModels;

/// <summary>"en" → "EN", Local → "LOCAL": language badges and group headers.</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public static UpperCaseConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpperInvariant() ?? "";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
