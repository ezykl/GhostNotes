using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GhostNotes.Interop;
using GhostNotes.Models;
using GhostNotes.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using DataFormats = System.Windows.DataFormats;

namespace GhostNotes;

public partial class NoteWindow : Window
{
    private readonly CaptureGuard _guard;
    private IntPtr _accentPtr = IntPtr.Zero;
    private bool _suppressTextEvents;

    public Note Model { get; }

    public event EventHandler? ModelChanged;
    public event EventHandler? GeometryChanged;
#pragma warning disable CS0067
    public event EventHandler? DeleteRequested;
    public event EventHandler? NewNoteRequested;
#pragma warning restore CS0067

    public NoteWindow(Note note, CaptureGuard guard)
    {
        InitializeComponent();
        Model = note;
        _guard = guard;
        Left = note.X;
        Top = note.Y;
        Width = note.Width;
        Height = note.Height;
        Body.FontSize = Math.Clamp(note.FontSize, FontZoom.Min, FontZoom.Max);
        LoadRtf(note.Rtf);
        ApplyGlassBackground();
        LocationChanged += (_, _) => SyncGeometry();
        SizeChanged += (_, _) => SyncGeometry();
        SourceInitialized += OnSourceInitialized;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TOOLWINDOW);
        _guard.ApplyToWindow(hwnd);
        TryAcrylic(hwnd);
    }

    public void ReapplyProtectionAndShow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) _guard.ApplyToWindow(hwnd);
        Show();
    }

    private void SyncGeometry()
    {
        Model.X = Left;
        Model.Y = Top;
        Model.Width = Width;
        Model.Height = Height;
        GeometryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDragStripDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        if (!ctrl) return;
        e.Handled = true;
        int next = FontZoom.Clamp((int)Body.FontSize, e.Delta > 0 ? 1 : -1);
        if (next == (int)Body.FontSize) return;
        Body.FontSize = next;
        Model.FontSize = next;
        Body.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, (double)next);
        Model.Rtf = SaveRtf();
        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBodyTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressTextEvents) return;
        Model.Rtf = SaveRtf();
        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.Tag is not string dir) return;
        double left = Left, top = Top, w = Width, h = Height;
        if (dir.Contains('W'))
        {
            w = Math.Max(Width - e.HorizontalChange, MinWidth);
            left = Left + Width - w;
        }
        if (dir.Contains('E')) w = Math.Max(Width + e.HorizontalChange, MinWidth);
        if (dir.Contains('N'))
        {
            h = Math.Max(Height - e.VerticalChange, MinHeight);
            top = Top + Height - h;
        }
        if (dir.Contains('S')) h = Math.Max(Height + e.VerticalChange, MinHeight);
        Left = left;
        Top = top;
        Width = w;
        Height = h;
    }

    private void ApplyGlassBackground()
    {
        var color = (Color)ColorConverter.ConvertFromString(Model.Tint);
        byte alpha = (byte)Math.Round(Model.Opacity * 255);
        Glass.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private void TryAcrylic(IntPtr hwnd)
    {
        if (_accentPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_accentPtr);
            _accentPtr = IntPtr.Zero;
        }
        int size = Marshal.SizeOf(typeof(NativeMethods.AccentPolicy));
        var accent = new NativeMethods.AccentPolicy
        {
            AccentState = NativeMethods.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            GradientColor = TintToAbgr(Model.Tint, 0x66)
        };
        _accentPtr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(accent, _accentPtr, false);
        var data = new NativeMethods.WindowCompositionAttributeData
        {
            Attribute = NativeMethods.WCA_ACCENT_POLICY,
            Data = _accentPtr,
            SizeOfData = size
        };
        if (NativeMethods.SetWindowCompositionAttribute(hwnd, ref data) != 0)
        {
            Marshal.FreeHGlobal(_accentPtr);
            _accentPtr = IntPtr.Zero;
        }
    }

    private static uint TintToAbgr(string hex, byte alpha)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return (uint)((alpha << 24) | (c.B << 16) | (c.G << 8) | c.R);
    }

    private void LoadRtf(string rtf)
    {
        _suppressTextEvents = true;
        try
        {
            var range = new TextRange(Body.Document.ContentStart, Body.Document.ContentEnd);
            if (string.IsNullOrEmpty(rtf))
            {
                range.Text = "";
            }
            else
            {
                using var ms = new MemoryStream(Encoding.Default.GetBytes(rtf));
                range.Load(ms, DataFormats.Rtf);
            }
        }
        catch (ArgumentException)
        {
            var range = new TextRange(Body.Document.ContentStart, Body.Document.ContentEnd);
            range.Text = "";
        }
        finally
        {
            _suppressTextEvents = false;
        }
    }

    private string SaveRtf()
    {
        var range = new TextRange(Body.Document.ContentStart, Body.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Rtf);
        return Encoding.Default.GetString(ms.ToArray());
    }
}
