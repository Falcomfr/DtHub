# Redimensionne une fenetre de jeu a plusieurs tailles et capture la zone
# client de chacune. Outil de developpement uniquement.
param([string]$Title = "Principal", [string]$Sizes = "1000x700,1600x1000,2400x1200,2842x1900,3840x2088,1200x800")

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public class S {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int m);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static IntPtr Find(string t) {
    IntPtr f = IntPtr.Zero;
    EnumWindows((h,l) => { if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowTextW(h, sb, sb.Capacity);
      if (sb.ToString().Contains(t)) { f = h; return false; } return true; }, IntPtr.Zero);
    return f; }
}
"@
[void][S]::SetProcessDPIAware()
$h = [S]::Find($Title)
if ($h -eq [IntPtr]::Zero) { "INTROUVABLE: $Title"; exit 1 }

foreach ($s in $Sizes.Split(',')) {
  $p = $s.Split('x'); $w = [int]$p[0]; $ht = [int]$p[1]
  [void][S]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, $w, $ht, 0x0014)
  Start-Sleep -Seconds 4
  $wr = New-Object S+RECT; $cr = New-Object S+RECT
  [void][S]::GetWindowRect($h, [ref]$wr); [void][S]::GetClientRect($h, [ref]$cr)
  $ww = $wr.R - $wr.L; $wh = $wr.B - $wr.T
  $bmp = New-Object System.Drawing.Bitmap $ww, $wh
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [void][S]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc)
  $border = [int](($ww - $cr.R) / 2); $title = $wh - $cr.B - $border
  $crop = New-Object System.Drawing.Rectangle $border, $title, $cr.R, $cr.B
  $c = $bmp.Clone($crop, $bmp.PixelFormat)
  $out = "C:\Dev\DTHub\build\app-$s.png"
  $c.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
  $c.Dispose(); $g.Dispose(); $bmp.Dispose()
  "demande ${w}x${ht} -> fenetre ${ww}x${wh} client $($cr.R)x$($cr.B)"
}
