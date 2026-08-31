# Pose ou met à jour le raccourci « DT Hub » du bureau.
#
# Le raccourci vise le lanceur, non le binaire. Viser le binaire directement ne
# garantissait rien : il datait de la dernière publication, pas de la dernière
# modification, et on pouvait jouer des heures sur une version périmée sans
# s'en douter. Le lanceur republie d'abord, ce qui coûte une seconde quand rien
# n'a changé.
#
#   powershell -ExecutionPolicy Bypass -File build\create-shortcut.ps1
#
# Si le bureau continue d'afficher l'ancienne icône, ce n'est pas ce script :
# Windows garde une copie de l'icône dans son propre cache, indexée sur le
# chemin du fichier. Le contenu de assets\app.ico a changé, le chemin non, donc
# le cache n'a rien vu passer. Mesuré : réécrire le raccourci ne suffit pas, et
# « ie4uinit.exe -show » non plus, alors que l'API du shell rendait déjà la
# bonne icône. Seul le redémarrage de l'explorateur l'a emporté :
#
#   Stop-Process -Name explorer -Force ; Start-Process explorer.exe
#
# Les fenêtres de dossiers se ferment, rien d'autre n'est touché.

param(
    [string]$Target = (Join-Path $PSScriptRoot 'lancer.cmd'),
    [string]$Icon = (Join-Path $PSScriptRoot '..\assets\app.ico'),
    [string]$Name = 'DtHub'
)

if (-not (Test-Path $Target)) {
    Write-Error "Lanceur introuvable : $Target."
    exit 1
}

$link = Join-Path ([Environment]::GetFolderPath('Desktop')) "$Name.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($link)
$shortcut.TargetPath = (Resolve-Path $Target).Path
$shortcut.WorkingDirectory = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$shortcut.IconLocation = (Resolve-Path $Icon).Path

# Réduite : la republication ouvre une console une seconde, elle n'a pas à
# passer devant le jeu.
$shortcut.WindowStyle = 7

$shortcut.Description = 'Ouvrir plusieurs comptes DOFUS Touch, toujours à la dernière version'
$shortcut.Save()

Write-Output "Raccourci a jour : $link"
Write-Output ("  cible   : " + $shortcut.TargetPath)
Write-Output ("  dossier : " + $shortcut.WorkingDirectory)
