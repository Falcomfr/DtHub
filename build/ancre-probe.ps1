# Clique une case de la grille de replacement par son index (0 a 8) et releve
# le deplacement des fenetres. Outil de developpement uniquement.
param([string]$Coin = "haut gauche", [int]$Attente = 6)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Rects {
    (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\list-windows.ps1") | Sort-Object
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Window)

$window = $null
foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
    if ($w.Current.Name -like "*DT Hub*") { $window = $w; break }
}
if (-not $window) { Write-Output "FENETRE INTROUVABLE"; exit 1 }

$buttons = $window.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)))

# Les cases de la grille se reconnaissent a leur infobulle.
$cible = $null
foreach ($b in $buttons) {
    if ($b.Current.HelpText -like "*$Coin*") { $cible = $b; break }
}
if (-not $cible) {
    Write-Output "CASE INTROUVABLE. Infobulles vues :"
    foreach ($b in $buttons) { if ($b.Current.HelpText) { Write-Output " - $($b.Current.HelpText)" } }
    exit 1
}

Write-Output "case : $($cible.Current.HelpText)"

$avant = Rects
$cible.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds $Attente
$apres = Rects

for ($i = 0; $i -lt $avant.Count; $i++) {
    $etat = if ($avant[$i] -eq $apres[$i]) { "IMMOBILE" } else { "deplacee" }
    Write-Output "$etat"
    Write-Output "  avant : $($avant[$i])"
    Write-Output "  apres : $($apres[$i])"
}
