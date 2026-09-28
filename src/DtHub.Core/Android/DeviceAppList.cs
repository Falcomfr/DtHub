using System.Text.RegularExpressions;

namespace DtHub.Core.Android;

/// <summary>An application as the phone names it.</summary>
/// <param name="Label">The name shown under its icon on the phone.</param>
/// <param name="PackageName">Its Android package.</param>
/// <param name="IsSystem">True for an application shipped with the phone.</param>
public readonly record struct DeviceApp(string Label, string PackageName, bool IsSystem);

/// <summary>
/// Reads the output of <c>scrcpy --list-apps</c>. A pure function,
/// checkable against recorded output.
///
/// It is the only source of application names at hand: the package
/// manager's shell commands give packages and activities, never the
/// label a person recognises. scrcpy asks the phone's package manager
/// from inside, where the labels are.
/// </summary>
public static partial class DeviceAppList
{
    /// <summary>
    /// The applications listed, in the order scrcpy gives them.
    ///
    /// The layout is read in scrcpy v4.1, <c>LogUtils.buildAppListMessage</c>:
    /// one application per line, <c>" * "</c> for a system one and
    /// <c>" - "</c> for the others, the name padded to thirty columns,
    /// then the package. A name of thirty characters or more cannot be
    /// padded, and the package then goes to the next line on its own.
    /// Both shapes are read; any other line is ignored.
    /// </summary>
    public static IReadOnlyList<DeviceApp> Parse(string? output)
    {
        List<DeviceApp> apps = [];

        if (string.IsNullOrWhiteSpace(output))
        {
            return apps;
        }

        // A long name waiting for the package on the next line.
        (string Label, bool IsSystem)? pending = null;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (pending is { } waiting)
            {
                pending = null;
                var alone = line.Trim();

                if (PackageParser.IsPackageName(alone) && line.StartsWith(' '))
                {
                    apps.Add(new DeviceApp(waiting.Label, alone, waiting.IsSystem));
                    continue;
                }
            }

            var match = AppLine().Match(line);

            if (!match.Success)
            {
                continue;
            }

            var isSystem = match.Groups["mark"].Value == "*";
            var rest = match.Groups["rest"].Value.TrimEnd();

            // At least two spaces separate the name from the package:
            // the padding brings one, the separator another. A single
            // space belongs to the name itself.
            var split = Separator().Match(rest);

            if (split.Success
                && PackageParser.IsPackageName(rest[(split.Index + split.Length)..]))
            {
                apps.Add(new DeviceApp(
                    rest[..split.Index].Trim(),
                    rest[(split.Index + split.Length)..],
                    isSystem));
            }
            else if (rest.Length > 0)
            {
                pending = (rest.Trim(), isSystem);
            }
        }

        return [.. apps.DistinctBy(a => a.PackageName, StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"^ (?<mark>[*-]) (?<rest>.+)$")]
    private static partial Regex AppLine();

    // The last run of two spaces or more: a name may itself hold a
    // double space, the package never does.
    [GeneratedRegex(@"\s{2,}(?=\S+$)")]
    private static partial Regex Separator();
}
