using System.Runtime.InteropServices;

namespace DtHub.Infrastructure.Processes;

/// <summary>
/// Rattache les processus lancés à la vie de l'application.
///
/// Sans cela, une fin anormale, une fermeture par le gestionnaire des tâches
/// ou un plantage laissent les fenêtres de scrcpy ouvertes. Au lancement
/// suivant elles ne sont pas reconnues, et de nouvelles s'ajoutent : on se
/// retrouve avec plusieurs jeux de fenêtres identiques.
///
/// Un objet de travail Windows avec l'option de terminaison à la fermeture
/// règle le cas à la racine : quand le dernier descripteur se ferme, ce qui
/// arrive même si le processus est tué, Windows arrête les enfants. Aucune
/// élévation n'est requise.
/// </summary>
internal static class ChildProcessJob
{
    private static readonly nint Handle = Create();

    /// <summary>
    /// Rattache un processus. L'échec n'est pas une erreur : on retombe alors
    /// sur le comportement d'avant, où la fermeture propre suffit.
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
