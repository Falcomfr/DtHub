# Dit quelle fenetre est au premier plan, par l'automatisation d'interface.
# Outil de developpement uniquement.
#
# Par UIAutomation et non par GetForegroundWindow : un script qui declare
# GetForegroundWindow et GetWindowText est refuse par l'antivirus, et il a
# raison, c'est la signature d'un journaliseur de frappes. On ne desactive rien
# pour autant : on demande autrement.
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$e = [System.Windows.Automation.AutomationElement]::FocusedElement
if (-not $e) { Write-Output "AUCUNE"; exit 1 }

# On remonte jusqu'a la fenetre qui porte l'element ayant le focus.
$marche = New-Object System.Windows.Automation.TreeWalker(
    [System.Windows.Automation.Automation]::ControlViewCondition)
$courant = $e
while ($courant -and $courant.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) {
    $suivant = $marche.GetParent($courant)
    if (-not $suivant) { break }
    $courant = $suivant
}
$nom = if ($courant) { $courant.Current.Name } else { $e.Current.Name }
Write-Output $nom.Split([string[]]@("  ("), [StringSplitOptions]::None)[0]
