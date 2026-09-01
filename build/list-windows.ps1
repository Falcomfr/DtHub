# Liste les fenetres visibles d'un processus, avec leur position et leur taille.
# Outil de developpement uniquement.
param([string]$ProcessName = "scrcpy")

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class WinList {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder s, int max);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static List<string> Of(HashSet<uint> pids) {
        var found = new List<string>();
        EnumWindows((h, l) => {
            if (!IsWindowVisible(h)) return true;
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (!pids.Contains(pid)) return true;
            var sb = new StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            if (sb.Length == 0) return true;
            RECT r; GetWindowRect(h, out r);
            found.Add(string.Format("{0} | {1},{2} | {3}x{4}", sb.ToString(), r.L, r.T, r.R - r.L, r.B - r.T));
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@

# Par ecran et non par systeme : l application est PerMonitorV2, et un
# outil qui ne l est pas mesure des coordonnees mises a l echelle.
[void][WinList]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))

$pids = New-Object 'System.Collections.Generic.HashSet[uint32]'
foreach ($p in Get-Process -Name $ProcessName -ErrorAction SilentlyContinue) { [void]$pids.Add([uint32]$p.Id) }

if ($pids.Count -eq 0) { Write-Output "AUCUN PROCESSUS"; exit 1 }

foreach ($line in [WinList]::Of($pids)) { Write-Output $line }
