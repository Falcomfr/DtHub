using System.Runtime.InteropServices;

namespace DtHub.Infrastructure.Processes;

/// <summary>
/// Ties launched processes to the application's lifetime.
///
/// Without this, an abnormal end, a closure from Task Manager, or
/// a crash leave scrcpy's windows open. On the next launch they
/// are not recognized, and new ones are added: several identical
/// sets of windows pile up.
///
/// A Windows job object with the kill-on-close option fixes this
/// at the root: when the last handle closes, which happens even if
/// the process is killed, Windows stops the children. No elevation
/// is required.
/// </summary>
internal static class ChildProcessJob
{
    private static readonly nint Handle = Create();

    /// <summary>
    /// Ties in a process. Failure is not an error: it then falls
    /// back to the previous behavior, where a clean shutdown is
    /// enough.
    /// </summary>
    public static void Adopt(nint process)
    {
        if (Handle != 0 && process != 0)
        {
            _ = AssignProcessToJobObject(Handle, process);
        }
    }

    private static nint Create()
    {
        var job = CreateJobObjectW(0, null);

        if (job == 0)
        {
            return 0;
        }

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = LimitKillOnJobClose,
            },
        };

        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(limits, buffer, fDeleteOld: false);

            if (!SetInformationJobObject(job, ExtendedLimitInformation, buffer, (uint)size))
            {
                _ = CloseHandle(job);
                return 0;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return job;
    }

    private const int ExtendedLimitInformation = 9;
    private const uint LimitKillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint job, int infoClass, nint info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
