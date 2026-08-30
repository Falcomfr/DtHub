# Sélectionne un onglet du configurateur par automatisation d'interface.
# Outil de developpement uniquement.
param([string]$Text = "Appareils", [string]$WindowTitle = "DT Hub")

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$window = $root.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $WindowTitle)))

if (-not $window) { "FENETRE INTROUVABLE"; exit 1 }

$element = $window.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Text)))

if (-not $element) { "ONGLET INTROUVABLE : $Text"; exit 1 }

$pattern = $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$pattern.Select()
"onglet $Text selectionne"
