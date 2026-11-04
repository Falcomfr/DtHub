using System.Runtime.InteropServices;
using System.Text;

namespace DtHub.Mac.Platform;

/// <summary>
/// The calls into Native/MacNative.swift (libDtHubMac.dylib). A window is a
/// CGWindowID, coordinates are points from the main screen's top left.
/// </summary>
internal static class MacNative
{
    private const string Library = "libDtHubMac";

    public static long[] WindowsOf(int processId)
    {
        var count = dthub_windows_of(processId, null, 0);

        if (count <= 0)
        {
            return [];
        }

        var ids = new long[count];
        count = dthub_windows_of(processId, ids, ids.Length);
        return ids[..Math.Min(count, ids.Length)];
    }

    public static (double X, double Y, double Width, double Height)? Frame(long window)
    {
        var frame = new double[4];
        return dthub_window_frame(window, frame) ? (frame[0], frame[1], frame[2], frame[3]) : null;
    }

    public static string? Title(long window)
    {
        var buffer = new byte[512];

        if (!dthub_window_title(window, buffer, buffer.Length))
        {
            return null;
        }

        var end = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
    }

    /// <summary>Frame then visible frame of each screen, the main one first.</summary>
    public static IReadOnlyList<(double[] Frame, double[] Visible)> Screens()
    {
        var values = new double[8 * 16];
        var count = Math.Min(dthub_screens(values, 16), 16);

        return [.. Enumerable.Range(0, count).Select(i => (values[(i * 8)..((i * 8) + 4)], values[((i * 8) + 4)..((i * 8) + 8)]))];
    }

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool dthub_ax_trusted([MarshalAs(UnmanagedType.I1)] bool prompt);

    [DllImport(Library)]
    public static extern long dthub_window_number(nint nsWindow);

    [DllImport(Library)]
    private static extern int dthub_windows_of(int pid, long[]? ids, int capacity);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool dthub_window_exists(long id);

    [DllImport(Library)]
    public static extern int dthub_window_pid(long id);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool dthub_window_frame(long id, double[] frame);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool dthub_window_title(long id, byte[] buffer, int capacity);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool dthub_move_window(long id, double x, double y, double width, double height);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool dthub_raise_window(long id, [MarshalAs(UnmanagedType.I1)] bool activate);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool dthub_close_window(long id);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool dthub_set_minimized(long id, [MarshalAs(UnmanagedType.I1)] bool minimized);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool dthub_is_minimized(long id);

    [DllImport(Library)]
    public static extern long dthub_front_window();

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool dthub_modifier_down();

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool dthub_shift_down();

    [DllImport(Library)]
    private static extern int dthub_screens(double[] values, int capacity);

    [DllImport(Library)]
    public static extern long dthub_front_most_of(long[] ids, int count);

    [DllImport(Library)]
    public static extern void dthub_accept_first_mouse(nint nsWindow);
}
