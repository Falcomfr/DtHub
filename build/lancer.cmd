@echo off
chcp 65001 >nul
rem ---------------------------------------------------------------------------
rem Lance DT Hub en s'assurant d'abord que le binaire est à jour.
rem
rem Le raccourci du bureau visait directement build\publish\DtHub.exe. Rien ne
rem garantissait que ce fichier corresponde au code : il datait de la dernière
rem publication, pas de la dernière modification, et on pouvait jouer des heures
rem sur une version périmée sans s'en douter.
rem
rem La republication est incrémentale. Mesuré sur ce poste : 1,1 s quand rien
rem n'a changé, 10,6 s après une modification. Le prix d'une certitude.
rem
rem Aucune étape n'est bloquante : SDK absent, compilation en échec, fichier
rem verrouillé par une instance déjà ouverte, on lance quand même ce qui est là.
rem Mieux vaut une version d'hier que pas d'application du tout.
rem ---------------------------------------------------------------------------

setlocal
cd /d "%~dp0.."

set "SORTIE=%CD%\build\publish"
set "BINAIRE=%SORTIE%\DtHub.exe"
set "JOURNAL=%CD%\build\publication.log"

where dotnet >nul 2>&1
if errorlevel 1 goto lancer

dotnet publish src\DtHub.App -p:PublishProfile=win-x64 ^
  -o "%SORTIE%" --nologo -v q >"%JOURNAL%" 2>&1

:lancer
if not exist "%BINAIRE%" (
  echo DT Hub n'a pas pu etre publie et aucun binaire n'existe.
  echo Detail : "%JOURNAL%"
  pause
  exit /b 1
)

rem Le repertoire de travail doit etre un chemin Windows : lance depuis un
rem chemin UNC, l'application se fige avant sa premiere ligne de journal.
start "" /d "%SORTIE%" "%BINAIRE%"
endlocal
