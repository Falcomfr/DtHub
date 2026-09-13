using System.Diagnostics;

namespace DtHub.Infrastructure.Processes;

/// <summary>
/// Cleans up processes left behind by a previous run.
///
/// An abnormal end of the application used to leave its mirroring
/// windows open. On the next launch they were not recognized, and
/// new ones kept being added: after a few incidents, the screen
/// would be covered in identical windows.
/// </summary>
public static class OrphanProcesses
{
    /// <summary>
    /// Stops the processes spawned from the given executable. Only
    /// ours is targeted, matched by full path: a copy of scrcpy
    /// installed by the user must never be touched.
    /// </summary>
    /// <returns>Number of processes stopped.</returns>
    public static int KillFrom(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return 0;
        }

        var name = Path.GetFileNameWithoutExtension(executablePath);
        var full = Path.GetFullPath(executablePath);
        var killed = 0;

        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                if (process.Id == Environment.ProcessId || !IsOurs(process, full))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                killed++;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception
                    or NotSupportedException)
            {
                // The process may have disappeared in the meantime, or
                // belong to another session. Nothing to report.
                _ = exception;
            }
            finally
            {
                process.Dispose();
            }
        }

        return killed;
    }

    private static bool IsOurs(Process process, string executablePath)
    {
        try
        {
            return string.Equals(
                process.MainModule?.FileName, executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The path of a process from another session is
            // unreadable. When in doubt, it is left untouched.
            return false;
        }
    }
}
