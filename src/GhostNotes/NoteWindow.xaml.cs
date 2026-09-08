using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GhostNotes.Interop;
using GhostNotes.Models;
using GhostNotes.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Orientation = System.Windows.Controls.Orientation;

namespace GhostNotes;

public partial class NoteWindow : Window
{
    private readonly CaptureGuard _guard;
    private IntPtr _accentPtr = IntPtr.Zero;

    public Note Model { get; }

    public event EventHandler? ModelChanged;
    public event EventHandler? GeometryChanged;
    public event EventHandler? DeleteRequested;
    public event EventHandler? EditRequested;

    public NoteWindow(Note note, CaptureGuard guard)
    {
        Model = note;
        _guard = guard;
        InitializeComponent();

        Left = note.X;
        Top = note.Y;
        Width = note.Width;
        Height = note.Height;

        RefreshContent();
        ApplyGlassBackground();

        LocationChanged += (_, _) => SyncGeometry();
        SizeChanged += (_, _) => SyncGeometry();
        SourceInitialized += OnSourceInitialized;
        PreviewMouseWheel += OnPreviewMouseWheel;
        ContextMenu = BuildContextMenu();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Hide from Alt-Tab & Taskbar for sleek overlay behavior
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TOOLWINDOW);

        // Re-enable capture protection (WDA_EXCLUDEFROMCAPTURE) for true OBS invisibility
        _guard.ApplyToWindow(hwnd);
        TryAcrylic(hwnd);
    }

    public void ReapplyProtectionAndShow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) _guard.ApplyToWindow(hwnd);
        Show();
    }

    public void RefreshContent()
    {
        TxtTitle.Text = Model.Title;
        Title = Model.Title;
        
        var content = !string.IsNullOrWhiteSpace(Model.Markdown)
            ? Model.Markdown
            : (!string.IsNullOrWhiteSpace(Model.Rtf) && !Model.Rtf.StartsWith("{\\rtf") ? Model.Rtf : "# Welcome to GhostNotes\n\n- Hidden from OBS & screen sharing\n- Edit notes in the Manager\n- Drag top bar to move\n- Resize from any edge");

        Viewer.Document = MarkdownRenderer.Render(content, Model.FontSize);
    }

    private void SyncGeometry()
    {
        if (Model is null) return;
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

    private void OnViewerDoubleClick(object sender, MouseButtonEventArgs e)
    {
        EditRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditClicked(object sender, RoutedEventArgs e)
    {
        EditRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        if (!ctrl) return;
        e.Handled = true;
        int next = FontZoom.Clamp((int)Model.FontSize, e.Delta > 0 ? 1 : -1);
        if (next == Model.FontSize) return;
        Model.FontSize = next;
        RefreshContent();
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

    private void OnCloseClicked(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(this, EventArgs.Empty);

    public void ApplyGlassBackground()
    {
        var color = (Color)ColorConverter.ConvertFromString(Model.Tint);
        byte alpha = (byte)Math.Clamp(Math.Round(Model.Opacity * 255), 160, 255);
        Glass.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private void TryAcrylic(IntPtr hwnd)
    {
        try
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
        catch { }
    }

    private static uint TintToAbgr(string hex, byte alpha)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return (uint)((alpha << 24) | (c.B << 16) | (c.G << 8) | c.R);
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var edit = new MenuItem { Header = "Edit in Manager (Double-Click)" };
        edit.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(edit);

        var delete = new MenuItem { Header = "Close Note" };
        delete.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(delete);

        menu.Items.Add(new Separator());

        var opacityItem = new MenuItem { StaysOpenOnClick = true };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = "Opacity  ", VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider { MinWidth = 120, Minimum = 0.3, Maximum = 1.0, Value = Model.Opacity };
        slider.ValueChanged += (_, e2) =>
        {
            Model.Opacity = Math.Round(e2.NewValue, 2);
            ApplyGlassBackground();
            ModelChanged?.Invoke(this, EventArgs.Empty);
        };
        panel.Children.Add(slider);
        opacityItem.Header = panel;
        menu.Items.Add(opacityItem);

        var resetFont = new MenuItem { Header = "Reset Font Size" };
        resetFont.Click += (_, _) =>
        {
            Model.FontSize = 14;
            RefreshContent();
            ModelChanged?.Invoke(this, EventArgs.Empty);
        };
        menu.Items.Add(resetFont);

        return menu;
    }
}
