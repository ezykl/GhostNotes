using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GhostNotes.Interop;
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
            Text = "GhostNotes",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        hotkeys.HotkeyNotice += (_, message) => ShowWarning(message);
        guard.ProtectionStatusChanged += (_, on) => UpdateTooltip(on);
        UpdateTooltip(guard.IsProtected);
    }

    private static Icon BuildIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(255, 245, 157));
            g.FillRectangle(brush, 1, 1, 13, 13);
            using var pen = new Pen(Color.FromArgb(90, 70, 0), 2);
            g.DrawRectangle(pen, 1, 1, 13, 13);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("New Note", null, (_, _) => _manager.CreateNote()));
        menu.Items.Add(new ToolStripMenuItem("Show/Hide All Notes", null, (_, _) => _manager.ToggleVisibility()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Application.Current.Shutdown()));
        return menu;
    }

    public void UpdateTooltip(bool protectionOn)
    {
        _icon.Text =
            $"GhostNotes — {_manager.Windows.Count} notes — Capture protection: {(protectionOn ? "ON" : "OFF")}";
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
