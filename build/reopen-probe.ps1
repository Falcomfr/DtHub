# Mesure si une instance rouvre exactement où elle etait.
# Outil de developpement uniquement.
param([string]$Title = "Principal", [string]$WindowTitle = "DT Hub")

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public class R {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int m);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rr, B; }
  public static string Rect(string t) {
    string found = null;
    EnumWindows((h,l) => { if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowTextW(h, sb, sb.Capacity);
      if (sb.ToString().Contains(t) && sb.ToString().StartsWith("DT Hub ")) {
        RECT r; GetWindowRect(h, out r);
        found = string.Format("{0},{1} {2}x{3}", r.L, r.T, r.Rr - r.L, r.B - r.T);
        return false; }
      return true; }, IntPtr.Zero);
    return found; }
}
"@
[void][R]::SetProcessDPIAware()

function Invoke-ByName([string]$name, [int]$index = 0) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $window = $root.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $WindowTitle)))
    if (-not $window) { return $false }

    $all = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)))
    if ($all.Count -le $index) { return $false }

    $pattern = $all[$index].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    return $true
}

"avant fermeture : $([R]::Rect($Title))"

if (-not (Invoke-ByName "Fermer" 0)) { "bouton Fermer introuvable"; exit 1 }
Start-Sleep -Seconds 4
"apres fermeture : $([R]::Rect($Title))"

if (-not (Invoke-ByName "Lancer" 0)) { "bouton Lancer introuvable"; exit 1 }
Start-Sleep -Seconds 12
"juste apres ouverture : $([R]::Rect($Title))"
Start-Sleep -Seconds 4
"quatre secondes plus tard : $([R]::Rect($Title))"
