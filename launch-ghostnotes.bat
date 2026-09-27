@echo off
echo Stopping any running GhostNotes instances...
taskkill /IM GhostNotes.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul
echo Launching latest GhostNotes...
if exist "%~dp0src\GhostNotes\bin\Debug\net8.0-windows\GhostNotes.exe" (
    start "" "%~dp0src\GhostNotes\bin\Debug\net8.0-windows\GhostNotes.exe"
) else if exist "%~dp0publish\GhostNotes.exe" (
    start "" "%~dp0publish\GhostNotes.exe"
) else (
    start "" "%~dp0publish-folder\GhostNotes.exe"
)
echo GhostNotes launched. Waiting 3 seconds...
timeout /t 3 /nobreak >nul
echo.
echo === STARTUP LOG ===
if exist "%APPDATA%\GhostNotes\startup.log" (
    type "%APPDATA%\GhostNotes\startup.log"
) else (
    echo No startup log found
)
echo.
echo === CRASH LOG ===
if exist "%APPDATA%\GhostNotes\crash.log" (
    type "%APPDATA%\GhostNotes\crash.log"
) else (
    echo No crash log
)
echo.
pause
