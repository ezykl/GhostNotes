using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GhostNotes.Interop;
using GhostNotes.Services;
using Application = System.Windows.Application;

namespace GhostNotes;

public sealed class TrayController : IDisposable
{
    private readonly NoteManager _manager;
    private readonly Action _openManager;
    private readonly NotifyIcon _icon;
    private bool _disposed;

    public TrayController(NoteManager manager, HotkeyService hotkeys, CaptureGuard guard, Action? openManager = null)
    {
        _manager = manager;
        _openManager = openManager ?? (() => { });

        _icon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "GhostNotes — Protected",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _icon.DoubleClick += (_, _) => _openManager();
        hotkeys.HotkeyNotice += (_, message) => ShowWarning(message);
        guard.ProtectionStatusChanged += (_, on) => UpdateTooltip(on);
        UpdateTooltip(guard.IsProtected);
    }

    private static Icon BuildIcon()
    {
        try
        {
            // Check local Assets directory
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(appDir, "Assets", "GhostNotes.ico");
            if (File.Exists(path))
            {
                return new Icon(path, 32, 32);
            }

            // Or render on the fly from IconBuilder vector geometry
            using var bmp = IconBuilder.RenderGhostLogo(32);
            return Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Open Manager", null, (_, _) => _openManager());
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("New Note (Ctrl+Alt+N)", null, (_, _) => _manager.CreateNote()));
        menu.Items.Add(new ToolStripMenuItem("Show / Hide Overlays (Ctrl+Alt+S)", null, (_, _) => _manager.ToggleVisibility()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Application.Current.Shutdown()));
        return menu;
    }

    public void UpdateTooltip(bool protectionOn)
    {
        _icon.Text =
            $"GhostNotes — {_manager.Windows.Count} notes — OBS Protection: {(protectionOn ? "ON" : "OFF")}";
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
