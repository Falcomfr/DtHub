# Pose un raccourci « DT Hub » sur le bureau, visant la publication en fichier
# unique. À rejouer après une nouvelle publication seulement si le chemin
# change : le raccourci suit le fichier, pas sa version.
#
#   dotnet publish src/DtHub.App -c Release -r win-x64 --self-contained true `
#     -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
#     -p:IncludeNativeLibrariesForSelfExtract=true -o build\publish
#   powershell -ExecutionPolicy Bypass -File build\create-shortcut.ps1

param(
    [string]$Target = (Join-Path $PSScriptRoot 'publish\DtHub.exe'),
    [string]$Icon = (Join-Path $PSScriptRoot '..\assets\app.ico')
)

if (-not (Test-Path $Target)) {
    Write-Error "Exécutable introuvable : $Target. Publiez d'abord."
    exit 1
}

$link = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DT Hub.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($link)
$shortcut.TargetPath = (Resolve-Path $Target).Path
$shortcut.WorkingDirectory = Split-Path (Resolve-Path $Target).Path
$shortcut.IconLocation = (Resolve-Path $Icon).Path
$shortcut.Description = 'Ouvrir plusieurs comptes DOFUS Touch'
$shortcut.Save()

Write-Output "Raccourci créé : $link"
