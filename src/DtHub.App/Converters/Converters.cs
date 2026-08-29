using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DtHub.App.Converters;

/// <summary>Vrai devient Visible, faux devient Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Inverse un booléen. Sert à griser ce qui doit l'être pendant un chargement.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>Faux devient Visible : utile pour les états vides.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

/// <summary>
/// Résout une clé de ressource en pinceau. Les vues-modèles nomment une
/// couleur du thème sans dépendre de WPF.
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

/// <summary>Convertit une couleur hexadécimale en pinceau.</summary>
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

/// <summary>Vrai si la valeur est égale au paramètre. Sert aux boutons de filtre.</summary>
public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Vrai si les deux valeurs désignent le même objet. Sert à cocher l'entrée de
/// navigation correspondant à la page courante, ce qu'un simple paramètre de
/// convertisseur ne permet pas d'exprimer.
/// </summary>
public sealed class SameInstanceConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is { Length: 2 } && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Rend Visible si la collection ou la chaîne est vide.</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var empty = value switch
        {
            null => true,
            string text => string.IsNullOrWhiteSpace(text),
            System.Collections.ICollection collection => collection.Count == 0,
            _ => false,
        };

        // Le paramètre « inverse » sert à afficher au contraire quand il y a
        // quelque chose, sans avoir à écrire un second convertisseur.
        var inverted = string.Equals(parameter as string, "inverse", StringComparison.OrdinalIgnoreCase);

        return empty != inverted ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
