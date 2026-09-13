# Creates or refreshes the "DT Hub" desktop shortcut.
#
# The shortcut targets build\lanceur.exe, not the binary and no longer
# lancer.cmd. Targeting the binary guaranteed nothing: it dated from the
# last publish, not from the last edit, and you could play for hours on a
# stale version without noticing. Targeting the batch file fixed that and
# cost a console, because Windows has to open one to interpret a .cmd and
# the shortcut's "minimised" only decides how that window shows, not
# whether it exists. The launcher is a WinExe: it republishes with no
# window at all, which costs one second when nothing has changed.
#
# Publish it with:
#
#   dotnet publish build\lanceur\lanceur.csproj -c Release -r win-x64 `
#     --self-contained false -p:PublishSingleFile=true
#
# then copy the single file to build\lanceur.exe. Publishing straight into
# build\ does not work: MSBuild excludes the output folder from the
# sources, and build\ holds the launcher's own source.
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
    [string]$Target = (Join-Path $PSScriptRoot 'lanceur.exe'),
    [string]$Icon = (Join-Path $PSScriptRoot '..\assets\app.ico'),
    [string]$Name = 'DtHub'
)

if (-not (Test-Path $Target)) {
    Write-Error "Lanceur introuvable : $Target. Publiez build\lanceur\lanceur.csproj."
    exit 1
}

$link = Join-Path ([Environment]::GetFolderPath('Desktop')) "$Name.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($link)
$shortcut.TargetPath = (Resolve-Path $Target).Path
$shortcut.WorkingDirectory = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$shortcut.IconLocation = (Resolve-Path $Icon).Path

# Normal. The launcher shows nothing, and the application places its own
# window: there is no console left to hide.
$shortcut.WindowStyle = 1

$shortcut.Description = 'Ouvrir plusieurs comptes DOFUS Touch, toujours à la dernière version'
$shortcut.Save()

Write-Output "Raccourci a jour : $link"
Write-Output ("  cible   : " + $shortcut.TargetPath)
Write-Output ("  dossier : " + $shortcut.WorkingDirectory)
