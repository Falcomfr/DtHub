using System.Runtime.InteropServices;

namespace DtHub.App.Services;

/// <summary>
/// The PC's processor counters, for the lag finding: a PC at full load
/// decodes the streams late, and that looks like the phone lagging.
/// </summary>
internal static class PcProcessor
{
    /// <summary>
    /// Idle time and total time since Windows started, in 100 ns units.
    /// Returns false if Windows refuses to answer.
    /// </summary>
    public static bool Times(out long idle, out long total)
    {
        // Kernel time includes idle time: their sum with user time is the
        // total.
        if (!GetSystemTimes(out idle, out var kernel, out var user))
        {
            total = 0;
            return false;
        }

        total = kernel + user;
        return true;
    }

    // DllImport and not LibraryImport, for the reason given in DarkTitleBar.
    // A FILETIME is two 32-bit halves, low first: it reads as one long.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}
