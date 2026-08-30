# Releve la geometrie des fenetres de jeu a intervalle regulier.
# Outil de developpement uniquement.
param([int]$Seconds = 30, [int]$IntervalMs = 500)

Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int m);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rr, B; }
  public static List<string> All() {
    var found = new List<string>();
    EnumWindows((h,l) => { if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowTextW(h, sb, sb.Capacity);
      var title = sb.ToString();
      if (title.StartsWith("DT Hub ") && title.Contains("Ctrl")) {
        RECT r; GetWindowRect(h, out r);
        var name = title.Substring(7); var cut = name.IndexOf("  (");
        if (cut > 0) name = name.Substring(0, cut);
        found.Add(string.Format("{0}={1},{2} {3}x{4}", name, r.L, r.T, r.Rr - r.L, r.B - r.T)); }
      return true; }, IntPtr.Zero);
    found.Sort();
    return found; }
}
"@
[void][W]::SetProcessDPIAware()

$end = (Get-Date).AddSeconds($Seconds)
$previous = ""

while ((Get-Date) -lt $end) {
    $line = [string]::Join("  |  ", [W]::All())
    if ($line -ne $previous) {
        "{0:HH:mm:ss.fff}  {1}" -f (Get-Date), $line
        $previous = $line
    }
    Start-Sleep -Milliseconds $IntervalMs
}
