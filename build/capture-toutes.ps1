# Capture les fenetres de DT Hub une par une, pour comparer avant et apres une
# refonte visuelle. Outil de developpement uniquement.
param([string]$Dossier = "C:\Dev\DTHub\build\refonte\avant")

New-Item -ItemType Directory -Force -Path $Dossier | Out-Null

function Montre {
    # Le panneau ne repond au raccourci que depuis une fenetre de jeu.
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Kb {
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@ -ErrorAction SilentlyContinue
    $dt = Get-Process DtHub -ErrorAction SilentlyContinue
    if ($dt -and $dt.MainWindowHandle -ne 0) { return }
    $g = Get-Process scrcpy -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $g) { return }
    [Kb]::SetForegroundWindow($g.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 600
    [Kb]::keybd_event(0x11, 0, 0, [IntPtr]::Zero)
    [Kb]::keybd_event(0x50, 0, 0, [IntPtr]::Zero)
    [Kb]::keybd_event(0x50, 0, 2, [IntPtr]::Zero)
    [Kb]::keybd_event(0x11, 0, 2, [IntPtr]::Zero)
    Start-Sleep -Seconds 2
}

function Prends([string]$Titre, [string]$Nom, [string]$Proc = "DtHub") {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File "$PSScriptRoot\capture-window.ps1" `
        -ProcessName $Proc -WindowTitle $Titre -Output "$Dossier\$Nom.png" | Out-Null
    if (Test-Path "$Dossier\$Nom.png") { Write-Output "  $Nom" } else { Write-Output "  $Nom : ECHEC" }
}

function Onglet([string]$Texte) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\select-tab.ps1" -Text $Texte | Out-Null
    Start-Sleep -Milliseconds 700
}

function Bouton([string]$Texte, [string]$Fenetre = "DT Hub") {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\click-button.ps1" -Text $Texte -WindowTitle $Fenetre | Out-Null
    Start-Sleep -Seconds 2
}

Montre
Write-Output "Configurateur :"
Onglet "Raccourcis";  Prends "DT Hub" "configurateur-raccourcis"
Onglet "Fenêtres";    Prends "DT Hub" "configurateur-fenetres"
Onglet "Appareils";   Prends "DT Hub" "configurateur-appareils"

Write-Output "Fenetres filles :"
Bouton "Modifier les raccourcis"
Prends "Raccourcis" "editeur-raccourcis"
Bouton "Fermer" "Raccourcis"

Onglet "Appareils"
Bouton "Plusieurs comptes sur un même appareil ?"
Prends "Jouer à plusieurs comptes" "aide-comptes"
Bouton "Fermer" "Jouer à plusieurs comptes"

Bouton "Une fenêtre se fige au bout d'un moment ?"
Prends "Empêcher la mise en veille" "aide-veille"
Bouton "Fermer" "Empêcher la mise en veille"

Bouton "+  Associer un nouvel appareil"
Prends "appareil" "association" 
Bouton "Aide" "appareil"
Prends "Préparer" "aide-preparation"
