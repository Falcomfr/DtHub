# Change le premier raccourci par l'editeur en envoyant une vraie frappe, puis
# releve le titre des fenetres de jeu. Outil de developpement uniquement.
param([string]$Frappe = "^a")

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Titres { (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\list-windows.ps1") | Sort-Object }

Write-Output "== avant =="
Titres | ForEach-Object { Write-Output "  $_" }

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Window)

$editeur = $null
foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
    if ($w.Current.Name -eq "Raccourcis") { $editeur = $w; break }
}
if (-not $editeur) { Write-Output "EDITEUR INTROUVABLE"; exit 1 }

$boutons = $editeur.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)))

$modifier = $null
foreach ($b in $boutons) { if ($b.Current.Name -eq "Modifier") { $modifier = $b; break } }
if (-not $modifier) { Write-Output "BOUTON MODIFIER INTROUVABLE"; exit 1 }

$modifier.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 600

$shell = New-Object -ComObject WScript.Shell
$null = $shell.AppActivate("Raccourcis")
Start-Sleep -Milliseconds 400
$shell.SendKeys($Frappe)
Start-Sleep -Seconds 3

Write-Output "== apres =="
Titres | ForEach-Object { Write-Output "  $_" }
