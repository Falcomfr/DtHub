# Releve la position et la taille des fenetres de jeu autour d'un changement
# de reglage, pour voir si l'une bouge. Outil de developpement uniquement.
param([string]$Reglage = "Basse", [int]$Attente = 20)

function Rects {
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\list-windows.ps1"
    return ($out | Sort-Object)
}

$avant = Rects
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\select-tab.ps1" -Text "Fenêtres" | Out-Null
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\select-tab.ps1" -Text $Reglage | Out-Null
Start-Sleep -Seconds $Attente
$apres = Rects

Write-Output "== $Reglage =="
for ($i = 0; $i -lt [Math]::Max($avant.Count, $apres.Count); $i++) {
    $a = if ($i -lt $avant.Count) { $avant[$i] } else { "(absente)" }
    $b = if ($i -lt $apres.Count) { $apres[$i] } else { "(absente)" }
    $etat = if ($a -eq $b) { "identique" } else { "A CHANGE" }
    Write-Output "$etat"
    Write-Output "  avant : $a"
    Write-Output "  apres : $b"
}
