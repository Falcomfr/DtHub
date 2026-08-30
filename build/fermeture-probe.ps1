# Ferme des fenetres comme le ferait un clic sur leur croix.
# Outil de developpement uniquement.
param([string]$Filtre = "DT Hub ")

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class F { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }
'@

foreach ($p in (Get-Process scrcpy -ErrorAction SilentlyContinue)) {
    if ($p.MainWindowTitle -like "*$Filtre*") {
        [F]::PostMessage($p.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Write-Output "fermeture demandee : $($p.MainWindowTitle)"
    }
}
