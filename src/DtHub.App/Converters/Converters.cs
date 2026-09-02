using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
/// <summary>
/// Vrai si la valeur correspond au nom passé en paramètre, et repose ce nom
/// quand la case est cochée. Sert aux boutons radio d'une énumération, qu'un
/// simple test d'égalité ne saurait pas rendre bidirectionnel.
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
/// <summary>
/// Charge une icône du cache et la garde.
///
/// Trois choses comptent. « OnLoad » referme le fichier tout de suite, sans
/// quoi une nouvelle extraction ne pourrait plus le réécrire. L'image est
/// gelée, donc partageable entre lignes et entre fils. Et le décodage est
/// mémorisé par chemin, si bien que dix instances du même jeu ne décodent
/// qu'une seule image.
///
/// Un chemin absent, un fichier illisible ou une image abîmée rendent
/// <c>null</c> : la place est tenue par la mise en page, et il ne s'affiche
/// rien.
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

            // Le double de la place à l'écran : net jusqu'à deux cents pour
            // cent de mise à l'échelle, sans décoder cent quatre-vingt-douze
            // pixels pour en montrer vingt.
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

/// <summary>Libellé d'une position de la grille.</summary>
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
/// Vrai si les deux valeurs sont égales. Sert à cocher la case de la grille
/// qui correspond à la position retenue, ce qu'un paramètre de convertisseur
/// ne permet pas d'exprimer.
/// </summary>
public sealed class SameValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is { Length: 2 } && Equals(values[0], values[1]);

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

            // Lier directement une collection ne fonctionne pas : sa référence
            // ne change jamais, donc la liaison ne se réévalue pas quand on y
            // ajoute un élément. On lie son compteur, qui lui est notifié.
            int count => count == 0,
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

/// <summary>
/// Rend l'élément que l'on déplace translucide, pour qu'on voie qu'il a
/// quitté sa place le temps du glissé.
/// </summary>
public sealed class DraggedOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? 0.45 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible seulement si toutes les conditions sont vraies. Sert là où une
/// visibilité dépend à la fois d'un réglage de la vue et de l'état des
/// données, sans avoir à mélanger les deux dans le modèle de vue.
/// </summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is not null && values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible dès qu'une des conditions est vraie. Pendant du précédent, pour les
/// cas où deux raisons distinctes justifient chacune l'affichage.
/// </summary>
public sealed class AnyTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is not null && values.Any(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Rend un élément visible sans jamais changer la mise en page. Un repère qui
/// apparaît et disparaît déplacerait ce qui l'entoure, donc le milieu de
/// l'élément survolé, donc le repère lui-même.
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

