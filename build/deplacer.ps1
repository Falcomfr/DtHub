# Deplace une fenetre a une position absolue, pour verifier la persistance.
# Outil de developpement uniquement.
param([string]$Fenetre = "Guides", [int]$X = 0, [int]$Y = 0)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Bouge {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int m);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
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
[void][Bouge]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))
$poignee = [Bouge]::Find($Fenetre)
if ($poignee -eq [IntPtr]::Zero) { Write-Output "FENETRE INTROUVABLE"; exit 1 }
$r = New-Object Bouge+RECT
[void][Bouge]::GetWindowRect($poignee, [ref]$r)
[void][Bouge]::SetWindowPos($poignee, [IntPtr]::Zero, $X, $Y, ($r.R - $r.L), ($r.B - $r.T), 0x0004)
Start-Sleep -Milliseconds 400
[void][Bouge]::GetWindowRect($poignee, [ref]$r)
Write-Output ("DEPLACEE en " + $r.L + "," + $r.T + " taille " + ($r.R - $r.L) + "x" + ($r.B - $r.T))
