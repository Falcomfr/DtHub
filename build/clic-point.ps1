# Clique un point donne en coordonnees relatives au coin haut-gauche d'une fenetre.
# Outil de developpement uniquement.
param([string]$Fenetre = "Guides", [int]$X = 0, [int]$Y = 0, [switch]$Survol)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Clic {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int m);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static IntPtr Find(string titre) {
        IntPtr trouve = IntPtr.Zero;
        EnumWindows((h, l) => {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(512); GetWindowTextW(h, sb, sb.Capacity);
            if (sb.ToString() == titre) { trouve = h; return false; }
            return true;
        }, IntPtr.Zero);
        return trouve;
    }
}
"@
# Par ecran et non par systeme : l application est PerMonitorV2, et un
# outil qui ne l est pas mesure des coordonnees mises a l echelle.
[void][Clic]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))
$h = [Clic]::Find($Fenetre)
if ($h -eq [IntPtr]::Zero) { Write-Output "FENETRE INTROUVABLE"; exit 1 }
[Clic]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500
$r = New-Object Clic+RECT
[void][Clic]::GetWindowRect($h, [ref]$r)
$px = $r.L + $X
$py = $r.T + $Y
[void][Clic]::SetCursorPos($px, $py)
Start-Sleep -Milliseconds 300
if (-not $Survol) {
    [Clic]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [Clic]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
}
Write-Output ((&{ if ($Survol) { "SURVOL" } else { "CLIC" } }) + " en " + $px + "," + $py + " (fenetre " + $r.L + "," + $r.T + ")")
