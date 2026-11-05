using System.Globalization;

using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DtHub.Mac.Converters;

// The PC's converters (src/DtHub.App/Converters/Converters.cs), same names
// and same answers. Where WPF answers with a Visibility, Avalonia shows and
// hides through IsVisible, a bool: the "visibility" converters give that.

/// <summary>True shows, false hides.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true;
}

/// <summary>False shows: useful for empty states.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>
/// True if the value matches the name passed as parameter, and puts
/// that name back when the box is checked. Used for the radio buttons
/// of an enum, which a plain equality test could not make two-way.
/// </summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // An unchecked radio says nothing: only the one being checked
        // writes, or the two would fight over the setting.
        return value is true && parameter is string name && type.IsEnum && Enum.TryParse(type, name, out var parsed)
            ? parsed
            : BindingOperations.DoNothing;
    }
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>
/// Resolves a resource key into a brush. View models name a theme
/// colour without depending on the interface toolkit.
/// </summary>
public sealed class ResourceKeyToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key
            && Application.Current is { } application
            && application.TryGetResource(key, application.ActualThemeVariant, out var found)
            && found is IBrush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts a hexadecimal colour into a brush.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex && Color.TryParse(hex, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Gray;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Loads an icon from the cache and keeps it.
///
/// The file is read whole and closed at once, or a new extraction could
/// no longer overwrite it; and the decoding is cached by path, so ten
/// instances of the same game only decode a single image.
///
/// A missing path, an unreadable file or a damaged image return
/// <c>null</c>: the spot is held by the layout, and nothing is
/// displayed.
/// </summary>
public sealed class IconPathToImageConverter : IValueConverter
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

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

    private static Bitmap? Load(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));

            // Double the space on screen: sharp on a Retina screen,
            // without decoding a hundred and ninety-two pixels to show
            // twenty.
            return Bitmap.DecodeToWidth(stream, 40);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Silence is intentional: a converter that throws breaks
            // the binding and leaves the row empty without saying
            // anything. The icon is a comfort, its absence shows on
            // screen, and the file comes from our own cache.
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// The shared code names its pictures as the PC's resource paths
/// ("/assets/dtmap.png"); on the Mac they are this assembly's resources,
/// under the same paths.
/// </summary>
public sealed class AssetToImageConverter : IValueConverter
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0)
        {
            return null;
        }

        lock (Cache)
        {
            if (!Cache.TryGetValue(path, out var image))
            {
                image = Load(path);
                Cache[path] = image;
            }

            return image;
        }
    }

    private static Bitmap? Load(string path)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://DtHub.Mac/{path.TrimStart('/')}"));
            return new Bitmap(stream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Same silence as the icons: a tile without its picture still
            // says its name.
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True if the value equals the parameter. Used for filter buttons.</summary>
public sealed class EqualityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// The ring of an account's colour: shown on the swatch whose colour,
/// given as parameter, is the one the account wears. The PC compares the
/// swatch's Tag in a template trigger, which Avalonia has no counterpart
/// for.
/// </summary>
public sealed class EqualToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value, parameter) ? 1.0 : 0.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True if the two values designate the same object.</summary>
public sealed class SameInstanceConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values is { Count: 2 } && ReferenceEquals(values[0], values[1]);
}

/// <summary>Label of a grid position.</summary>
public sealed class AnchorLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DtHub.Core.Windows.WindowAnchor anchor
            ? DtHub.Core.Windows.WindowAnchors.Describe(anchor)
            : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True if the two values are equal.</summary>
public sealed class SameValueConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values is { Count: 2 } && Equals(values[0], values[1]);
}

/// <summary>
/// Shows when the collection or the string is empty; with the "inverse"
/// parameter, when there is something.
/// </summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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

        var inverted = string.Equals(parameter as string, "inverse", StringComparison.OrdinalIgnoreCase);

        return empty != inverted;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Makes the element being dragged translucent, so it is clear it has
/// left its place for the duration of the drag.
/// </summary>
public sealed class DraggedOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 0.45 : 1.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Shows only if every condition is true: a view setting and the data's
/// state, without mixing the two in the view model.
/// </summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values is not null && values.All(v => v is true);
}

/// <summary>Shows as soon as one of the conditions is true.</summary>
public sealed class AnyTrueToVisibilityConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values is not null && values.Any(v => v is true);
}

/// <summary>
/// Makes an element visible without ever changing the layout. A
/// marker that appears and disappears would shift what surrounds it,
/// and therefore the centre of the hovered element, and therefore the
/// marker itself.
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// The PC's language list names the system's own language "Windows": on
/// the Mac, that choice is macOS, and on Linux, Linux.
/// </summary>
public sealed class SystemNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is "Windows" ? (OperatingSystem.IsMacOS() ? "macOS" : "Linux") : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
