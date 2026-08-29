# Capture la fenêtre principale de DT Hub dans un PNG.
# Outil de développement uniquement : jamais utilisé par l'application.
# PrintWindow capture le contenu de la fenêtre même si elle n'est pas au
# premier plan, contrairement à une copie d'écran.
param(
    [string]$ProcessName = "DtHub",
    [string]$Output = "C:\Dev\DTHub\build\capture.png"
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
    // Sans cela, un processus non conscient de la mise a l'echelle recoit des
    // coordonnees virtualisees et la capture est rognee.
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

[void][Win]::SetProcessDPIAware()

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1

if (-not $proc) { Write-Output "AUCUNE FENETRE"; exit 1 }

$rect = New-Object Win+RECT
[void][Win]::GetWindowRect($proc.MainWindowHandle, [ref]$rect)
$w = $rect.R - $rect.L
$h = $rect.B - $rect.T
if ($w -le 0 -or $h -le 0) { Write-Output "FENETRE VIDE"; exit 1 }

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# 2 = PW_RENDERFULLCONTENT, nécessaire pour les fenêtres composées comme WPF.
[void][Win]::PrintWindow($proc.MainWindowHandle, $hdc, 2)
$g.ReleaseHdc($hdc)
$bmp.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

Write-Output "OK $w x $h -> $Output"
