# Clique une ligne de la liste deroulante de la fenetre Quetes, par son texte.
# Outil de developpement uniquement.
param([string]$Texte = "Quêtes", [string]$Fenetre = "Quêtes")
Add-Type -AssemblyName System.Windows.Forms

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Window)

$w = $null
foreach ($x in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
    if ($x.Current.Name -eq $Fenetre) { $w = $x; break }
}
if (-not $w) { Write-Output "FENETRE INTROUVABLE"; exit 1 }

$items = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)))

foreach ($i in $items) {
    if ($i.Current.Name -like "*$Texte*") {
        $i.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 300
        # Le clic est simule par la touche Entree, que la fenetre traite.
        $i.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
        Write-Output "CHOISI : $($i.Current.Name)"
        exit 0
    }
}
Write-Output "LIGNE INTROUVABLE. Vues :"
foreach ($i in $items) { Write-Output " - $($i.Current.Name)" }
exit 1
