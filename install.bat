@echo off
setlocal enabledelayedexpansion

echo ==============================================================================
echo   NEXT : XLR v0.1.0-alpha - Setup ^& Installer
echo   (c) 2026 plysystems. Alle Rechte vorbehalten.
echo ==============================================================================
echo.
echo LIZENZBEDINGUNGEN:
echo  - Erlaubt: Private Nutzung, Kommerzielle Nutzung (Streaming, YouTube, Twitch),
echo             Firmeninterne Nutzung, Quellcode-Aenderungen, Weiterverteilung.
echo  - Pflicht: Copyright-Hinweis (plysystems) und Lizenztext beibehalten.
echo.

set /p ACCEPT="Akzeptieren Sie die plysystems Lizenzbedingungen? (J/N): "
if /i not "%ACCEPT%"=="J" (
    echo Installation abgebrochen.
    pause
    exit /b 1
)

echo.
echo [1/4] Suche Anwendungsdateien...
set "TARGET_DIR=%LOCALAPPDATA%\Programs\NextXLR"
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

if exist "%~dp0apps\windows\bin\Debug\net8.0-windows10.0.19041.0\win-x64\NextXLR.exe" (
    set "SRC_DIR=%~dp0apps\windows\bin\Debug\net8.0-windows10.0.19041.0\win-x64"
) else if exist "%~dp0dist\app\NextXLR.exe" (
    set "SRC_DIR=%~dp0dist\app"
) else (
    set "SRC_DIR=%~dp0"
)

echo [2/4] Kopiere Dateien nach %TARGET_DIR%...
robocopy "%SRC_DIR%" "%TARGET_DIR%" /E /XD obj bin /XF *.pdb *.old >nul

rem Kopiere mixforged / mixforge
if exist "%~dp0target\debug\mixforged.exe" (
    copy /y "%~dp0target\debug\mixforged.exe" "%TARGET_DIR%\mixforged.exe" >nul
    copy /y "%~dp0target\debug\mixforged.exe" "%TARGET_DIR%\mixforge.exe" >nul
) else if exist "%~dp0target\release\mixforged.exe" (
    copy /y "%~dp0target\release\mixforged.exe" "%TARGET_DIR%\mixforged.exe" >nul
    copy /y "%~dp0target\release\mixforged.exe" "%TARGET_DIR%\mixforge.exe" >nul
)

if exist "%TARGET_DIR%\mixforged.exe" (
    if not exist "%TARGET_DIR%\mixforge.exe" copy /y "%TARGET_DIR%\mixforged.exe" "%TARGET_DIR%\mixforge.exe" >nul
)

echo [3/4] Installiere GoXLR Audiotreiber via pnputil (Administratorrechte erforderlich)...
set "DRV_DIR=%~dp0apps\windows\Assets\Driver"
if not exist "%DRV_DIR%" set "DRV_DIR=%TARGET_DIR%\Assets\Driver"
if exist "%DRV_DIR%\goxlr_audio.inf" (
    powershell -NoProfile -Command "Start-Process pnputil.exe -ArgumentList '/add-driver \"%DRV_DIR%\goxlr_audio.inf\" /install' -Verb RunAs -Wait"
    powershell -NoProfile -Command "Start-Process pnputil.exe -ArgumentList '/add-driver \"%DRV_DIR%\goxlr_audioks.inf\" /install' -Verb RunAs -Wait"
    echo GoXLR Audiotreiber registriert.
)

echo [4/4] Richte mixforge Hintergrunddienst ^& Windows Autostart ein...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "NextXLR_BackgroundService" /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "NextXLR" /t REG_SZ /d "\"%TARGET_DIR%\NextXLR.exe\" --minimized" /f >nul

rem Erstelle Desktop Verknuepfung
powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), 'NEXT XLR.lnk')); $s.TargetPath = '%TARGET_DIR%\NextXLR.exe'; $s.WorkingDirectory = '%TARGET_DIR%'; $s.Save()"

echo.
echo ==============================================================================
echo   NEXT : XLR v0.1.0-alpha wurde erfolgreich installiert!
echo   Zielverzeichnis: %TARGET_DIR%
echo ==============================================================================
echo.
set /p STARTNOW="Moechten Sie NEXT : XLR jetzt starten? (J/N): "
if /i "%STARTNOW%"=="J" (
    start "" "%TARGET_DIR%\NextXLR.exe"
)
pause

