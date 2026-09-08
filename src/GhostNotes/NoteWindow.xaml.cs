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
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;

namespace GhostNotes;

public partial class NoteWindow : Window
{
    private readonly CaptureGuard _guard;
    private IntPtr _accentPtr = IntPtr.Zero;
    private bool _suppressTextEvents;
    private System.Windows.Point _dragStartPos;

    public Note Model { get; }

    public event EventHandler? ModelChanged;
    public event EventHandler? GeometryChanged;
    public event EventHandler? CloseRequested;
    public event EventHandler? PermanentDeleteRequested;
    public event EventHandler? NewNoteRequested;

    public NoteWindow(Note note, CaptureGuard guard)
    {
        Model = note;
        _guard = guard;
        InitializeComponent();

        Left = note.X;
        Top = note.Y;
        Width = note.Width > 0 ? note.Width : 320;
        Height = note.Height > 0 ? note.Height : 220;

        LoadContent();
        ApplyGlassBackground();

        LocationChanged += (_, _) => SyncGeometry();
        SizeChanged += (_, _) => SyncGeometry();
        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += OnPreviewKeyDown;

        if (note.IsMinimized)
        {
            SetMinimized(true);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Hide from Alt-Tab & Taskbar
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TOOLWINDOW);

        // Re-enable capture protection (WDA_EXCLUDEFROMCAPTURE)
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
        if (Model is null) return;
        if (!Model.IsMinimized)
        {
            Model.X = Left;
            Model.Y = Top;
            Model.Width = Width;
            Model.Height = Height;
            Model.RestoreWidth = Width;
            Model.RestoreHeight = Height;
        }
        else
        {
            Model.X = Left;
            Model.Y = Top;
        }
        GeometryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadContent()
    {
        _suppressTextEvents = true;
        try
        {
            var range = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
            var text = !string.IsNullOrWhiteSpace(Model.Markdown)
                ? Model.Markdown
                : (!string.IsNullOrWhiteSpace(Model.Rtf) && !Model.Rtf.StartsWith("{\\rtf") ? Model.Rtf : "");

            if (string.IsNullOrWhiteSpace(text))
            {
                range.Text = "# Quick Note\n\nStart typing here...";
            }
            else
            {
                range.Text = text;
            }

            EditorBox.FontSize = Math.Clamp(Model.FontSize, 10, 28);
            TxtTitle.Text = Model.Title;
        }
        finally
        {
            _suppressTextEvents = false;
        }
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextEvents || Model is null) return;

        var range = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
        var text = range.Text.TrimEnd();
        Model.Markdown = text;
        Model.Rtf = text;
        TxtTitle.Text = Model.Title;

        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Formatting shortcuts
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.B)
            {
                EditingCommands.ToggleBold.Execute(null, EditorBox);
                e.Handled = true;
            }
            else if (e.Key == Key.I)
            {
                EditingCommands.ToggleItalic.Execute(null, EditorBox);
                e.Handled = true;
            }
            else if (e.Key == Key.U)
            {
                EditingCommands.ToggleUnderline.Execute(null, EditorBox);
                e.Handled = true;
            }
            else if (e.Key == Key.N)
            {
                NewNoteRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }
    }

    // ── HEADER & DRAG & PILL RESTORE ──
    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _dragStartPos = e.GetPosition(this);

            if (Model.IsMinimized)
            {
                // Dragging or clicking the minimized pill
                DragMove();

                // Snap to screen edge if near
                SnapToNearestEdge();

                // If user didn't drag far, treat as click to restore
                var endPos = e.GetPosition(this);
                if (Math.Abs(endPos.X - _dragStartPos.X) < 5 && Math.Abs(endPos.Y - _dragStartPos.Y) < 5)
                {
                    SetMinimized(false);
                }
            }
            else
            {
                DragMove();
            }
        }
    }

    private void SnapToNearestEdge()
    {
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var bounds = screen.WorkingArea;
            if (Left >= bounds.Left - 50 && Left <= bounds.Right + 50)
            {
                // Snap to left edge
                if (Math.Abs(Left - bounds.Left) < 30) Left = bounds.Left + 4;
                // Snap to right edge
                if (Math.Abs((Left + Width) - bounds.Right) < 30) Left = bounds.Right - Width - 4;
                // Snap to top
                if (Math.Abs(Top - bounds.Top) < 30) Top = bounds.Top + 4;
                // Snap to bottom
                if (Math.Abs((Top + Height) - bounds.Bottom) < 30) Top = bounds.Bottom - Height - 4;
                break;
            }
        }
    }

    // ── MINIMIZE / EXPAND GHOST PILL ──
    public void SetMinimized(bool min)
    {
        Model.IsMinimized = min;
        if (min)
        {
            SettingsFlyout.Visibility = Visibility.Collapsed;
            if (Width > 200) Model.RestoreWidth = Width;
            if (Height > 60) Model.RestoreHeight = Height;

            BodyRow.Height = new GridLength(0);
            Width = 180;
            Height = 34;
            HeaderBorder.CornerRadius = new CornerRadius(11);
            HeaderButtons.Visibility = Visibility.Collapsed;
            PillHint.Visibility = Visibility.Visible;
            ResizeGrips.Visibility = Visibility.Collapsed;
        }
        else
        {
            BodyRow.Height = new GridLength(1, GridUnitType.Star);
            Width = Model.RestoreWidth > 180 ? Model.RestoreWidth : 320;
            Height = Model.RestoreHeight > 100 ? Model.RestoreHeight : 220;
            HeaderBorder.CornerRadius = new CornerRadius(11, 11, 0, 0);
            HeaderButtons.Visibility = Visibility.Visible;
            PillHint.Visibility = Visibility.Collapsed;
            ResizeGrips.Visibility = Visibility.Visible;
        }
        SyncGeometry();
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        SetMinimized(true);
    }

    private void OnNewNoteClicked(object sender, RoutedEventArgs e)
    {
        NewNoteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── SETTINGS FLYOUT ──
    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (SettingsFlyout.Visibility == Visibility.Visible)
        {
            SettingsFlyout.Visibility = Visibility.Collapsed;
        }
        else
        {
            SliderOpacity.Value = Model.Opacity;
            TxtOpacityVal.Text = $"{(int)(Model.Opacity * 100)}%";
            SliderFontSize.Value = Model.FontSize;
            TxtFontSizeVal.Text = $"{Model.FontSize}pt";
            HighlightSelectedTint(Model.Tint);
            HighlightSelectedFontColor(Model.FontColor ?? "#1E293B");
            SettingsFlyout.Visibility = Visibility.Visible;
        }
    }

    private void OnCloseSettingsClicked(object sender, RoutedEventArgs e)
    {
        SettingsFlyout.Visibility = Visibility.Collapsed;
    }

    private void OnSelectTint(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string hex)
        {
            Model.Tint = hex;
            HighlightSelectedTint(hex);
            ApplyGlassBackground();
            RefreshAcrylic();
            ModelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HighlightSelectedTint(string hex)
    {
        foreach (var child in TintWrapPanel.Children)
        {
            if (child is Border b)
            {
                bool isSelected = string.Equals(b.Tag as string, hex, StringComparison.OrdinalIgnoreCase);
                b.BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : Brushes.Transparent;
            }
        }
    }

    private void OnSelectFontColor(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string hex)
        {
            Model.FontColor = hex;
            HighlightSelectedFontColor(hex);
            ApplyGlassBackground();
            ModelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HighlightSelectedFontColor(string hex)
    {
        foreach (var child in FontColorWrapPanel.Children)
        {
            if (child is Border b)
            {
                bool isSelected = string.Equals(b.Tag as string, hex, StringComparison.OrdinalIgnoreCase);
                b.BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : Brushes.Transparent;
            }
        }
    }

    private void OnOpacityValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Model is null) return;
        Model.Opacity = Math.Round(e.NewValue, 2);
        this.Opacity = Model.Opacity;
        if (TxtOpacityVal != null) TxtOpacityVal.Text = $"{(int)(Model.Opacity * 100)}%";
        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFontSizeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Model is null) return;
        Model.FontSize = (int)e.NewValue;
        if (TxtFontSizeVal != null) TxtFontSizeVal.Text = $"{Model.FontSize}pt";
        EditorBox.FontSize = Model.FontSize;
        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPermanentDeleteClicked(object sender, RoutedEventArgs e)
    {
        var res = System.Windows.MessageBox.Show(
            "Are you sure you want to permanently delete this note?",
            "Delete Note",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            PermanentDeleteRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── GLASS & ACRYLIC ──
    public void ApplyGlassBackground()
    {
        // Change window opacity directly for smooth, natural window transparency
        this.Opacity = Math.Clamp(Model.Opacity, 0.2, 1.0);

        var color = (Color)ColorConverter.ConvertFromString(Model.Tint);
        Glass.Background = new SolidColorBrush(color);

        var fontHex = string.IsNullOrWhiteSpace(Model.FontColor) ? "#1E293B" : Model.FontColor;
        var fontColor = (Color)ColorConverter.ConvertFromString(fontHex);
        var fontBrush = new SolidColorBrush(fontColor);

        EditorBox.Foreground = fontBrush;
        try
        {
            var range = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
            range.ApplyPropertyValue(TextElement.ForegroundProperty, fontBrush);
        }
        catch { }
    }

    private void RefreshAcrylic()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) TryAcrylic(hwnd);
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
                GradientColor = TintToAbgr(Model.Tint, 0x55)
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

    // ── RESIZE ──
    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (Model.IsMinimized) return;
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
}
