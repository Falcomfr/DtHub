using System.Globalization;

using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// What the device says about its free space.
///
/// Lack of space is a documented cause of DOFUS Touch crashes, and it
/// is misleading: the game does not say it is short on space, it
/// just closes.
///
/// **Honestly, this is insurance more than a need.** The reference
/// device has close to three hundred gigabytes free, and the warning
/// will never appear there. It costs one read and one analysis, and
/// it earns its keep the day someone plays on a full phone.
/// </summary>
/// <param name="FreeBytes">Free bytes on the data partition.</param>
public sealed record StorageReading(long FreeBytes)
{
    /// <summary>Below this, the game can fail without saying why.</summary>
    public const long Low = 2L * 1024 * 1024 * 1024;

    /// <summary>Below this, it will fail.</summary>
    public const long Critical = 512L * 1024 * 1024;

    /// <summary>Free space in gigabytes, rounded to one decimal.</summary>
    public double FreeGigabytes => Math.Round(FreeBytes / (1024.0 * 1024 * 1024), 1);

    /// <summary>True when the space is worth mentioning.</summary>
    public bool IsLow => FreeBytes <= Low;

    /// <summary>
    /// Reads the output of <c>df /data</c>. Returns <c>null</c> as
    /// soon as the column is missing: knowing nothing is an ordinary
    /// case.
    ///
    /// Recorded on the reference device, in one-kilobyte blocks:
    ///
    /// <code>
    /// Filesystem       1K-blocks      Used Available Use% Mounted on
    /// /dev/block/dm-59 485636064 171563720 313535476  36% /data/user/0
    /// </code>
    ///
    /// The column is looked up by its heading and not by its rank:
    /// the volume name can contain a space, and counting columns
    /// from the left would shift everything. So we read the header
    /// to know where to look, counting **from the right**, since the
    /// end of the lines is regular.
    /// </summary>
    public static StorageReading? Parse(string? df)
    {
        if (string.IsNullOrWhiteSpace(df))
        {
            return null;
        }

        var lines = df.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 2)
        {
            return null;
        }

        var header = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var column = Array.FindIndex(header, h => h.Equals("Available", StringComparison.OrdinalIgnoreCase));

        if (column < 0)
        {
            return null;
        }

        // From the right: "Mounted on" counts as two words in the
        // header and as one path in the line, and the volume name
        // can contain a space. The rank from the end, however, is
        // stable.
        var fromEnd = header.Length - column;

        foreach (var line in lines[1..])
        {
            var cells = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (cells.Length < fromEnd)
            {
                continue;
            }

            // The header counts "Mounted on" as two words, the line
            // as a single path: one notch of gap, always the same.
            var at = cells.Length - fromEnd + 1;

            if (at >= 0
                && at < cells.Length
                && long.TryParse(cells[at], NumberStyles.None, CultureInfo.InvariantCulture, out var blocks))
            {
                return new StorageReading(blocks * 1024L);
            }
        }

        return null;
    }

    /// <summary>
    /// What there is to say, or <c>null</c> when there is nothing to
    /// say.
    /// </summary>
    public string? Describe() => FreeBytes <= Critical
        ? Strings.Format("DeviceStorageCritical", FreeGigabytes)
        : IsLow
            ? Strings.Format("DeviceStorageLow", FreeGigabytes)
            : null;
}
