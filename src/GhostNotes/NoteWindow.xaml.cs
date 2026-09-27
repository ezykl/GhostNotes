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
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Brushes = System.Windows.Media.Brushes;
using DataFormats = System.Windows.DataFormats;

namespace GhostNotes;

public partial class NoteWindow : Window
{
    private readonly CaptureGuard _guard;
    private bool _suppressTextEvents;
    private bool _isMouseDownOnHeader;
    private bool _isDragging;
    private System.Windows.Point _mouseDownScreenPos;
    private bool _isPeekState;
    private double _fullDockLeft;
    private double _peekDockLeft;

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

        Left = note.X > 0 ? note.X : 100;
        Top = note.Y > 0 ? note.Y : 100;
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
            Model.RestoreX = Left;
            Model.RestoreY = Top;
        }
        GeometryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadContent()
    {
        _suppressTextEvents = true;
        try
        {
            var range = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);

            if (!string.IsNullOrWhiteSpace(Model.Rtf) && Model.Rtf.TrimStart().StartsWith("{\\rtf"))
            {
                try
                {
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(Model.Rtf));
                    range.Load(ms, DataFormats.Rtf);
                }
                catch
                {
                    range.Text = !string.IsNullOrWhiteSpace(Model.Markdown) ? Model.Markdown : Model.Rtf;
                }
            }
            else
            {
                var text = !string.IsNullOrWhiteSpace(Model.Markdown)
                    ? Model.Markdown
                    : (!string.IsNullOrWhiteSpace(Model.Rtf) ? Model.Rtf : "");
                range.Text = text ?? "";
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

        FlushEditorToModel();
        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(EditorBox);
        var pointer = EditorBox.GetPositionFromPoint(point, snapToText: true);
        if (pointer?.Paragraph is not { } para) return;

        var startRange = new TextRange(para.ContentStart, pointer);
        int clickOffset = startRange.Text.Length;

        // Only toggle if clicked near the checkbox indicator at line start
        if (clickOffset > 8) return;

        foreach (var inline in para.Inlines)
        {
            if (inline is Run r)
            {
                var rText = r.Text;
                int prefixIndex = rText.IndexOf("[ ]", StringComparison.Ordinal);
                if (prefixIndex >= 0 && prefixIndex <= 4)
                {
                    _suppressTextEvents = true;
                    try
                    {
                        r.Text = rText.Substring(0, prefixIndex) + "[x]" + rText.Substring(prefixIndex + 3);
                        new TextRange(para.ContentStart, para.ContentEnd)
                            .ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
                    }
                    finally { _suppressTextEvents = false; }

                    FlushEditorToModel();
                    ModelChanged?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                }

                int checkedIndex = rText.IndexOf("[x]", StringComparison.OrdinalIgnoreCase);
                if (checkedIndex >= 0 && checkedIndex <= 4)
                {
                    _suppressTextEvents = true;
                    try
                    {
                        r.Text = rText.Substring(0, checkedIndex) + "[ ]" + rText.Substring(checkedIndex + 3);
                        new TextRange(para.ContentStart, para.ContentEnd)
                            .ApplyPropertyValue(Inline.TextDecorationsProperty, null);
                    }
                    finally { _suppressTextEvents = false; }

                    FlushEditorToModel();
                    ModelChanged?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                }
                break;
            }
        }
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
            _isMouseDownOnHeader = true;
            _isDragging = false;
            _mouseDownScreenPos = PointToScreen(e.GetPosition(this));
        }
    }

    // ── WINDOW HOVER (PEEK / SLIDE FOR MINIMIZED PILL) ──
    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        if (Model.IsMinimized && _isPeekState && !_isDragging)
        {
            Left = _fullDockLeft;
            _isPeekState = false;
        }
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        if (Model.IsMinimized && !_isPeekState && !_isDragging && !_isMouseDownOnHeader)
        {
            Left = _peekDockLeft;
            _isPeekState = true;
        }
    }

    private void OnHeaderMouseMove(object sender, MouseEventArgs e)
    {
        if (_isMouseDownOnHeader && e.LeftButton == MouseButtonState.Pressed && !_isDragging)
        {
            var currentScreenPos = PointToScreen(e.GetPosition(this));
            var deltaX = currentScreenPos.X - _mouseDownScreenPos.X;
            var deltaY = currentScreenPos.Y - _mouseDownScreenPos.Y;

            // If moved more than 4 pixels, initiate dragging
            if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) > 4)
            {
                _isDragging = true;
                _isPeekState = false;
                try
                {
                    DragMove();
                }
                catch { }

                _isMouseDownOnHeader = false;
                _isDragging = false;
                if (Model.IsMinimized)
                {
                    SnapToNearestEdge();
                }
            }
        }
    }

    private void OnHeaderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isMouseDownOnHeader)
        {
            _isMouseDownOnHeader = false;
            if (!_isDragging && Model.IsMinimized)
            {
                // Clicked without dragging while minimized -> restore note to where it was last minimized!
                SetMinimized(false);
            }
        }
    }

    private void OnRestorePillClicked(object sender, RoutedEventArgs e)
    {
        SetMinimized(false);
    }

    private (double DpiX, double DpiY) GetDpiScale()
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            return (dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0, dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0);
        }
        catch
        {
            return (1.0, 1.0);
        }
    }

    private (double Left, double Top, double Right, double Bottom, double Width, double Height) GetActiveMonitorBoundsDip()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var screen = hwnd != IntPtr.Zero
            ? System.Windows.Forms.Screen.FromHandle(hwnd)
            : System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)Left, (int)Top));

        var (dpiX, dpiY) = GetDpiScale();
        var b = screen.WorkingArea;
        return (
            b.Left / dpiX,
            b.Top / dpiY,
            b.Right / dpiX,
            b.Bottom / dpiY,
            b.Width / dpiX,
            b.Height / dpiY
        );
    }

    private void SnapToNearestEdge()
    {
        try
        {
            var bounds = GetActiveMonitorBoundsDip();

            if (Math.Abs(Left - bounds.Left) < 50)
            {
                _fullDockLeft = bounds.Left + 6;
                _peekDockLeft = bounds.Left - Width + 75;
                Left = _isPeekState ? _peekDockLeft : _fullDockLeft;
            }
            else if (Math.Abs((Left + Width) - bounds.Right) < 50)
            {
                _fullDockLeft = bounds.Right - Width - 6;
                _peekDockLeft = bounds.Right - 75;
                Left = _isPeekState ? _peekDockLeft : _fullDockLeft;
            }

            // Snap to top edge
            if (Math.Abs(Top - bounds.Top) < 40) Top = bounds.Top + 6;
            // Snap to bottom edge
            else if (Math.Abs((Top + Height) - bounds.Bottom) < 40) Top = bounds.Bottom - Height - 6;

            // Keep within working bounds
            Top = Math.Clamp(Top, bounds.Top, bounds.Bottom - Height);
        }
        catch { }
    }

    private void DockToActiveMonitorRightEdge(bool peek = true)
    {
        try
        {
            var bounds = GetActiveMonitorBoundsDip();
            _fullDockLeft = bounds.Right - Width - 6;
            _peekDockLeft = bounds.Right - 75;

            Left = peek ? _peekDockLeft : _fullDockLeft;
            _isPeekState = peek;
            Top = Math.Clamp(Top, bounds.Top + 10, bounds.Bottom - Height - 10);
        }
        catch { }
    }

    private void ClampToActiveMonitor()
    {
        try
        {
            var bounds = GetActiveMonitorBoundsDip();
            Left = Math.Clamp(Left, bounds.Left + 4, bounds.Right - Width - 4);
            Top = Math.Clamp(Top, bounds.Top + 4, bounds.Bottom - Height - 4);
        }
        catch { }
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
            Model.RestoreX = Left;
            Model.RestoreY = Top;

            BodyRow.Height = new GridLength(0);
            Width = 180;
            Height = 48;
            HeaderBorder.CornerRadius = new CornerRadius(11);
            HeaderButtons.Visibility = Visibility.Collapsed;
            PillControls.Visibility = Visibility.Visible;
            ResizeGrips.Visibility = Visibility.Collapsed;

            // Dock to right edge in half-peek state
            DockToActiveMonitorRightEdge(peek: true);
        }
        else
        {
            BodyRow.Height = new GridLength(1, GridUnitType.Star);
            Width = Model.RestoreWidth > 180 ? Model.RestoreWidth : 320;
            Height = Model.RestoreHeight > 100 ? Model.RestoreHeight : 220;
            Left = Model.RestoreX > 0 ? Model.RestoreX : Left;
            Top = Model.RestoreY > 0 ? Model.RestoreY : Top;
            HeaderBorder.CornerRadius = new CornerRadius(11, 11, 0, 0);
            HeaderButtons.Visibility = Visibility.Visible;
            PillControls.Visibility = Visibility.Collapsed;
            ResizeGrips.Visibility = Visibility.Visible;
            _isPeekState = false;

            ClampToActiveMonitor();
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

    public void FlushEditorToModel()
    {
        if (Model is null) return;
        var range = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
        var plainText = range.Text.TrimEnd();
        Model.Markdown = plainText;

        try
        {
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.Rtf);
            Model.Rtf = Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            Model.Rtf = plainText;
        }

        TxtTitle.Text = Model.Title;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        FlushEditorToModel();
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
        if (TxtOpacityVal != null) TxtOpacityVal.Text = $"{(int)(Model.Opacity * 100)}%";
        ApplyGlassBackground();
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

    // ── GLASS BACKGROUND ──
    public void ApplyGlassBackground()
    {
        // Window itself stays 100% opaque so SettingsFlyout and icons remain crisp & solid
        this.Opacity = 1.0;

        var color = (Color)ColorConverter.ConvertFromString(Model.Tint);
        byte alpha = (byte)Math.Clamp((int)(Model.Opacity * 255), 40, 255);
        Glass.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));

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

        // Dynamic Accent Color for Ghost Logo from Note Tint
        Color accentColor = (Model.Tint ?? "").ToUpperInvariant() switch
        {
            "#FFF59D" => Color.FromRgb(245, 158, 11),  // Amber
            "#BBDEFB" => Color.FromRgb(2, 132, 199),   // Sky
            "#C8E6C9" => Color.FromRgb(16, 185, 129),  // Emerald
            "#F8BBD0" => Color.FromRgb(236, 72, 153),  // Rose
            "#D1C4E9" => Color.FromRgb(139, 92, 246),  // Violet
            _ => Color.FromRgb(56, 189, 248)           // Cyan
        };

        if (GhostStroke != null)
        {
            GhostStroke.Fill = new SolidColorBrush(accentColor);
        }
        if (GhostGlow != null)
        {
            GhostGlow.Color = accentColor;
        }
    }

    // ── BOTTOM HOVER TOOLBAR ──
    private void OnBottomAreaMouseEnter(object sender, MouseEventArgs e)
    {
        if (!Model.IsMinimized && HoverToolbar != null)
        {
            HoverToolbar.BeginAnimation(UIElement.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150)));
        }
    }

    private void OnBottomAreaMouseLeave(object sender, MouseEventArgs e)
    {
        if (HoverToolbar != null)
        {
            HoverToolbar.BeginAnimation(UIElement.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200)));
        }
    }

    private void OnToolbarBold(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBold.Execute(null, EditorBox);
        EditorBox.Focus();
    }

    private void OnToolbarItalic(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleItalic.Execute(null, EditorBox);
        EditorBox.Focus();
    }

    private void OnToolbarUnderline(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleUnderline.Execute(null, EditorBox);
        EditorBox.Focus();
    }

    private void OnToolbarStrikethrough(object sender, RoutedEventArgs e)
    {
        var sel = EditorBox.Selection;
        if (!sel.IsEmpty)
        {
            var cur = sel.GetPropertyValue(Inline.TextDecorationsProperty);
            if (cur == TextDecorations.Strikethrough)
                sel.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
            else
                sel.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
        }
        EditorBox.Focus();
    }

    private void OnToolbarBullets(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBullets.Execute(null, EditorBox);
        EditorBox.Focus();
    }

    private void OnToolbarNumbers(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleNumbering.Execute(null, EditorBox);
        EditorBox.Focus();
    }

    private void OnToolbarTask(object sender, RoutedEventArgs e)
    {
        var sel = EditorBox.Selection;
        if (sel.IsEmpty)
        {
            sel.Text = "- [ ] ";
        }
        else
        {
            sel.Text = "- [ ] " + sel.Text;
        }
        EditorBox.Focus();
    }

    private void OnToolbarCode(object sender, RoutedEventArgs e)
    {
        var sel = EditorBox.Selection;
        if (!sel.IsEmpty)
        {
            sel.Text = "`" + sel.Text + "`";
        }
        else
        {
            sel.Text = "`code`";
        }
        EditorBox.Focus();
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
