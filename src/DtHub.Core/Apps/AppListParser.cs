using System.Text.RegularExpressions;

namespace DtHub.Core.Apps;

/// <summary>
/// Lecture des sorties liées aux applications : liste des paquets, activités
/// lançables, et liste nommée produite par scrcpy. Fonctions pures, donc
/// vérifiables sur des sorties enregistrées.
/// </summary>
public static partial class AppListParser
{
    /// <summary>
    /// Lit <c>pm list packages</c>. Gère la forme simple <c>package:nom</c> et
    /// la forme détaillée <c>package:/chemin/base.apk=nom</c>.
    /// </summary>
    public static IReadOnlyList<string> ParsePackageList(string? output)
    {
        var packages = new List<string>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return packages;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("package:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line["package:".Length..].Trim();

            // Avec -f, le chemin de l'APK précède le nom, séparé par « = ».
            var separator = value.LastIndexOf('=');
            if (separator >= 0)
            {
                value = value[(separator + 1)..];
            }

            if (IsPackageName(value))
            {
                packages.Add(value);
            }
        }

        return [.. packages.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Lit les composants lançables rapportés par
    /// <c>cmd package query-activities</c> ou <c>resolve-activity</c>. Le
    /// format varie selon les versions d'Android, on cherche donc les jetons
    /// qui ont la forme d'un composant plutôt que de suivre une mise en page.
    /// </summary>
    public static IReadOnlyList<AppComponent> ParseComponents(string? output)
    {
        var components = new List<AppComponent>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return components;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            foreach (var token in rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseComponent(token) is { } component)
                {
                    components.Add(component);
                }
            }
        }

        return [.. components.DistinctBy(c => c.Value, StringComparer.Ordinal)];
    }

    /// <summary>Lit un jeton <c>paquet/activité</c>, ou rend <c>null</c>.</summary>
    public static AppComponent? TryParseComponent(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var value = token.Trim().TrimEnd(',', ';', '}', ')');

        var separator = value.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            return null;
        }

        var package = value[..separator];
        var className = value[(separator + 1)..];

        if (!IsPackageName(package) || className.Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        // La forme abrégée « .Activité » désigne une classe du paquet.
        if (className.StartsWith('.'))
        {
            className = package + className;
        }

        return IsClassName(className) ? new AppComponent(package, className) : null;
    }

    /// <summary>
    /// Lit la sortie de <c>scrcpy --list-apps</c>, seule source pratique des
    /// vrais noms d'applications. Le format aligne le nom sur une colonne
    /// fixe, et rejette le paquet sur la ligne suivante lorsque le nom est
    /// trop long. Les deux cas sont gérés.
    /// </summary>
    public static IReadOnlyList<AndroidApp> ParseScrcpyAppList(string? output, int userId = 0)
    {
        var apps = new List<AndroidApp>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return apps;
        }

        string? pendingName = null;
        var pendingIsSystem = false;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();

            var isSystem = trimmed.StartsWith("* ", StringComparison.Ordinal);
            var isUser = trimmed.StartsWith("- ", StringComparison.Ordinal);

            if (isSystem || isUser)
            {
                // Une entrée précédente restée sans paquet est abandonnée
                // plutôt que rattachée à tort à celle-ci.
                var rest = trimmed[2..];
                var parts = ColumnSeparator().Split(rest.Trim())
                    .Where(part => part.Length > 0)
                    .ToArray();

                if (parts.Length >= 2 && IsPackageName(parts[^1]))
                {
                    apps.Add(Build(string.Join("  ", parts[..^1]), parts[^1], isSystem, userId));
                    pendingName = null;
                    continue;
                }

                pendingName = rest.Trim();
                pendingIsSystem = isSystem;
                continue;
            }

            if (pendingName is not null && IsPackageName(trimmed.Trim()))
            {
                apps.Add(Build(pendingName, trimmed.Trim(), pendingIsSystem, userId));
                pendingName = null;
            }
        }

        return [.. apps.DistinctBy(a => a.PackageName, StringComparer.Ordinal)];
    }

    private static AndroidApp Build(string label, string packageName, bool isSystem, int userId) => new()
    {
        PackageName = packageName,
        UserId = userId,
        Label = label.Length > 0 ? label : null,
        IsSystem = isSystem,
    };

    /// <summary>Vrai si la chaîne a la forme d'un nom de paquet Android.</summary>
    public static bool IsPackageName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && PackageName().IsMatch(value);

    private static bool IsClassName(string value) => ClassName().IsMatch(value);

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z0-9_]+)+$")]
    private static partial Regex PackageName();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_$]*(\.[a-zA-Z0-9_$]+)*$")]
    private static partial Regex ClassName();

    /// <summary>Deux espaces ou plus séparent les colonnes de scrcpy.</summary>
    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ColumnSeparator();
}
