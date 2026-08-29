# Clique sur un bouton de l'application par automatisation d'interface.
# Outil de developpement uniquement : jamais utilise par l'application.
param(
    [Parameter(Mandatory = $true)][string]$Text,
    [string]$WindowTitle = "DT Hub"
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement

# Recherche souple : le titre exact varie, et une boite ouverte par une autre
# fenetre reste un enfant du bureau.
$windowCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Window)

$windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $windowCondition)

$window = $null
foreach ($w in $windows) {
    if ($w.Current.Name -like "*$WindowTitle*") { $window = $w; break }
}

# Une boite ouverte par une autre fenetre peut apparaitre sous son
# proprietaire plutot que sous le bureau.
if (-not $window) {
    foreach ($w in $windows) {
        $nested = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $windowCondition)
        foreach ($n in $nested) {
            if ($n.Current.Name -like "*$WindowTitle*") { $window = $n; break }
        }
        if ($window) { break }
    }
}

if (-not $window) {
    Write-Output "FENETRE INTROUVABLE. Titres vus :"
    foreach ($w in $windows) { if ($w.Current.Name) { Write-Output " - $($w.Current.Name)" } }
    exit 1
}

$buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)

$buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)

foreach ($b in $buttons) {
    if ($b.Current.Name -like "*$Text*") {
        $pattern = $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        Write-Output "CLIQUE : $($b.Current.Name)"
        exit 0
    }
}

Write-Output "BOUTON INTROUVABLE. Disponibles :"
foreach ($b in $buttons) { Write-Output " - $($b.Current.Name)" }
exit 1
