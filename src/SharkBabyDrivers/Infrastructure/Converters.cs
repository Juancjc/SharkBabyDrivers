using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SharkBabyDrivers.Infrastructure;

/// <summary>IsChecked="{Binding Value, Converter={StaticResource Eq}, ConverterParameter=1000}" for segmented choices.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(Text(value), Text(parameter), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null) return Binding.DoNothing;
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (target.IsInstanceOfType(parameter)) return parameter;
        if (target.IsEnum) return Enum.Parse(target, parameter.ToString()!);
        return System.Convert.ChangeType(parameter.ToString(), target, CultureInfo.InvariantCulture)!;
    }

    private static string? Text(object? o) => o is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : o?.ToString();
}

public sealed class NotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Visible when the value is true / non-null / non-zero / non-empty (or the opposite with Invert).</summary>
public sealed class VisibleIfConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var on = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int i => i != 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter is string p && value is not null) on = string.Equals(value.ToString(), p, StringComparison.OrdinalIgnoreCase);
        return on ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value?.ToString() ?? "#000000")); }
        catch (FormatException) { return Brushes.Transparent; }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats a value with the parameter as format string, e.g. ConverterParameter='{}{0:0.00} mm'.</summary>
public sealed class FormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Format(CultureInfo.CurrentCulture, parameter?.ToString() ?? "{0}", value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
