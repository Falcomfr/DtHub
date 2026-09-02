# Fait defiler une fenetre en envoyant des crans de molette sur son centre.
# Outil de developpement uniquement.
param([string]$Fenetre = "Guides", [int]$Crans = -10, [int]$OffsetY = 0)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Molette {
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
[void][Molette]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))
$h = [Molette]::Find($Fenetre)
if ($h -eq [IntPtr]::Zero) { Write-Output "FENETRE INTROUVABLE"; exit 1 }
[Molette]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 400
$r = New-Object Molette+RECT
[void][Molette]::GetWindowRect($h, [ref]$r)
$x = [int](($r.L + $r.R) / 2)
$y = [int](($r.T + $r.B) / 2) + $OffsetY
[void][Molette]::SetCursorPos($x, $y)
Start-Sleep -Milliseconds 200
$pas = if ($Crans -lt 0) { -120 } else { 120 }
for ($i = 0; $i -lt [Math]::Abs($Crans); $i++) {
    [Molette]::mouse_event(0x0800, 0, 0, $pas, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
}
Write-Output ("DEFILE " + $Crans + " crans en " + $x + "," + $y)
