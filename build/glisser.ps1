# Glisse le pointeur d'un point a un autre, bouton gauche enfonce, en
# coordonnees absolues de l'ecran (pixels physiques).
# Outil de developpement uniquement : jamais utilise par l'application.
#
# Le mouvement est fait par petits pas : un saut unique reste sous le seuil de
# glissement de Windows, et le geste passe pour un simple clic.
param([int]$X = 0, [int]$Y = 0, [int]$VersX = 0, [int]$VersY = 0, [int]$Pas = 25)

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class Glisse {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
}
"@

[Glisse]::SetProcessDPIAware() | Out-Null

[Glisse]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 300
[Glisse]::mouse_event(0x02, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 200

for ($i = 1; $i -le $Pas; $i++) {
    $px = [int]($X + (($VersX - $X) * $i / $Pas))
    $py = [int]($Y + (($VersY - $Y) * $i / $Pas))
    [Glisse]::SetCursorPos($px, $py) | Out-Null
    Start-Sleep -Milliseconds 40
}

Start-Sleep -Milliseconds 300
[Glisse]::mouse_event(0x04, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 300

Write-Output "GLISSE $X,$Y -> $VersX,$VersY"
