<div align="center">

<img src="GhostNotes.svg" width="96" height="96" alt="GhostNotes Logo" />

# GhostNotes

**Ultra-lightweight, OBS-invisible floating sticky notes for Windows 10 & 11.**

Keep private stream notes, meeting prompts, checklists, and scratchpads on your screen during live broadcasts, video calls, or screen recordings — without your audience ever seeing them.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?style=flat-square)](https://github.com/ezykl/GhostNotes/releases)
[![Release](https://img.shields.io/badge/release-v1.0.0-0078D6?style=flat-square)](https://github.com/ezykl/GhostNotes/releases)
[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)

<br />

### [⬇️ Download GhostNotes (v1.0.0)](https://github.com/ezykl/GhostNotes/releases)

*No installation or .NET runtime needed. Just download and run.*

</div>

---

## ✨ Features

- **🛡️ 100% Screen-Capture Invisible (OBS Stealth)**  
  Uses Windows DWM Hardware Capture Exclusion (`WDA_EXCLUDEFROMCAPTURE`). Notes remain crystal-clear on your physical monitor, but are completely hidden from OBS Studio, Streamlabs, Discord screen share, Zoom, Google Meet, Microsoft Teams, and screenshot tools.
- **📌 Collapsible Edge Pill**  
  Click the **—** button on any note to collapse it into a discreet edge pill docked against your monitor border. Hovering expands a preview; clicking restores the note to its exact previous position.
- **✍️ In-Place Rich Text Editing**  
  Click anywhere and type. Hover near the bottom edge of any note to reveal the formatting capsule: Bold, Italic, Underline, Strikethrough, Bullet lists, Numbered lists, and Checkboxes (`- [ ]`).
- **🎨 Pastel Colors & Glass Opacity**  
  Click the gear **⚙** button to choose from 6 soft pastel tints, high-contrast font colors, and smooth transparency control (20% to 100%).
- **💾 Automatic Instant Autosave**  
  Every keystroke and position change is safely autosaved to `%APPDATA%\GhostNotes\notes` in the background.
- **🔔 System Tray & Windows Startup**  
  Manage active/closed notes, monitor capture status, and toggle "Start with Windows" directly from the system tray icon.

---

## ⌨️ Shortcuts

| Shortcut | Context | Action |
| :--- | :--- | :--- |
| **`Ctrl + Alt + N`** | Global | Create a new sticky note from anywhere |
| **`Ctrl + Alt + S`** | Global | Hide / reveal all notes instantly |
| **`Ctrl + B`** / **`I`** / **`U`** | Editor | Bold / Italic / Underline |
| **`Ctrl + N`** | Editor | Create a new note |
| **Drag Header** | Window | Reposition note anywhere (including across multi-monitor setups) |
| **Drag Borders** | Window | Resize smoothly in any direction |

---

## 🚀 How to Run

1. Go to **[Releases](https://github.com/ezykl/GhostNotes/releases)**.
2. Download **`GhostNotes.exe`** (Portable single file) or **`GhostNotes-Setup.exe`** (Installer).
3. Double-click to launch!

> **Note:** Zero dependencies required. The portable app is completely self-contained and does not require .NET, Visual Studio, or administrative privileges to run.

---

## 🛠️ Building from Source (Developers)

```powershell
# Clone the repository
git clone https://github.com/ezykl/GhostNotes.git
cd GhostNotes

# Run directly
dotnet run --project src/GhostNotes

# Publish single-file portable executable
dotnet publish src/GhostNotes/GhostNotes.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

---

## 📄 License

Distributed under the [MIT License](LICENSE).