using DtHub.Core.Adb;

namespace DtHub.Core.Android;

/// <summary>Where Samsung's game optimizing service stands on a phone.</summary>
public enum GameOptimizingState
{
    Absent,
    On,
    Off,
}

/// <summary>
/// Samsung's game optimizing service (GOS). On a player's Galaxy S21 FE on
/// 2026-11-24 it drew the original game blurry on the main profile, outside
/// Android's game modes, while a renamed copy and the work profile stayed
/// sharp; disabling it made the original sharp too (D237).
/// </summary>
public static class SamsungGameOptimizing
{
    public const string Package = "com.samsung.android.game.gos";

    /// <summary>True for a Samsung, the only brand that ships it.</summary>
    public static bool Concerns(string? manufacturer) =>
        string.Equals(manufacturer, "samsung", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the state on the main profile, or <c>null</c> when the phone
    /// could not be asked: unknown is not absent.
    /// </summary>
    public static async Task<GameOptimizingState?> ReadAsync(
        IAdbClient adb,
        string serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adb);

        try
        {
            if (await ListsAsync(adb, serial, "-e", cancellationToken).ConfigureAwait(false))
            {
                return GameOptimizingState.On;
            }

            return await ListsAsync(adb, serial, "-d", cancellationToken).ConfigureAwait(false)
                ? GameOptimizingState.Off
                : GameOptimizingState.Absent;
        }
        catch (AdbException)
        {
            // Unknown, not absent: the caller shows nothing and logs nothing.
            return null;
        }
    }

    /// <summary>
    /// Turns it on or off, then reads the state back: what the command prints
    /// on Samsung is not known, and a silent refusal must show. A phone that
    /// cannot be reached throws, so the caller does not call it a refusal.
    /// </summary>
    public static async Task<GameOptimizingState?> SetAsync(
        IAdbClient adb,
        string serial,
        bool on,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adb);

        _ = await adb.ShellAsync(
            serial,
            on ? ["pm", "enable", Package] : ["pm", "disable-user", "--user", "0", Package],
            null,
            cancellationToken).ConfigureAwait(false);

        return await ReadAsync(adb, serial, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>"pm list packages" filters by substring: the name must match whole.</summary>
    private static async Task<bool> ListsAsync(
        IAdbClient adb,
        string serial,
        string filter,
        CancellationToken cancellationToken)
    {
        var output = await adb.ShellAsync(
            serial,
            ["pm", "list", "packages", filter, "--user", "0", Package],
            null,
            cancellationToken).ConfigureAwait(false);

        return PackageParser.ParsePackageList(output).Contains(Package, StringComparer.Ordinal);
    }
}
