using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CodexBar.Core.Presentation;

namespace CodexBar.Windows;

/// <summary>
/// Value converters the flyout needs. Kept tiny and stateless so a XAML change cannot hide logic:
/// every rule that decides what to show lives in <c>CodexBar.Core</c>.
/// </summary>
internal sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
        {
            return Brushes.Transparent;
        }

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Provider marks ship as SVG path data so Core stays free of WPF types.</summary>
internal sealed class IconGeometryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string path && !string.IsNullOrWhiteSpace(path)
            ? Geometry.Parse(path)
            : Geometry.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal sealed class StatusBrushConverter : IValueConverter
{
    private static readonly Brush Ok = Frozen("#22C55E");
    private static readonly Brush Warning = Frozen("#F59E0B");
    private static readonly Brush Error = Frozen("#EF4444");
    private static readonly Brush Unknown = Frozen("#9CA3AF");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is FlyoutStatusKind kind
            ? kind switch
            {
                FlyoutStatusKind.Ok => Ok,
                FlyoutStatusKind.Warning => Warning,
                FlyoutStatusKind.Error => Error,
                _ => Unknown,
            }
            : Unknown;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Visible when the value has content; collapses null, blank strings, and empty lists.</summary>
internal sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        IsEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static bool IsEmpty(object? value) =>
        value is null
        || (value is string text && string.IsNullOrWhiteSpace(text))
        || (value is System.Collections.ICollection collection && collection.Count == 0);
}

/// <summary>Visible only when the value is missing; the empty-state counterpart.</summary>
internal sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        HasValueToVisibilityConverter.IsEmpty(value) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
