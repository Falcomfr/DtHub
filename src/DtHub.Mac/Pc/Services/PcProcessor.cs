using System.Runtime.InteropServices;

namespace DtHub.App.Services;

/// <summary>
/// The PC's PcProcessor (src/DtHub.App/Services/PcProcessor.cs) reads
/// Windows' GetSystemTimes, which the Mac has not: here the kernel's tick
/// counters, all cores, through Native/MacNative.swift. Same contract: idle
/// and total time, in a unit that only has to be the same between two
/// readings. On Linux, /proc/stat.
/// </summary>
internal static class PcProcessor
{
    public static bool Times(out long idle, out long total)
    {
        if (OperatingSystem.IsMacOS())
        {
            return dthub_cpu_ticks(out idle, out total);
        }

        return LinuxTimes(out idle, out total);
    }

    /// <summary>
    /// Linux: the first line of /proc/stat, all cores, in ticks; idle
    /// counts the time waiting for input/output too.
    /// </summary>
    private static bool LinuxTimes(out long idle, out long total)
    {
        idle = 0;
        total = 0;

        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault();
            var fields = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (fields is not { Length: > 5 } || fields[0] != "cpu")
            {
                return false;
            }

            var ticks = fields.Skip(1).Select(f => long.Parse(f, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            idle = ticks[3] + ticks[4];
            total = ticks.Take(8).Sum();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            // Said to the caller as a failure: the PC's sampling then skips
            // this reading, as it does when GetSystemTimes fails.
            _ = exception;
            return false;
        }
    }

    [DllImport("libDtHubMac")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool dthub_cpu_ticks(out long idle, out long total);
}
