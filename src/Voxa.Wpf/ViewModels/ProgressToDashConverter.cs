using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Voxa.Wpf.ViewModels;

/// <summary>
/// 0-100 → StrokeDashArray of a progress ring (an Ellipse). Dash lengths are in stroke thicknesses, so the parameter
/// is the circumference of the stroke's center line divided by StrokeThickness: π · (Width − StrokeThickness) / StrokeThickness.
/// </summary>
public sealed class ProgressToDashConverter : IValueConverter
{
    public static ProgressToDashConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        double circumference = double.Parse((string)parameter!, CultureInfo.InvariantCulture);
        double fraction = Math.Clamp(value is double progress ? progress / 100 : 0, 0, 1);
        return new DoubleCollection { fraction * circumference, circumference };
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
