# GhostNotes — Design

**Date:** 2026-09-07
**Status:** Approved in brainstorming session; pending user review
**Target:** Windows 11 (build 26200), .NET 8

## 1. Overview

GhostNotes is a sticky-note widget app for Windows. Notes float above all
windows on the user's physical screen, yet are **always invisible to software
screen capture** — web-conference sharing (Teams, Zoom, Google Meet, Discord),
recording/streaming software (OBS, Xbox Game Bar), and screenshots (Print
Screen, Snipping Tool). Capture exclusion is a structural property of every
window the app creates; there is no off switch and no mode to forget.

## 2. Goals (v1)

- Multiple independent sticky notes; tray-driven and hotkey-driven
- Always-on-top, draggable, freely resizable notes with a glassy (frosted)
  appearance
- Rich text: bold, italic, underline, text color, note color
- Responsive text: word-wrap with live reflow on resize; Ctrl+wheel font zoom
- Persistence: notes (content, formatting, position, size, color, opacity,
  font size) survive restarts
- `Ctrl+Alt+N` creates a note; `Ctrl+Alt+S` shows/hides all notes **for the
  user's own view only** — capture exclusion remains active in both states
- No taskbar presence, no Alt-Tab entry (tool windows)
- Single self-contained `.exe` via `dotnet publish`; personal use, no installer

## 3. Non-goals (v1)

- Installer, autostart with Windows, multi-user distribution, signing
- Click-through mode, per-note always-on-top toggle (deferred)
- Cloud sync, encryption, search across notes
- macOS/Linux support

## 4. Threat model — what capture exclusion does and does not stop

The mechanism is the documented Win32 call
`SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` (Windows 10 2004+,
build 19041). The Desktop Window Manager (DWM) composites two rendering
paths: one for the physical display, one for capture consumers (Desktop
Duplication, Windows Graphics Capture, GDI BitBlt). A window flagged
`WDA_EXCLUDEFROMCAPTURE` is included in the display pass and omitted from
the capture pass. Capture software cannot override this; it receives a frame
that never contained the window. The desktop behind the note shows through —
no black rectangle, no visual cue for viewers.

**Stops:**
- Screen share in Teams, Zoom, Google Meet, Webex, Discord (any tool built on
  standard Windows capture surfaces)
- OBS, Xbox Game Bar, and similar recorders
- Print Screen, Snipping Tool, programmatic BitBlt/DXGI/WGC capture

**Does not stop (documented in-app):**
- A phone, camera, or person physically viewing the monitor
- An HDMI capture card between GPU and monitor (hardware path, pre-DWM)
- The process appearing in Task Manager (affinity affects compositing, not
  process enumeration)
- The tray icon appearing when the user shares the entire screen including the
  taskbar

Microsoft's own documentation: this is a content-protection convenience, not
DRM and not a security boundary against the local user.

## 5. Architecture

Single-process .NET 8 WPF application, one project, no external UI
dependencies. No main window: the app is resident in the system tray; each
note is an independent frameless top-level window.

Every top-level window the process shows receives, at creation:

1. **Capture exclusion** — `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`
2. **Tool-window style** — `WS_EX_TOOLWINDOW` (no Alt-Tab, no taskbar)
3. **Activation** — `WS_EX_NOACTIVATE` was evaluated and rejected: it stops
   the window from taking keyboard focus, which would break typing into the
   note whenever another application is foreground. Notes therefore activate
   normally on click (required for editing) and rely on
   `WS_EX_TOOLWINDOW` alone for taskbar/Alt-Tab invisibility

### The WPF popup wrinkle

WPF tooltips, context menus, and dropdowns each spawn their own top-level
HWND that starts capture-visible. CaptureGuard closes this gap two ways:

- A `Popup.Opened` class handler applies affinity the instant any popup
  belonging to the process appears
- A 1-second `DispatcherTimer` sweep enumerates all top-level windows of the
  current process (`EnumWindows` + `GetWindowThreadProcessId`), applies
  `WDA_EXCLUDEFROMCAPTURE` to any that lack it, and verifies applied state
  with `GetWindowDisplayAffinity`

The periodic sweep also heals cases where Windows silently resets affinity
after certain window events. Affinity application failure is never silent:
the tray tooltip reflects live protection status.

### Windows version gate

`WDA_EXCLUDEFROMCAPTURE` requires build ≥ 19041. On older builds it silently
degrades to `WDA_MONITOR` (black box in captures). The app checks
`Environment.OSVersion` at startup; below 19041 it uses `WDA_MONITOR` and
notifies the user. (Dev machine: Windows 11 Pro build 26200 — unaffected.)

## 6. Components

| Unit | Purpose | Key dependencies |
|---|---|---|
| `App` / `TrayController` | Startup, single-instance mutex, tray icon + menu (New Note, Show/Hide All, Exit), protection-status tooltip | `NoteManager`, `HotkeyService`, WinForms `NotifyIcon` |
| `NoteManager` | Creates/restores/deletes note windows; owns the collection; services show/hide-all | `NoteWindow`, `NoteRepository` |
| `NoteWindow` | One frameless note: glassy visual, drag header, 8-direction resize grips, rich-text toolbar, context menu | `NativeMethods`, `CaptureGuard` |
| `NativeMethods` | P/Invoke surface: `SetWindowDisplayAffinity`, `GetWindowDisplayAffinity`, `RegisterHotKey`/`UnregisterHotKey`, `SetWindowLong`, `EnumWindows`, `SetWindowCompositionAttribute` | — |
| `CaptureGuard` | Popup handler + 1s sweep + status reporting (raises `ProtectionStatusChanged`) | `NativeMethods` |
| `HotkeyService` | Registers `Ctrl+Alt+N` / `Ctrl+Alt+S`; on conflict falls back to `Ctrl+Alt+Shift+N/S` with tray balloon notice; routes to `NoteManager` | `NativeMethods` |
| `NoteRepository` | Load/save one JSON file per note in `%APPDATA%\GhostNotes\notes\`; 500ms debounced autosave; corrupt-file recovery | `Models.Note` |
| `Models.Note` | Serializable note state: id, RTF payload, position, size, tint color, opacity, font size, timestamps | — |

## 7. UX specification

### Appearance (glassy overlay)

- Frameless (`WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`)
- Rounded corners ~12px, thin luminous border, soft drop shadow
- **Frosted acrylic blur-behind** via `SetWindowCompositionAttribute` with
  `ACCENT_ENABLE_ACRYLICBLURBEHIND`, tinted with the note's color; if the
  call fails, graceful fallback to plain semi-transparency (gradient tint at
  the note's opacity) — the note always renders
- Per-note tint color (default yellow); per-note opacity slider
  (context menu), default 85%

### Sizing and text

- 8-direction resize grips (edges + corners) via custom `Thumb` elements;
  minimum size 180×120; size and position persist
- Text always word-wraps and reflows live during resize (no horizontal
  scrolling, no clipping)
- Ctrl+mouse wheel inside a note changes font size (range 8–48); persisted
  per note
- Toolbar: bold / italic / underline / text color / note color

### Interaction

- Drag anywhere on the note header strip; note body edits text
- Context menu: New Note, Delete Note, Note Color, Opacity, Font Size,
  Exit
- Tray menu: New Note, Show/Hide All, Exit; tooltip shows note count and
  protection status ("Capture protection: ON")

### Hotkeys

| Combo | Action |
|---|---|
| `Ctrl+Alt+N` | New note (cascaded: each new note offset 24px from the previous) |
| `Ctrl+Alt+S` | Show/Hide all notes — user's own view only |

Show/hide semantics: hiding sets `Visibility=Hidden` on every note (a hidden
window is uncapturable by definition); showing re-applies
`WDA_EXCLUDEFROMCAPTURE` **before** setting `Visibility=Visible`, so there is
no capture gap on reveal.

## 8. Data & persistence

One JSON file per note: `%APPDATA%\GhostNotes\notes\{id}.json`

```json
{
  "id": "guid",
  "rtf": "{\\rtf1...}",
  "x": 120, "y": 240,
  "width": 320, "height": 220,
  "tint": "#FFF59D",
  "opacity": 0.85,
  "fontSize": 14,
  "createdAt": "2026-09-07T10:00:00Z",
  "updatedAt": "2026-09-07T10:05:00Z"
}
```

- Autosave: text edit, move, resize, color/opacity/font change → 500ms
  debounce → write file
- Save-all on app exit and on Windows session end (`SessionEnding`)
- Storing RTF preserves rich-text formatting across restarts

## 9. Error handling

| Failure | Behavior |
|---|---|
| Affinity call fails or is reset | CaptureGuard retries next sweep; tray tooltip shows "protection: OFF" until verified back on |
| Hotkey combo owned by another app | Automatic fallback to `Ctrl+Alt+Shift+N/S` + tray balloon notice |
| Corrupt/unreadable note JSON | Rename to `{id}.json.bad` (backup), skip it, load remaining notes |
| Second app launch | Single-instance mutex; the new process exits and signals the existing instance to surface |
| Windows build < 19041 | `WDA_MONITOR` fallback + balloon warning |
| Acrylic blur unsupported/fails | Plain semi-transparency fallback; appearance degrades, function does not |
| Note position off-screen (display layout changed) | Clamp back onto the nearest visible monitor at startup |

## 10. Testing strategy

### Unit tests (xUnit, `tests/GhostNotes.Tests`)

- `NoteRepository`: save/load round-trip including RTF payload; corrupt-file
  rename-and-skip; debounce flush timing
- Hotkey fallback selection logic
- Off-screen position clamping logic
- JSON schema stability (fields survive save/load unchanged)

The Win32 interop surface (`NativeMethods`, `CaptureGuard`) requires a real
desktop and is covered by the manual checklist below; interop wrappers are
kept thin so logic above them is unit-testable.

### Manual verification checklist (run on target machine, once per release)

1. OBS preview open → note absent; desktop behind it shows through
2. Teams/Zoom share viewed from a second device (phone) → note absent
3. Snipping Tool and Print Screen → note absent from screenshots
4. Physical monitor → note visible and fully interactive
5. Alt-Tab → no GhostNotes entry; taskbar → no entry; Task Manager → process
   listed (expected)
6. Right-click a note while OBS records → context menu also invisible in
   recording
7. `Ctrl+Alt+S` → all notes hide instantly; press again → notes return;
   capture exclusion verified still on (checklist item 1 again)
8. Terminate the app abruptly mid-edit (`taskkill /IM GhostNotes.exe /F`)
   → restart → notes restored from last autosave

## 11. Project structure

```
/ (repo root)
  docs/superpowers/specs/2026-09-07-ghostnotes-design.md   (this file)
  .gitignore
  src/GhostNotes/
    GhostNotes.csproj          (net8.0-windows, UseWPF + UseWindowsForms for NotifyIcon)
    App.xaml / App.xaml.cs     (startup, single-instance, session-end save)
    TrayController.cs
    NoteManager.cs
    NoteWindow.xaml / NoteWindow.xaml.cs
    Interop/NativeMethods.cs
    Interop/CaptureGuard.cs
    Services/HotkeyService.cs
    Services/NoteRepository.cs
    Models/Note.cs
  tests/GhostNotes.Tests/
    GhostNotes.Tests.csproj
    NoteRepositoryTests.cs
    HotkeyServiceTests.cs
    PositionClampingTests.cs
```

Publish: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
→ single `.exe`, personal use.

## 12. Future work (explicitly deferred)

- Click-through mode; per-note always-on-top toggle
- Installer, autostart, portable-mode settings file
- Note search, tags, export
- Optional: hide tray icon (hotkey-only operation)
