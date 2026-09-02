# Envoie une vraie combinaison de touches, la fenetre demandee au premier plan.
# Outil de developpement uniquement : jamais utilise par l'application.
#
# De vraies frappes au niveau du systeme : RegisterHotKey ne repond qu'a
# celles-la. SendKeys poste dans la file de la fenetre et le raccourci global
# ne le voit jamais.
#
# Le releve des fenetres est laisse a list-windows.ps1, exprès : un script qui
# envoie des frappes ET enumere les fenetres est bloque par l'antivirus, et
# c'est une signature de journaliseur de frappes qu'il a raison de refuser.
#
# Exemple : build\frappe.ps1 -Combo "ctrl+r" -Fenetre "Principal"
param(
    [string]$Combo = "ctrl+p",
    [string]$Fenetre = "",
    [int]$AttenteMs = 1500
)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Clavier {
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@

$codes = @{
    'ctrl' = 0x11; 'shift' = 0x10; 'tab' = 0x09
    'p' = 0x50; 'r' = 0x52; 't' = 0x54; 'q' = 0x51
    '0' = 0x30; '1' = 0x31; '2' = 0x32; '3' = 0x33; '4' = 0x34; '5' = 0x35
}

$touches = @()
foreach ($part in $Combo.ToLower().Split('+')) {
    $p = $part.Trim()
    if (-not $codes.ContainsKey($p)) { Write-Output "TOUCHE INCONNUE : $p"; exit 1 }
    $touches += [byte]$codes[$p]
}

if ($Fenetre) {
    $cible = $null
    foreach ($p in @(Get-Process scrcpy, DtHub, notepad -ErrorAction SilentlyContinue)) {
        if ($p.MainWindowTitle -like "*$Fenetre*") { $cible = $p; break }
    }
    if (-not $cible) { Write-Output "FENETRE INTROUVABLE : $Fenetre"; exit 1 }
    [Clavier]::SetForegroundWindow($cible.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 700
    Write-Output ("premier plan : " + $cible.MainWindowTitle.Split([string[]]@("  ("), [StringSplitOptions]::None)[0])
}

foreach ($t in $touches) { [Clavier]::keybd_event($t, 0, 0, [IntPtr]::Zero) }
Start-Sleep -Milliseconds 60
for ($i = $touches.Count - 1; $i -ge 0; $i--) { [Clavier]::keybd_event($touches[$i], 0, 2, [IntPtr]::Zero) }

Write-Output "$Combo envoye"
Start-Sleep -Milliseconds $AttenteMs
