param([string]$Name)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root=[Windows.Automation.AutomationElement]::RootElement
$cond=New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty,'DT Hub')
$win=$root.FindFirst([Windows.Automation.TreeScope]::Children,$cond)
if(-not $win){ 'fenetre introuvable'; exit 1 }
if($Name -eq '--list'){
  $all=$win.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
  foreach($e in $all){ $n=$e.Current.Name; if($n){ "$($e.Current.ControlType.ProgrammaticName -replace 'ControlType\.','') : [$n]" } }
  exit 0
}
$c2=New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty,$Name)
$el=$win.FindFirst([Windows.Automation.TreeScope]::Descendants,$c2)
if(-not $el){ "introuvable: $Name"; exit 1 }
try{ $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
catch{ $el.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() }
"clic: $Name"
