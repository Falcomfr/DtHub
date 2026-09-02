# Capture une zone de l'ecran definie par rapport a une fenetre, en pixels
# physiques. Sert a voir ce qui se dessine hors de la fenetre : les infobulles.
# Outil de developpement uniquement.
param([string]$Fenetre = "Guides", [int]$X = 0, [int]$Y = 0, [int]$L = 800, [int]$H = 400,
      [string]$Output = "C:\Dev\DTHub\build\zone.png")
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Zone {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int m);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
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
[void][Zone]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))
$poignee = [Zone]::Find($Fenetre)
if ($poignee -eq [IntPtr]::Zero) { Write-Output "FENETRE INTROUVABLE"; exit 1 }
$r = New-Object Zone+RECT
[void][Zone]::GetWindowRect($poignee, [ref]$r)
$img = New-Object System.Drawing.Bitmap($L, $H)
$g = [System.Drawing.Graphics]::FromImage($img)
$g.CopyFromScreen(($r.L + $X), ($r.T + $Y), 0, 0, $img.Size)
$img.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output ("OK depuis " + ($r.L + $X) + "," + ($r.T + $Y) + " -> " + $Output)
