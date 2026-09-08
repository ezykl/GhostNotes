<div align=center>

# 👻 GhostNotes

**Ultra-lightweight, OBS-invisible floating sticky notes for Windows 10 & 11.**

Keep private stream notes, meeting prompts, talking points, checklists, and scratchpads on your screen during live broadcasts, video calls, or screen recordings — without your audience ever seeing them.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue?style=flat-square)](https://github.com/)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple?style=flat-square)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)

</div>

---

## ✨ Features

### 🛡️ 100% Invisible to Screen Captures (OBS Stealth)
GhostNotes leverages Windows Desktop Window Manager (DWM) hardware display affinity (WDA_EXCLUDEFROMCAPTURE):
- **Invisible to Software Capture**: OBS Studio, Streamlabs, Discord stream, Zoom, Microsoft Teams, Google Meet, Xbox Game Bar, and Snipping Tool / Print Screen capture pipelines cannot see your notes.
- **Physical Monitor Only**: Notes remain completely sharp, readable, and editable on your physical monitors.
- **Background Guard**: Continuous audit sweep ensures all popups, menus, and flyouts stay strictly excluded from capture.

### 📝 Pure Floating Sticky Notes
- **Direct In-Place Editing**: No separate preview mode. Click into the note and start typing markdown or rich text immediately.
- **Subtle Bottom Hover Toolbar**: Invisible while typing; hover near the bottom edge to reveal a sleek formatting capsule:
  - **Formatting**: Bold (Ctrl+B), Italic (Ctrl+I), Underline (Ctrl+U), Strikethrough
  - **Structure**: Bullet lists, Numbered lists, Task checkboxes (- [ ]), and Inline code (` code `)
- **Fast Autosave**: Every keystroke is safely persisted to %APPDATA%\GhostNotes\notes with debounced background writing.

### 📌 Half-Pill Docking Drawer
- **Discreet Edge Peek**: Click **Minus (—)** in any note header to collapse it into a sleek edge pill that auto-docks against the edge of your active monitor.
- **Hover-to-Slide**: Only a subtle 75px peek tab remains visible on screen. Hovering your mouse slides the full pill out; moving away tucks it back in.
- **Last-Position Memory**: Clicking the pill immediately expands the sticky note right back to where you had it on your screen.

### 🎨 Customization & Theme Control
Click the **Gear (⚙)** icon in any note header to customize:
- **Color Tints**: Curated pastel glass finishes (Yellow, Sky Blue, Mint Green, Pastel Pink, Lavender, White).
- **Adaptive Logo Glow**: The signature ghost header icon dynamically tints its outline and glow to match the active note color.
- **Font Colors**: High-contrast selections (Charcoal, White, Crimson, Ocean Blue, Emerald, Royal Purple).
- **Glass Opacity Slider**: Smoothly adjust transparency from 20% to 100%.
- **Typography Sizing**: Adjust font size on the fly (10pt to 24pt).
- **Modern Scrollbar**: Minimalist 6px rounded capsule scrollbar replacing clunky default Windows scrollbars.

### ⌨️ Global Hotkeys & Tray Controller
- Lives conveniently in your system notification tray with live OBS status indication.
- **Ctrl + Alt + N**: Create a new note from anywhere.
- **Ctrl + Alt + S**: Toggle visibility of all notes instantly (your view only).
- **Active & Closed Notes Submenus**: Easily focus or restore any open or closed notes.
- **Start with Windows**: 1-click toggle to automatically launch GhostNotes on Windows startup.

---

## 🚀 Download & Installation

Visit the [Releases](https://github.com/) page to grab the latest build:

| Distribution | Description |
| :--- | :--- |
| **GhostNotes-Portable.exe** | **Recommended.** Standalone single-file executable. No install required — download, double-click, and run anywhere. |
| **GhostNotes-Setup.exe** | Standard Windows installer with Start Menu entry, optional desktop shortcut, and uninstaller. |

*Requires Windows 10 (Build 2004+) or Windows 11.*

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Context | Action |
| :--- | :--- | :--- |
| Ctrl + Alt + N | Global | Create a new note anywhere |
| Ctrl + Alt + S | Global | Toggle visibility of all notes |
| Ctrl + B | Editor | Toggle **Bold** |
| Ctrl + I | Editor | Toggle *Italic* |
| Ctrl + U | Editor | Toggle <u>Underline</u> |
| Ctrl + N | Editor | Create a new note |
| Drag Header | Note Window | Move note across displays |
| Drag Edges / Corners | Note Window | 8-directional smooth resize |

---

## 🛠️ Building from Source

### Prerequisites
- Windows 10/11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Clone & Build

`powershell
# Clone the repository
git clone https://github.com/<your-username>/GhostNotes.git
cd GhostNotes

# Run unit tests
dotnet test tests/GhostNotes.Tests

# Run the app locally
dotnet run --project src/GhostNotes

# Publish standalone portable executable
dotnet publish src/GhostNotes/GhostNotes.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
`

---

## 🔒 Privacy & Honest Limits

GhostNotes relies on Microsoft-documented Windows APIs (SetWindowDisplayAffinity) designed for digital rights and confidential content display. Please keep the following inherent limitations in mind:

- **Hardware Capture Cards**: External HDMI/DisplayPort capture devices (e.g. Elgato Cam Link, capture PCIe cards) tap directly into GPU output signals and will display notes.
- **Physical Monitors**: Anyone physically looking at your display or pointing a mobile camera at your screen will see your notes.
- **System Tray Icon**: If you share your entire desktop screen including the Windows taskbar, the small GhostNotes ghost icon in your tray will be visible unless placed inside the overflow chevron.

---

## 📄 License

Distributed under the MIT License. See LICENSE for details.