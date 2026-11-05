using System.Diagnostics;
using System.Globalization;

namespace DtHub.Mac.Platform;

/// <summary>
/// What a run that ended badly leaves behind on the Mac, besides scrcpy's
/// windows (closed by the PC's <c>OrphanProcesses</c>): "adb ... shell"
/// commands whose parent is gone, a scrcpy server's or a query that hung
/// while the phone stopped answering. Only DT Hub's own adb, only shells
/// whose parent has died (re-parented to launchd): the adb server itself,
/// which holds the phone's Wi-Fi link, is never touched.
/// </summary>
internal static class MacOrphans
{
    public static int KillShellsFrom(string adbPath)
    {
        var full = Path.GetFullPath(adbPath);
        var killed = 0;

        foreach (var (pid, parent, command) in Processes())
        {
            if (parent != 1
                || !command.StartsWith(full + " ", StringComparison.Ordinal)
                || !command.Contains(" shell", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                killed++;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                                  or System.ComponentModel.Win32Exception)
            {
                // Gone in the meantime, or not ours to stop: nothing left to
                // clean then.
                _ = exception;
            }
        }

        return killed;
    }

    /// <summary>Every process with its parent and command line, from ps.</summary>
    private static List<(int Pid, int Parent, string Command)> Processes()
    {
        var start = new ProcessStartInfo("/bin/ps")
        {
            ArgumentList = { "-axww", "-o", "pid=,ppid=,command=" },
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var ps = Process.Start(start);

        if (ps is null)
        {
            return [];
        }

        List<(int, int, string)> found = [];

        while (ps.StandardOutput.ReadLine() is { } line)
        {
            var fields = line.TrimStart().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length == 3
                && int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent))
            {
                found.Add((pid, parent, fields[2].Trim()));
            }
        }

        ps.WaitForExit();
        return found;
    }
}
