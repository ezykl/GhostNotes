using System;
using System.Windows.Interop;
using GhostNotes.Interop;

namespace GhostNotes.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyIdNewNote = 0xB001;
    private const int HotkeyIdToggle = 0xB002;

    public static readonly HotkeySpec PreferredNewNote =
        new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, NativeMethods.VK_N, "Ctrl+Alt+N");
    public static readonly HotkeySpec PreferredToggle =
        new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, NativeMethods.VK_S, "Ctrl+Alt+S");
    public static readonly HotkeySpec FallbackNewNote =
        new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT,
            NativeMethods.VK_N, "Ctrl+Alt+Shift+N");
    public static readonly HotkeySpec FallbackToggle =
        new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT,
            NativeMethods.VK_S, "Ctrl+Alt+Shift+S");

    public event EventHandler? NewNoteRequested;
    public event EventHandler? ToggleVisibilityRequested;
    public event EventHandler<string>? HotkeyNotice;

    private HwndSource? _source;

    public (HotkeySpec NewNote, HotkeySpec Toggle) Register()
    {
        _source = new HwndSource(new HwndSourceParameters("GhostNotesHotkeys")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: pure message-only window, zero taskbar presence
            Width = 0,
            Height = 0
        });
        _source.AddHook(WndProc);

        var specNew = HotkeySelection.Select(PreferredNewNote, FallbackNewNote,
            s => TryRegister(HotkeyIdNewNote, s));
        var specToggle = HotkeySelection.Select(PreferredToggle, FallbackToggle,
            s => TryRegister(HotkeyIdToggle, s));

        if (specNew is null)
            HotkeyNotice?.Invoke(this, "Could not register the New Note hotkey — both combos are taken.");
        if (specToggle is null)
            HotkeyNotice?.Invoke(this, "Could not register the Show/Hide hotkey — both combos are taken.");
        if (specNew is not null && specNew != PreferredNewNote)
            HotkeyNotice?.Invoke(this, $"New Note hotkey fell back to {specNew.Display}.");
        if (specToggle is not null && specToggle != PreferredToggle)
            HotkeyNotice?.Invoke(this, $"Show/Hide hotkey fell back to {specToggle.Display}.");

        return (specNew ?? PreferredNewNote, specToggle ?? PreferredToggle);
    }

    private bool TryRegister(int id, HotkeySpec spec) =>
        NativeMethods.RegisterHotKey(_source!.Handle, id, spec.Modifiers, spec.VirtualKey);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            switch (wParam.ToInt64())
            {
                case HotkeyIdNewNote:
                    NewNoteRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                case HotkeyIdToggle:
                    ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyIdNewNote);
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyIdToggle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}
