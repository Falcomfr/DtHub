# Verifie ou va une fenetre quand on change la taille, selon sa position.
# Outil de developpement uniquement.
param([string]$Title = "Principal", [int]$Percent = 90)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public class Q {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int m);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rr, B; }
  public static IntPtr Find(string t) {
    IntPtr f = IntPtr.Zero;
    EnumWindows((h,l) => { if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowTextW(h, sb, sb.Capacity);
      if (sb.ToString().StartsWith("DT Hub ") && sb.ToString().Contains(t)) { f = h; return false; }
      return true; }, IntPtr.Zero);
    return f; }
  public static string Rect(IntPtr h) {
    RECT r; GetWindowRect(h, out r);
    return string.Format("{0},{1} {2}x{3}", r.L, r.T, r.Rr - r.L, r.B - r.T); }
}
"@
[void][Q]::SetProcessDPIAware()

$h = [Q]::Find($Title)
if ($h -eq [IntPtr]::Zero) { "fenetre introuvable"; exit 1 }

$root = [System.Windows.Automation.AutomationElement]::RootElement
$window = $root.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "DT Hub")))
if (-not $window) { "configurateur introuvable"; exit 1 }

$slider = $window.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Slider)))
if (-not $slider) { "curseur introuvable"; exit 1 }

$range = $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)

foreach ($spot in @(
    @{ Name = "haut-gauche"; X = 0;    Y = 0 },
    @{ Name = "bas-droit";   X = 2304; Y = 1180 },
    @{ Name = "centre";      X = 1152; Y = 590 })) {

    [void][Q]::SetWindowPos($h, [IntPtr]::Zero, $spot.X, $spot.Y, 1536, 908, 0x0014)
    Start-Sleep -Milliseconds 1500

    $before = [Q]::Rect($h)
    $range.SetValue($Percent)
    Start-Sleep -Seconds 3
    $after = [Q]::Rect($h)

    "{0,-12} avant {1,-22} apres {2}" -f $spot.Name, $before, $after

    $range.SetValue(60)
    Start-Sleep -Seconds 2
}
