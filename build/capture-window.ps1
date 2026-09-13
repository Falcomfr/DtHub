# Captures a window of the application into a PNG.
# Development tool only: never used by the application.
#
# PrintWindow captures the content even if the window is not in the foreground.
# The process declares itself DPI aware: without that, it measures the
# window too small and the capture ends up cropped.
param(
    [int]$ProcessId = 0,
    [string]$ProcessName = "DtHub",
    [string]$Output = (Join-Path $PSScriptRoot "capture.png"),
    [string]$WindowTitle = ""
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class Win {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder s, int max);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    // Every visible window of a process, not just the main one.
    public static List<KeyValuePair<IntPtr, string>> WindowsOf(uint targetPid) {
        var found = new List<KeyValuePair<IntPtr, string>>();
        EnumWindows((h, l) => {
            if (!IsWindowVisible(h)) return true;
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != targetPid) return true;
            var sb = new StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Length > 0) found.Add(new KeyValuePair<IntPtr, string>(h, title));
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@

# Per monitor and not per system: the application is PerMonitorV2, and a
# tool that is not paints the window into an oversized canvas.
[void][Win]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))

# A process ID lets you target a specific window when several
# instances of the same program are running.
$proc = if ($ProcessId -gt 0) {
    Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
} else {
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
}
if (-not $proc) { Write-Output "PROCESSUS INTROUVABLE"; exit 1 }

$windows = [Win]::WindowsOf([uint32]$proc.Id)
if ($windows.Count -eq 0) { Write-Output "AUCUNE FENETRE"; exit 1 }

if ($WindowTitle) {
    $match = $windows | Where-Object { $_.Value -like "*$WindowTitle*" } | Select-Object -First 1
} else {
    $match = $windows | Select-Object -First 1
}

if (-not $match) {
    Write-Output "TITRE INTROUVABLE. Fenetres visibles :"
    foreach ($w in $windows) { Write-Output " - $($w.Value)" }
    exit 1
}

$handle = $match.Key
$rect = New-Object Win+RECT
[void][Win]::GetWindowRect($handle, [ref]$rect)
$w = $rect.R - $rect.L
$h = $rect.B - $rect.T
if ($w -le 0 -or $h -le 0) { Write-Output "FENETRE VIDE"; exit 1 }

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# 2 = PW_RENDERFULLCONTENT, necessary for composited windows like WPF.
[void][Win]::PrintWindow($handle, $hdc, 2)
$g.ReleaseHdc($hdc)
$bmp.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

Write-Output "OK [$($match.Value)] $w x $h -> $Output"
