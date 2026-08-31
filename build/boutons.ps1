param([string]$Invoke = "")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = Get-Process DtHub -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "AUCUN"; exit 1 }
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cw = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$cb = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
foreach ($w in $root.FindAll('Children', $cw)) {
  Write-Output ("[FENETRE] " + $w.Current.Name)
  foreach ($b in $w.FindAll('Descendants', $cb)) {
    $t = $b.Current.HelpText
    Write-Output ("  bouton: '" + $b.Current.Name + "' aide: '" + $t + "'")
    if ($Invoke -and (($b.Current.Name -like "*$Invoke*") -or ($t -like "*$Invoke*"))) {
      $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      Write-Output "  -> INVOQUE"
      Start-Sleep -Milliseconds 1200
      exit 0
    }
  }
}
