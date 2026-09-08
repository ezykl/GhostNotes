# GhostNotes

Sticky notes for Windows that are completely invisible to software screen capture —
web-conference sharing (Teams, Zoom, Google Meet, Discord), recorders (OBS,
Xbox Game Bar), and screenshots (Print Screen, Snipping Tool) — while remaining
fully visible and editable on your own physical monitor.

---

## Architecture: Pure Floating Sticky Notes

GhostNotes is built around an independent floating sticky note architecture inspired by Obsidian and modern desktop canvas design. Notes exist as lightweight, topmost, borderless acrylic windows with zero clutter:

- **In-Place Direct Editing**: No bulky toolbars or preview toggles. Click directly into the note and start typing markdown or rich text immediately.
- **Fast Autosave**: Changes are automatically debounced and saved to `%APPDATA%\GhostNotes\notes` within 500ms, persisting cleanly across app restarts.
- **Stealth Windows**: Each note is assigned the `WS_EX_TOOLWINDOW` extended style so it never pollutes your taskbar or Alt-Tab switcher.

---

## Keyboard Shortcuts & Controls

| Shortcut | Action |
| :--- | :--- |
| `Ctrl + B` | Toggle **bold** text |
| `Ctrl + I` | Toggle *italic* text |
| `Ctrl + U` | Toggle <u>underline</u> text |
| `Ctrl + N` | Create a new note (when note editor is focused) |
| `Ctrl + Alt + N` | Global hotkey: create a new note anywhere |
| `Ctrl + Alt + S` | Global hotkey: toggle visibility of all notes (your view only) |
| `Drag Header` | Reposition the note anywhere across your monitors |
| `Drag Borders / Corners` | 8-directional smooth window resizing |

---

## In-Place Settings Flyout

Click the **Gear (⚙)** icon in any note header to open the in-place settings flyout:

- **Color Tints**: Choose from curated pastel glass tints (Yellow, Sky Blue, Mint Green, Pastel Pink, Lavender, White) with real-time accenting.
- **Font Colors**: High-contrast swatches (Charcoal, White, Crimson Red, Ocean Blue, Emerald Green, Royal Purple).
- **Opacity Slider**: Smoothly adjust note glass transparency from 20% to 100%.
- **Font Size Slider**: Snap typography sizing from 10pt to 24pt.
- **Delete Permanently**: Permanently delete the note and its file from disk.

---

## Edge Pill Minimization & Dual-Monitor Auto-Docking

- **Minimize to Edge Pill**: Click the **Minus (—)** button on any note header to collapse the note into an unobtrusive 34px-tall edge pill.
- **Dual-Monitor Auto-Docking**: GhostNotes calculates the active monitor's working area bounds and automatically docks the minimized pill flush against the right edge of whichever display the note is on.
- **Click-to-Restore**: Click anywhere on the pill or click the expand icon to instantly pop the note back to its exact previous dimensions and position.
- **Edge Snapping**: Drag the minimized pill to slide it along your screen edge or snap it to any display border.

---

## System Tray Controller

GhostNotes lives in the Windows notification area (system tray) with a custom ghost icon:

- **Protection Status & Note Counter**: Hover over the tray icon to see active notes count and live OBS Stealth status (`ON`/`OFF`).
- **Tray Menu Options**:
  - **New Note** (`Ctrl+Alt+N`)
  - **Show / Hide All** (`Ctrl+Alt+S`)
  - **Active Notes**: Direct list of active notes with pill indicators and 1-click focus/restore.
  - **Closed Notes**: Quick submenu to resurrect closed notes without losing their content.
  - **Start with Windows**: Toggle automatic startup on Windows boot via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
  - **Exit**: Safely flushes all pending saves to disk and shuts down the application.

---

## How It Works

Every window created by GhostNotes is protected using:
```csharp
NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
```
The Windows Desktop Window Manager (DWM) renders these windows exclusively to the physical display output, completely stripping them from software capture pipelines (DirectX Graphics Capture, Desktop Duplication API, GDI `BitBlt`, Windows Graphics Capture).

A background `CaptureGuard` service continuously audits all window handles every second and re-asserts protection on all popups, flyouts, and newly spawned windows.

---

## Build & Run

### Run from source

```powershell
dotnet run --project src/GhostNotes
```

### Run unit tests

```powershell
dotnet test tests/GhostNotes.Tests
```

### Build solution

```powershell
dotnet build GhostNotes.sln
```

### Publish Single-File Self-Contained Executable

```powershell
dotnet publish src/GhostNotes/GhostNotes.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

### Publish Self-Contained Directory Release

```powershell
dotnet publish src/GhostNotes/GhostNotes.csproj -c Release -r win-x64 --self-contained -o publish-folder
```

---

## Honest Limits

- **Physical Viewers**: Does not protect against someone physically viewing your monitor or using an external phone/camera.
- **Hardware Capture Cards**: HDMI/DisplayPort capture cards tap into the raw video signal between your GPU and monitor, so hardware-captured feeds will show notes.
- **Task Manager**: The process is visible in Task Manager as `GhostNotes.exe`.
- **Tray Icon**: If you share your entire desktop display including the Windows taskbar, the small GhostNotes tray icon in the system tray is visible (unless placed inside the overflow chevron).

This tool leverages official Microsoft-supported Windows content-protection APIs for privacy convenience during meetings, screen shares, and streaming.

---

## Verification Checklist

1. OBS preview open → note absent; desktop behind shows through
2. Teams/Zoom share viewed from a second device → note absent
3. Snipping Tool and Print Screen → note absent
4. Physical monitor → note visible and editable
5. Alt-Tab and taskbar → no GhostNotes entries
6. Click gear icon to toggle Settings Flyout → flyout absent from screen recording
7. Minimize note → docks neatly as edge pill against the monitor's right boundary; click pill restores full note
8. Toggle "Start with Windows" in tray menu → registry key created/removed cleanly
9. Ctrl+Alt+S twice → notes hide and return; still absent from OBS
10. `taskkill /IM GhostNotes.exe /F` mid-edit → restart restores last ~0.5s of edits

