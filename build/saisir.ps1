# Ecrit un texte dans le champ de recherche d'une fenetre, sans clavier.
# Outil de developpement uniquement.
param([string]$Fenetre = "Quêtes", [string]$Texte = "")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cw = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $Fenetre)
$w = $root.FindFirst('Children', $cw)
if (-not $w) { Write-Output "FENETRE INTROUVABLE"; exit 1 }
$ce = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$e = $w.FindFirst('Descendants', $ce)
if (-not $e) { Write-Output "CHAMP INTROUVABLE"; exit 1 }
$v = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$v.SetValue($Texte)
Start-Sleep -Milliseconds 1200
Write-Output ("SAISI : '" + $Texte + "'")
