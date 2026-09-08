using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GhostNotes.Interop;
using GhostNotes.Models;
using GhostNotes.Services;
using Application = System.Windows.Application;

namespace GhostNotes;

public sealed class TrayController : IDisposable
{
    private readonly NoteManager _manager;
    private readonly NotifyIcon _icon;
    private bool _disposed;

    public TrayController(NoteManager manager, HotkeyService hotkeys, CaptureGuard guard)
    {
        _manager = manager;

        _icon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "GhostNotes — OBS Protected",
            Visible = true
        };

        RebuildMenu();

        _icon.DoubleClick += (_, _) => _manager.CreateNote();
        hotkeys.HotkeyNotice += (_, message) => ShowWarning(message);
        guard.ProtectionStatusChanged += (_, on) => UpdateTooltip(on);
        _manager.NotesStateChanged += (_, _) => RebuildMenu();

        UpdateTooltip(guard.IsProtected);
    }

    private static Icon BuildIcon()
    {
        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(appDir, "Assets", "GhostNotes.ico");
            if (File.Exists(path))
            {
                return new Icon(path, 32, 32);
            }

            using var bmp = IconBuilder.RenderGhostLogo(32);
            return Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private void RebuildMenu()
    {
        var menu = new ContextMenuStrip();

        // New Note
        var newNoteItem = new ToolStripMenuItem("New Note (Ctrl+Alt+N)", null, (_, _) => _manager.CreateNote());
        newNoteItem.Font = new Font(newNoteItem.Font, FontStyle.Bold);
        menu.Items.Add(newNoteItem);

        // Show/Hide All
        menu.Items.Add(new ToolStripMenuItem("Show / Hide All (Ctrl+Alt+S)", null, (_, _) => _manager.ToggleVisibility()));
        menu.Items.Add(new ToolStripSeparator());

        // Active Notes Submenu
        var activeNotes = _manager.Windows.ToList();
        if (activeNotes.Count > 0)
        {
            var activeMenu = new ToolStripMenuItem($"Active Notes ({activeNotes.Count})");
            foreach (var win in activeNotes)
            {
                var title = string.IsNullOrWhiteSpace(win.Model.Title) ? "Untitled Note" : win.Model.Title;
                if (title.Length > 28) title = title.Substring(0, 25) + "...";
                if (win.Model.IsMinimized) title += " [Pill]";

                activeMenu.DropDownItems.Add(new ToolStripMenuItem(title, null, (_, _) =>
                {
                    if (win.Model.IsMinimized) win.SetMinimized(false);
                    win.ReapplyProtectionAndShow();
                    win.Activate();
                }));
            }
            menu.Items.Add(activeMenu);
        }

        // Closed Notes Submenu
        var closedNotes = _manager.ClosedNotes.ToList();
        if (closedNotes.Count > 0)
        {
            var closedMenu = new ToolStripMenuItem($"Closed Notes ({closedNotes.Count})");
            foreach (var note in closedNotes)
            {
                var title = string.IsNullOrWhiteSpace(note.Title) ? "Untitled Note" : note.Title;
                if (title.Length > 28) title = title.Substring(0, 25) + "...";

                closedMenu.DropDownItems.Add(new ToolStripMenuItem(title, null, (_, _) =>
                {
                    _manager.ReopenNote(note);
                }));
            }
            menu.Items.Add(closedMenu);
        }

        menu.Items.Add(new ToolStripSeparator());

        // Start with Windows toggle
        var startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = IsStartWithWindowsEnabled(),
            CheckOnClick = true
        };
        startWithWindowsItem.CheckedChanged += (_, _) =>
        {
            SetStartWithWindows(startWithWindowsItem.Checked);
        };
        menu.Items.Add(startWithWindowsItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Application.Current.Shutdown()));

        _icon.ContextMenuStrip = menu;
        UpdateTooltip(true);
    }

    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryValueName = "GhostNotes";

    public static bool IsStartWithWindowsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            return key?.GetValue(AppRegistryValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartWithWindows(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(AppRegistryValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppRegistryValueName, false);
            }
        }
        catch { }
    }

    public void UpdateTooltip(bool protectionOn)
    {
        int count = _manager.Windows.Count;
        _icon.Text = $"GhostNotes — {count} active notes — OBS Stealth: {(protectionOn ? "ON" : "OFF")}";
    }

    public void ShowWarning(string message) =>
        _icon.ShowBalloonTip(5000, "GhostNotes", message, ToolTipIcon.Warning);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
