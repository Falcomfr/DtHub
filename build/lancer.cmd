@echo off
chcp 65001 >nul
rem ---------------------------------------------------------------------------
rem Launches DT Hub after first making sure the binary is up to date.
rem
rem The desktop shortcut used to target build\publish\DtHub.exe directly.
rem Nothing guaranteed that this file matched the code: it dated from the
rem last publish, not from the last edit, and you could play for hours on
rem a stale version without noticing.
rem
rem The republish is incremental. Measured on this machine: 1.1 s when
rem nothing changed, 10.6 s after an edit. The price of certainty.
rem
rem No step is blocking: SDK missing, build failed, file locked by an
rem instance already open, it launches whatever is there anyway.
rem Better yesterday's version than no application at all.
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

rem The working directory must be a Windows path: launched from a UNC
rem path, the application freezes before its first log line.
start "" /d "%SORTIE%" "%BINAIRE%"
endlocal
