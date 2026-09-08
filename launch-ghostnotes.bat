@echo off
echo Stopping any running GhostNotes instances...
taskkill /IM GhostNotes.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul
echo Launching GhostNotes...
start "" "%~dp0publish-folder\GhostNotes.exe"
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
