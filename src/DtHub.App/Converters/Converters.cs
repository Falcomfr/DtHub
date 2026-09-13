using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DtHub.App.Converters;

/// <summary>True becomes Visible, false becomes Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>
/// Inverts a boolean. Used to grey out what should be greyed out
/// during a loading.
/// </summary>
/// <summary>
/// True if the value matches the name passed as parameter, and puts
/// that name back when the box is checked. Used for the radio buttons
/// of an enum, which a plain equality test could not make two-way.
/// </summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is string name && Enum.TryParse(targetType, name, out var parsed)
            ? parsed
            : Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>False becomes Visible: useful for empty states.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

/// <summary>
/// Resolves a resource key into a brush. View models name a theme
/// color without depending on WPF.
/// </summary>
public sealed class ResourceKeyToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string key && Application.Current?.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts a hexadecimal color into a brush.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex
            && ColorConverter.ConvertFromString(hex) is Color color)
        {
            return new SolidColorBrush(color);
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// True if the value equals the parameter. Used for filter buttons.
/// </summary>
/// <summary>
/// Loads an icon from the cache and keeps it.
///
/// Three things matter. "OnLoad" closes the file right away, or a new
/// extraction could no longer overwrite it. The image is frozen, so
/// it can be shared between rows and between threads. And the
/// decoding is cached by path, so ten instances of the same game only
/// decode a single image.
///
/// A missing path, an unreadable file or a damaged image return
/// <c>null</c>: the spot is held by the layout, and nothing is
/// displayed.
/// </summary>
public sealed class IconPathToImageConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage?> Cache = new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0)
        {
            return null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var known))
            {
                return known;
            }

            var image = Load(path);
            Cache[path] = image;

            return image;
        }
    }

    private static BitmapImage? Load(string path)
    {
        try
        {
            var image = new BitmapImage();

            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;

            // Double the space on screen: sharp up to two hundred
            // percent scaling, without decoding one hundred and
            // ninety-two pixels to show twenty.
            image.DecodePixelWidth = 40;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException
                      or NotSupportedException or UriFormatException)
        {
            // Silence is intentional: a converter that throws breaks
            // the binding and leaves the row empty without saying
            // anything. The icon is a comfort, its absence shows on
            // screen, and the file comes from our own cache.
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// True if the two values designate the same object. Used to check
/// the navigation entry matching the current page, which a plain
/// converter parameter cannot express.
/// </summary>
public sealed class SameInstanceConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is { Length: 2 } && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Label of a grid position.</summary>
public sealed class AnchorLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DtHub.Core.Windows.WindowAnchor anchor
            ? DtHub.Core.Windows.WindowAnchors.Describe(anchor)
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// True if the two values are equal. Used to check the grid cell that
/// matches the chosen position, which a converter parameter cannot
/// express.
/// </summary>
public sealed class SameValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is { Length: 2 } && Equals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Returns Visible if the collection or the string is empty.
/// </summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var empty = value switch
        {
            null => true,
            string text => string.IsNullOrWhiteSpace(text),

            // Binding a collection directly does not work: its
            // reference never changes, so the binding never
            // re-evaluates when an item is added to it. Its count is
            // bound instead, which does get notified.
            int count => count == 0,
            System.Collections.ICollection collection => collection.Count == 0,
            _ => false,
        };

        // The "inverse" parameter is used to show the opposite, when
        // there is something, without having to write a second
        // converter.
        var inverted = string.Equals(parameter as string, "inverse", StringComparison.OrdinalIgnoreCase);

        return empty != inverted ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Makes the element being dragged translucent, so it is clear it has
/// left its place for the duration of the drag.
/// </summary>
public sealed class DraggedOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? 0.45 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible only if every condition is true. Used where visibility
/// depends on both a view setting and the data's state, without
/// having to mix the two in the view model.
/// </summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is not null && values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible as soon as one of the conditions is true. Counterpart to
/// the previous one, for cases where two distinct reasons each
/// justify showing it.
/// </summary>
public sealed class AnyTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is not null && values.Any(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Makes an element visible without ever changing the layout. A
/// marker that appears and disappears would shift what surrounds it,
/// and therefore the center of the hovered element, and therefore the
/// marker itself.
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

