using System.Runtime.InteropServices;

namespace DtHub.App.Services;

/// <summary>
/// The PC's PcProcessor (src/DtHub.App/Services/PcProcessor.cs) reads
/// Windows' GetSystemTimes, which the Mac has not: here the kernel's tick
/// counters, all cores, through Native/MacNative.swift. Same contract: idle
/// and total time, in a unit that only has to be the same between two
/// readings.
/// </summary>
internal static class PcProcessor
{
    public static bool Times(out long idle, out long total) => dthub_cpu_ticks(out idle, out total);

    [DllImport("libDtHubMac")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool dthub_cpu_ticks(out long idle, out long total);
}
