# GhostNotes — Design (v2, compact)

**Target:** Windows 11 (build 26200), .NET 8 WPF. Tray-resident Markdown notes app.
Manager window for editing + organizing; glassy always-on-top overlays for display.
Every window (manager, overlays, all popups) is invisible to software screen
capture via `WDA_EXCLUDEFROMCAPTURE`, visible on the physical monitor.

## Core guarantee

- Mechanism: `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` (Win10 2004+,
  build ≥19041). DWM omits flagged windows from the capture pass; desktop shows
  through — no black box, no viewer cue.
- Stops: Teams/Zoom/Meet/Webex/Discord shares, OBS/Game Bar, Print Screen,
  Snipping Tool, BitBlt/DXGI/WGC capture — for manager, overlays, popups.
- Does NOT stop: cameras/people, HDMI capture cards, Task Manager process entry,
  tray icon on full-screen share. Convenience, not DRM.
- WPF popups (tooltips, menus, dropdowns) spawn capture-visible HWNDs →
  `CaptureGuard` closes the gap: `Popup.Opened` handler + 1s `EnumWindows` sweep,
  re-asserts affinity, verifies via `GetWindowDisplayAffinity`, reports status in
  tray tooltip. Failures never silent.
- Below build 19041: `WDA_MONITOR` fallback (black box) + balloon warning.

## Product

- **Manager** (normal window: title bar, taskbar, Alt-Tab; still capture-excluded):
  opens on launch; X → hide to tray; tray reopens; tray Exit quits.
- **Tab strip** (top): named deployable note-sets; `+` add, double-click rename,
  X close (deletes tab + notes), eye marker on deployed tab.
- **Sidebar** (left): note cards for selected tab; title derived (first `#`
  heading → first line → "(untitled)"); `+` add, per-card delete.
- **Editor** (main): Edit mode (monospace, no wrap) + Preview mode (rendered),
  `Ctrl+P` toggles. Toolbar commands wrap selection or insert at caret:
  Bold `Ctrl+B` `**…**`, Italic `Ctrl+I` `*…*`, Strike `~~…~~`, H1–H3 `#/##/###`,
  bullet `- `, numbered `1. `, task `- [ ]`, inline code, fenced code block,
  quote `> `, link `[text](url)`, rule `---`.
- **Note settings:** tint, opacity, font size (overlay appearance), delete.
- **First run:** seeds "Welcome" tab with 2 sample notes (syntax showcase + how
  overlays work).
- **Overlays** (one per deployed note): read-only rendered Markdown via the same
  renderer as preview; acrylic glass (translucency fallback), rounded ~12px,
  always-on-top, drag anywhere, 8-way resize (min 180×120), `WS_EX_TOOLWINDOW`
  (no Alt-Tab/taskbar); double-click → opens note in manager editor; context
  menu: delete, tint, opacity, font size; live re-render on edit (500ms debounce).
- **Deployment:** exactly one tab at a time (deploying B hides A). Tray lists
  tabs with checkmark; click toggles. Restart → nothing deployed (safe default).
- **Hotkeys:** `Ctrl+Alt+N` new note in active tab; `Ctrl+Alt+S` show/hide active
  tab; `Ctrl+Alt+1…9` deploy tab N; `Ctrl+P` Edit/Preview. Conflicts → Shift
  variants + balloon.
- Single self-contained `.exe`; no installer.

## Data

- `%APPDATA%\GhostNotes\notes\{id}.md` — Markdown content (UTF-8, portable).
- `%APPDATA%\GhostNotes\index.json` — `{version, activeTabId, tabs:[{id, name,
  notes:[{id, x, y, width, height, tint, opacity, fontSize, createdAt,
  updatedAt}]}]}`. Title derived, never stored. Order = array position.
- Atomic writes (temp + `File.Replace`); index rewritten on change (500ms debounce).
- Corrupt index → `.bad` backup + rebuild from `.md` files into "Recovered" tab.
- Missing `.md` → empty content, metadata kept.
- Non-goals: installer/autostart, drag-reorder, split view, search, syntax
  highlighting, images, tables, click-through, per-note topmost toggle.

## Build

- C# / .NET 8 (`net8.0-windows`), WPF + WinForms (`NotifyIcon`), **Markdig**
  (NuGet) with custom Markdig→FlowDocument renderer shared by preview + overlays.
- Components: `App`/`TrayController`, `NoteManager` (facade), `TabDeploymentController`
  (pure deploy state machine), `ManagerWindow`, `OverlayWindow`, `MarkdownRenderer`,
  `MarkdownCommands` (pure text transforms), `Store`, `HotkeyService`/`HotkeySelection`,
  `CaptureGuard`, `AutosaveScheduler` (500ms), `SingleInstanceGuard`,
  `NativeMethods`/`VersionGate`, `Clamp` (`PositionClamp`, `FontZoom` 8–48).
- Errors: affinity fail → retry + "protection: OFF" tooltip; hotkey taken → Shift
  fallback + balloon; 2nd launch → signal first instance's manager; off-screen
  geometry → clamp to nearest monitor; acrylic fail → translucency.
- Layout: `src/GhostNotes/` (`App`, `ManagerWindow`, `OverlayWindow`,
  `TrayController`, `NoteManager`, `TabDeploymentController`, `Models/`,
  `Services/`, `Interop/`); `tests/GhostNotes.Tests/` (`StoreTests`,
  `TabDeploymentTests`, `MarkdownCommandsTests`, `RendererTests`, `ClampTests`,
  `HotkeySelectionTests`, `CaptureGuardTests`, `SingleInstanceGuardTests`,
  `AutosaveSchedulerTests`).
- Publish: `dotnet publish -c Release -r win-x64 --self-contained
  -p:PublishSingleFile=true -o publish`.

## Testing

- Unit (xUnit): store round-trip/corrupt-recovery/atomic; deploy exclusivity,
  toggle, hide-all, index bounds; MD command transforms; renderer assertions
  (heading→large bold, `**x**`→Bold run, code block→monospace tinted, `- `→List,
  `> `→indented quote); clamp, hotkey selection, guard (fakes), single-instance,
  autosave. No WPF windows in tests.
- Manual per release: OBS/Teams/SnippingTool absence of manager+overlays+popups;
  physical monitor visible; Alt-Tab shows manager only; deploy via tray+hotkeys
  stays clean; live re-render clean; `taskkill` mid-edit restores ≤0.5s loss;
  2nd launch surfaces manager.

## Deferred

Drag-reorder, split view, search, tags, export, syntax highlighting, images,
tables, click-through, per-note topmost, installer, autostart, portable mode.

---
*Compacted from `2026-09-07-ghostnotes-design.md` (v2, 2026-09-07). Full spec is
authoritative on any conflict.*
