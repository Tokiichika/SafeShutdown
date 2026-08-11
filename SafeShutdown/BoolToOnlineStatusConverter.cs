using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SafeShutdown;

public sealed class BoolToOnlineStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            true => "在线",
            false => "离线",
            _ => "未检测"
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush OnlineBrush = new(Color.FromRgb(22, 163, 74));
    private static readonly SolidColorBrush OfflineBrush = new(Color.FromRgb(220, 38, 38));
    private static readonly SolidColorBrush UnknownBrush = new(Color.FromRgb(100, 116, 139));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            true => OnlineBrush,
            false => OfflineBrush,
            _ => UnknownBrush
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class PasswordMaskConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var password = value as string;
        return string.IsNullOrEmpty(password) ? "未设置" : new string('•', Math.Min(password.Length, 8));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
