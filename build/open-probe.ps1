# Capture une fenetre de jeu a plusieurs instants apres le lancement, pour
# verifier qu'une mise en page perimee finit par se rattraper.
# Outil de developpement uniquement.
param([string]$Title = "Principal", [string]$At = "5,10,17,24")

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public class O {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
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
[void][O]::SetProcessDPIAware()

$start = Get-Date
foreach ($s in $At.Split(',')) {
  $target = [int]$s
  do { Start-Sleep -Milliseconds 200 } while (((Get-Date) - $start).TotalSeconds -lt $target)

  $h = [O]::Find($Title)
  if ($h -eq [IntPtr]::Zero) { "t+$target : fenetre absente"; continue }
  $wr = New-Object O+RECT; $cr = New-Object O+RECT
  [void][O]::GetWindowRect($h, [ref]$wr); [void][O]::GetClientRect($h, [ref]$cr)
  $ww = $wr.R - $wr.L; $wh = $wr.B - $wr.T
  if ($ww -le 0 -or $cr.B -le 0) { "t+$target : fenetre vide"; continue }
  $bmp = New-Object System.Drawing.Bitmap $ww, $wh
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [void][O]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc)
  $border = [int](($ww - $cr.R) / 2); $title = $wh - $cr.B - $border
  $crop = New-Object System.Drawing.Rectangle $border, $title, $cr.R, $cr.B
  $c = $bmp.Clone($crop, $bmp.PixelFormat)
  $c.Save("C:\Dev\DTHub\build\open-t$target.png", [System.Drawing.Imaging.ImageFormat]::Png)
  $c.Dispose(); $g.Dispose(); $bmp.Dispose()
  "t+$target : client $($cr.R)x$($cr.B)"
}
