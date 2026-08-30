# Affiche le panneau si le raccourci l'a laisse masque.
# Outil de developpement uniquement.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class K {
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
$dt = Get-Process DtHub -ErrorAction SilentlyContinue
if ($dt -and $dt.MainWindowHandle -ne 0) { Write-Output "deja affiche"; exit 0 }
$g = Get-Process scrcpy -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $g) { Write-Output "aucune fenetre de jeu"; exit 1 }
[K]::SetForegroundWindow($g.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800
[K]::keybd_event(0x11, 0, 0, [IntPtr]::Zero)
[K]::keybd_event(0x50, 0, 0, [IntPtr]::Zero)
[K]::keybd_event(0x50, 0, 2, [IntPtr]::Zero)
[K]::keybd_event(0x11, 0, 2, [IntPtr]::Zero)
Start-Sleep -Seconds 2
Write-Output "affiche"
