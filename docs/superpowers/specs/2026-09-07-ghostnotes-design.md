# GhostNotes — Design (v2)

**Date:** 2026-09-07
**Status:** Approved in brainstorming (v2 revision); pending user review
**Target:** Windows 11 (build 26200), .NET 8
**Supersedes:** the v1 RTF/rich-text design — no implementation exists yet, so no migration.

## 1. Overview

GhostNotes is a tray-resident Markdown notes app for Windows. You compose and
organize notes in a proper **manager window**, then deploy them as glassy,
always-on-top **overlay** widgets. Every window the app shows — the manager,
the overlays, and every popup/tooltip/context menu — is structurally
invisible to software screen capture (Teams, Zoom, Meet, Discord, OBS,
screenshots) while fully visible on the physical monitor.

## 2. Goals (v2)

- A **manager window**: normal window (title bar, taskbar, Alt-Tab), opens on
  launch; X hides to tray; tray reopens; tray Exit quits.
- Notes are **Markdown documents** edited in a two-mode editor:
  - **Edit mode** — monospace editing with a predefined Markdown command
    toolbar (bold, italic, strikethrough, H1–H3, bullet/numbered lists, task
    checkboxes, inline code, fenced code block, blockquote, link, horizontal
    rule)
  - **Preview mode** — the rendered document, toggled with `Ctrl+P`
- **Tabs** are named deployable sets of notes. A tab strip on top; `+` add,
  double-click rename, X close, eye marker on the deployed tab.
- A **note sidebar** (left) listing the selected tab's notes as title cards
  (title = first heading / first non-empty line). `+` add note; delete per
  card.
- **Overlays**: read-only rendered Markdown (the manager's exact renderer),
  glassy acrylic, always-on-top, draggable, resizable; double-click opens the
  note in the manager editor; live re-render while editing in the manager.
- **Deployment**: exactly one tab deployed at a time. Tray menu per tab,
  `Ctrl+Alt+S` show/hide the active tab, `Ctrl+Alt+1…9` deploy tab N,
  `Ctrl+Alt+N` new note in the active tab. On restart nothing is deployed.
- **Persistence**: notes as real `.md` files; tabs + overlay geometry in
  `index.json`; atomic writes; 500ms debounced autosave.
- **First run**: seed a "Welcome" tab with two sample notes (syntax showcase
  + how overlays work).
- Single self-contained `.exe`; personal use, no installer.

## 3. Non-goals (v2)

- Installer, autostart, multi-user distribution, signing
- Note/tab drag-reorder, split view, search across notes
- Markdown syntax highlighting in Edit mode, image rendering, tables
  (parse-and-render subset only)
- Click-through mode; per-note always-on-top toggle
- macOS/Linux support

## 4. Threat model — what capture exclusion does and does not stop

The mechanism is the documented Win32 call
`SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` (Windows 10 2004+,
build 19041). The Desktop Window Manager (DWM) composites two rendering
paths — one for the physical display, one for capture consumers (Desktop
Duplication, Windows Graphics Capture, GDI BitBlt). A flagged window is
included in the display pass and omitted from the capture pass. Capture
software cannot override this; the desktop behind the window shows through —
no black box, no cue for viewers.

**Stops:** screen share in Teams/Zoom/Meet/Webex/Discord; OBS, Xbox Game Bar,
and similar recorders; Print Screen, Snipping Tool, programmatic
BitBlt/DXGI/WGC capture — for the manager, the overlays, and their popups.

**Does not stop (documented in-app):** a phone/camera/person viewing the
monitor; an HDMI capture card between GPU and monitor; the process appearing
in Task Manager; the tray icon on a full-screen share. This is a
content-protection convenience, not DRM and not a security boundary.

## 5. Architecture

Single-process .NET 8 WPF application. Two kinds of window:

- **ManagerWindow** — normal (not layered) window, resizable, in taskbar and
  Alt-Tab. Still flagged `WDA_EXCLUDEFROMCAPTURE` (its content and every
  popup are never capturable). It is the app's home: tabs, sidebar, editor.
- **OverlayWindow** — one per deployed note: frameless, layered, topmost,
  read-only, `WS_EX_TOOLWINDOW` (no Alt-Tab/taskbar), flagged
  `WDA_EXCLUDEFROMCAPTURE`.

Every top-level window the process shows is, at creation:
1. **Capture-excluded** via `SetWindowDisplayAffinity(…, WDA_EXCLUDEFROMCAPTURE)`
2. Overlays additionally get `WS_EX_TOOLWINDOW`

### The WPF popup wrinkle

Tooltips, context menus, and combo dropdowns each spawn their own top-level
HWND that starts capture-visible. `CaptureGuard` closes the gap:
- A `Popup.Opened` class handler applies affinity the instant any process
  popup appears
- A 1-second `DispatcherTimer` sweep enumerates all top-level windows of the
  process (`EnumWindows`), re-asserts affinity on any that lack it, and
  verifies state with `GetWindowDisplayAffinity`

Affinity failure is never silent: the tray tooltip reports live status.

### Version gate

Below build 19041, `WDA_EXCLUDEFROMCAPTURE` degrades to `WDA_MONITOR` (black
box). The app gates on `Environment.OSVersion` and notifies via balloon.
(Dev machine: build 26200.)

## 6. Components

| Unit | Purpose | Dependencies |
|---|---|---|
| `App` / `TrayController` | Startup, single-instance, tray icon + menu (Open Manager, per-tab deploy, Show/Hide, New Note, Exit), protection-status tooltip, balloon notices | `NoteManager`, `HotkeyService`, `CaptureGuard`, `TabDeploymentController` |
| `NoteManager` | Facade: creates manager/overlay windows, wires editing → autosave → overlay re-render, deploy/hide, delete | `Store`, `TabDeploymentController`, `ManagerWindow`, `OverlayWindow`, `AutosaveScheduler` |
| `TabDeploymentController` | Pure deploy-state machine: exactly one deployed tab; `Deploy(tab)`, `Toggle()`, `HideAll()`, events | `Models` |
| `ManagerWindow` | Tab strip, note sidebar, Edit/Preview editor, note settings | `MarkdownRenderer`, `MarkdownCommands`, `Store` |
| `OverlayWindow` | Read-only rendered note; glassy, draggable, resizable; double-click → edit in manager | `MarkdownRenderer` |
| `MarkdownRenderer` | Markdig → FlowDocument (shared by preview and overlays) | Markdig |
| `MarkdownCommands` | Pure text transforms for the toolbar (wrap/insert at caret) | — |
| `Store` | Load/save `.md` files + `index.json`; atomic writes; corrupt recovery | `Models` |
| `HotkeyService` / `HotkeySelection` | Register/route global hotkeys; fallback selection | `NativeMethods` |
| `CaptureGuard` | Popup handler + sweep + status events | `NativeMethods` |
| `AutosaveScheduler` | Debounced save (500ms) | — |
| `SingleInstanceGuard` | Mutex + "open manager" signal to first instance | — |
| `NativeMethods` / `VersionGate` | P/Invoke surface + build gate | — |
| `Clamp` (`PositionClamp`, `FontZoom`) | Off-screen clamp + font clamp (8–48) | — |

## 7. UX specification

### Manager window

- **Chrome:** standard window; X hides to tray (app keeps running).
- **Tab strip (top):** tabs in order; `+` adds "New Tab"; double-click
  renames inline; X closes (deletes tab + its notes); the deployed tab shows
  an eye marker.
- **Note sidebar (left):** the selected tab's notes as title cards; title
  derived from the first `#` heading, else the first non-empty line, else
  "(untitled)"; `+` adds a note; a per-card delete.
- **Editor (main):** Edit mode is a monospace, non-wrapping text box with the
  command toolbar above it. Preview mode renders via `MarkdownRenderer` into
  a read-only `FlowDocumentScrollViewer`. `Ctrl+P` toggles.
- **Note settings (right of editor or in a per-note header):** tint color,
  opacity, font size, delete note. These affect the overlay appearance.
- **Command toolbar (Edit mode):**
  - Bold `Ctrl+B` → `**…**`, Italic `Ctrl+I` → `*…*`, Strikethrough → `~~…~~`
  - H1/H2/H3 → prefix lines `#`/`##`/`###`
  - Bullet list `- `, Numbered list `1. `, Task checkbox `- [ ]`
  - Inline code `` `…` ``, Code block ````` ``` `````` ```` ````` ``````, Blockquote `> `
  - Link `[text](url)`, Horizontal rule `---`
  - Commands wrap the current selection or insert at the caret.

### Overlay windows

- Read-only rendered note in a glassy frame: rounded (~12px), acrylic
  blur-behind (tinted with the note color), luminous edge, shadow; plain
  translucency fallback if acrylic fails.
- Always-on-top; drag anywhere; 8-direction resize grips (min 180×120).
- Double-click → the note opens in the manager editor (manager surfaces if
  hidden). Context menu: Delete Note, tint, opacity, font size.
- Geometry (position/size), tint, opacity, font size persist per note.

### Deployment semantics

- One tab deployed at a time; `Deploy(B)` hides A's overlays and shows B's.
- `Ctrl+Alt+S` toggles the active tab's overlays (show/hide — hide also
  resets deployment to "none").
- `Ctrl+Alt+1…9` deploys the Nth tab by order (ignored if N > tab count).
- Tray lists tabs with a checkmark on the deployed one; clicking toggles.

### Hotkeys

| Combo | Action |
|---|---|
| `Ctrl+Alt+N` | New note in the active tab (opens in manager editor) |
| `Ctrl+Alt+S` | Show/hide the active tab's overlays |
| `Ctrl+Alt+1…9` | Deploy tab N |
| `Ctrl+P` | Toggle Edit/Preview (in manager only) |

Conflicts fall back to `Ctrl+Alt+Shift+…` with a balloon notice.

## 8. Data model & persistence

- `%APPDATA%\GhostNotes\notes\{id}.md` — the Markdown content, plain UTF-8,
  editable outside the app.
- `%APPDATA%\GhostNotes\index.json`:

```json
{
  "version": 1,
  "activeTabId": "guid",
  "tabs": [
    {
      "id": "guid",
      "name": "Standup",
      "notes": [
        {
          "id": "guid",
          "x": 120, "y": 240, "width": 320, "height": 220,
          "tint": "#FFF59D", "opacity": 0.85, "fontSize": 14,
          "createdAt": "2026-09-07T10:00:00Z",
          "updatedAt": "2026-09-07T10:05:00Z"
        }
      ]
    }
  ]
}
```

- Note title is **derived**, not stored (first `#` heading → first line →
  "(untitled)"), so titles never drift from content.
- Order = array position; v2 has no drag-reorder.
- Atomic writes: write temp file then `File.Replace`; index rewritten on any
  change (debounced 500ms).
- Corrupt `index.json` → renamed `.bad`, then rebuilt from the `.md` files
  into a "Recovered" tab (content is never lost — it lives in the files).
- Missing `.md` for a known note → treated as empty content, metadata kept.

## 9. Error handling

| Failure | Behavior |
|---|---|
| Affinity call fails or is reset | CaptureGuard retries next sweep; tray tooltip shows "protection: OFF" |
| Hotkey combo owned by another app | Shift-variant fallback + balloon notice |
| Corrupt `index.json` | `.bad` backup + rebuild from `.md` files into "Recovered" tab |
| Missing `.md` file | Note loads with empty content, metadata preserved |
| Second app launch | Exits immediately and signals the first instance to open its manager |
| Windows build < 19041 | `WDA_MONITOR` fallback + balloon warning |
| Acrylic unsupported/fails | Plain semi-transparency fallback |
| Note geometry off-screen (display changed) | Clamped onto the nearest visible monitor at startup |

## 10. Testing strategy

### Unit tests (xUnit)

- `Store`: index round-trip; corrupt-index recovery + rebuild; missing-.md
  handling; atomic write leaves no partial file; note create/delete/rename.
- `TabDeploymentController`: deploy exclusivity (deploying B hides A), toggle,
  hide-all, deploy-by-index bounds.
- `MarkdownCommands`: bold/italic/strike wrap selection; wrap empty selection
  (inserts placeholder); header/list/code/quote/link/hr transforms.
- `MarkdownRenderer` (Markdig→FlowDocument): a heading produces a large bold
  paragraph; `**x**` produces a Bold run; a fenced code block produces a
  monospace tinted paragraph; `- ` produces a List; `> ` produces an indented
  quote.
- Carried over: `PositionClamp`/`FontZoom`, `HotkeySelection`,
  `CaptureGuard` (fakes), `SingleInstanceGuard`, `AutosaveScheduler`.

Desktop-bound code (ManagerWindow/OverlayWindow/Tray/App wiring) is covered by
the manual checklist.

### Manual verification checklist (per release)

1. OBS preview open → manager window absent; desktop shows through
2. Teams/Zoom share viewed from a second device → manager AND overlays absent
3. Snipping Tool + Print Screen → absent
4. Physical monitor → manager and overlays visible and interactive
5. Alt-Tab/taskbar → overlays absent; manager present (by design)
6. Right-click / open combos in the manager while OBS records → popups absent
7. Deploy tab via tray and `Ctrl+Alt+1…9`; `Ctrl+Alt+S` hide/return — overlays
   never appear in OBS
8. Edit a deployed note in the manager → overlay re-renders live; OBS clean
9. `taskkill /IM GhostNotes.exe /F` mid-edit → restart restores last ~0.5s of
   edits and all notes/tabs
10. Second launch → first instance's manager surfaces

## 11. Project structure

```
/ (repo root)
  docs/superpowers/specs/2026-09-07-ghostnotes-design.md   (this file)
  .gitignore
  src/GhostNotes/
    GhostNotes.csproj               (net8.0-windows, UseWPF + UseWindowsForms, Markdig)
    App.xaml / App.xaml.cs
    ManagerWindow.xaml / .cs
    OverlayWindow.xaml / .cs
    TrayController.cs
    NoteManager.cs
    TabDeploymentController.cs
    Models/  (Tab.cs, Note.cs, Index.cs)
    Services/ (Store.cs, MarkdownRenderer.cs, MarkdownCommands.cs,
               AutosaveScheduler.cs, HotkeyService.cs, HotkeySelection.cs,
               SingleInstanceGuard.cs, Clamp.cs)
    Interop/ (NativeMethods.cs, VersionGate.cs, CaptureGuard.cs)
  tests/GhostNotes.Tests/
    GhostNotes.Tests.csproj          (xunit + Markdig)
    StoreTests.cs, TabDeploymentTests.cs, MarkdownCommandsTests.cs,
    RendererTests.cs, ClampTests.cs, HotkeySelectionTests.cs,
    CaptureGuardTests.cs, SingleInstanceGuardTests.cs, AutosaveSchedulerTests.cs
```

Publish: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish`.

## 12. Future work (explicitly deferred)

- Drag-reorder of tabs/notes, split Edit+Preview view, search, tags, export
- Syntax highlighting in Edit mode, image rendering, tables
- Click-through mode; per-note always-on-top toggle
- Installer, autostart, portable-mode settings
