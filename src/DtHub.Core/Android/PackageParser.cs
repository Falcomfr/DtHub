using System.Text.RegularExpressions;

namespace DtHub.Core.Android;

/// <summary>
/// Reading the output of the Android package manager. Pure
/// functions, checkable against recorded output.
/// </summary>
public static partial class PackageParser
{
    /// <summary>
    /// Reads <c>pm list packages</c>. Handles the simple form
    /// <c>package:name</c> and the detailed form
    /// <c>package:/path/base.apk=name</c>.
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

            // With -f, the APK path precedes the name, separated by "=".
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
    /// Reads the launchable components reported by
    /// <c>cmd package query-activities</c> or
    /// <c>resolve-activity</c>. The format varies across Android
    /// versions, so tokens that have the shape of a component are
    /// looked for rather than following a layout.
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

    /// <summary>
    /// Reads a <c>package/activity</c> token, or returns <c>null</c>.
    /// </summary>
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

        // The abbreviated form ".Activity" designates a class from
        // the package.
        if (className.StartsWith('.'))
        {
            className = package + className;
        }

        return IsClassName(className) ? new AppComponent(package, className) : null;
    }

    /// <summary>
    /// True if the string has the shape of an Android package name.
    /// </summary>
    public static bool IsPackageName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && PackageName().IsMatch(value);

    private static bool IsClassName(string value) => ClassName().IsMatch(value);

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z0-9_]+)+$")]
    private static partial Regex PackageName();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_$]*(\.[a-zA-Z0-9_$]+)*$")]
    private static partial Regex ClassName();
}
