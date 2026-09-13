# Creates or refreshes the "DT Hub" desktop shortcut.
#
# The shortcut targets the launcher, not the binary. Targeting the binary
# directly guaranteed nothing: it dated from the last publish, not from
# the last edit, and you could play for hours on a stale version without
# noticing. The launcher republishes first, which costs one second when
# nothing has changed.
#
#   powershell -ExecutionPolicy Bypass -File build\create-shortcut.ps1
#
# If the desktop keeps showing the old icon, it is not this script:
# Windows keeps a copy of the icon in its own cache, indexed on the
# file's path. The content of assets\app.ico changed, the path did not,
# so the cache never saw anything go by. Measured: rewriting the
# shortcut is not enough, and neither is "ie4uinit.exe -show", even
# though the shell API was already returning the right icon. Only
# restarting Explorer won out:
#
#   Stop-Process -Name explorer -Force ; Start-Process explorer.exe
#
# Folder windows close, nothing else is touched.

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

# Minimised: the republish opens a console for a second, it should not
# come up over the game.
$shortcut.WindowStyle = 7

$shortcut.Description = 'Ouvrir plusieurs comptes DOFUS Touch, toujours à la dernière version'
$shortcut.Save()

Write-Output "Raccourci a jour : $link"
Write-Output ("  cible   : " + $shortcut.TargetPath)
Write-Output ("  dossier : " + $shortcut.WorkingDirectory)
