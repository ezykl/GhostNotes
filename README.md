# GhostNotes

Sticky notes for Windows that are always invisible to software screen capture —
web-conference sharing (Teams, Zoom, Google Meet, Discord), recorders (OBS,
Xbox Game Bar), and screenshots (Print Screen, Snipping Tool) — while fully
visible and editable on your own monitor.

## Run

    dotnet run --project src/GhostNotes

## Publish a single exe

    dotnet publish src/GhostNotes/GhostNotes.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish

## Use

- Tray menu (yellow square): New Note / Show/Hide All Notes / Exit
- Ctrl+Alt+N — new note
- Ctrl+Alt+S — show/hide all notes (your view only; capture exclusion is always on)
- Toolbar: bold / italic / underline / text color / note color
- Right-click: New Note / Delete Note / Opacity / Reset Font Size
- Drag the top strip to move; drag any edge or corner to resize
- Ctrl+mouse wheel changes font size (8–48)
- Notes autosave to %APPDATA%\GhostNotes\notes and survive restarts

## How it works

Every window the app creates is flagged with
`SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`. The Windows
compositor (DWM) renders those windows to the physical display but omits them
from every software capture surface. A background guard re-asserts the flag
every second and on every popup, and the tray tooltip reports live protection
status. Notes are also `WS_EX_TOOLWINDOW`, so they never appear in Alt-Tab or
the taskbar.

## What this does NOT protect against (honest limits)

- A phone, camera, or person viewing your physical monitor
- An HDMI capture card between the GPU and monitor
- The process listing in Task Manager (it is there, by name)
- The tray icon if you share your entire screen including the taskbar

This uses a Microsoft-documented content-protection API, not DRM; it is a
privacy convenience, not a security boundary.

## Verification checklist (run per release)

1. OBS preview open → note absent; desktop behind shows through
2. Teams/Zoom share viewed from a second device → note absent
3. Snipping Tool and Print Screen → note absent
4. Physical monitor → note visible and editable
5. Alt-Tab and taskbar → no GhostNotes entries
6. Right-click a note while OBS records → context menu absent from recording
7. Ctrl+Alt+S twice → notes hide and return; still absent from OBS
8. `taskkill /IM GhostNotes.exe /F` mid-edit → restart restores last ~0.5s of edits
