using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GhostNotes.Interop;
using GhostNotes.Models;
using GhostNotes.Services;
using RadioButton = System.Windows.Controls.RadioButton;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace GhostNotes.Views;

public partial class ManagerWindow : Window
{
    private readonly NoteManager _noteManager;
    private readonly CaptureGuard _guard;

    private readonly List<Tab> _tabs = new();
    private Tab? _selectedTab;
    private Note? _selectedNote;
    private bool _isPreviewMode;
    private bool _suppressTextChange;

    public ManagerWindow(NoteManager noteManager, CaptureGuard guard)
    {
        _noteManager = noteManager;
        _guard = guard;
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) _guard.ApplyToWindow(hwnd);
        };

        KeyDown += OnWindowKeyDown;
        Closing += (s, e) =>
        {
            // Close hides to system tray rather than terminating process
            e.Cancel = true;
            Hide();
        };

        InitializeDefaultTabs();
        LoadTab(_tabs[0]);
    }

    private void InitializeDefaultTabs()
    {
        _tabs.Clear();
        _tabs.Add(new Tab { Name = "Standup", IsDeployed = true, Order = 0 });
        _tabs.Add(new Tab { Name = "Research", IsDeployed = false, Order = 1 });
        _tabs.Add(new Tab { Name = "Personal", IsDeployed = false, Order = 2 });
    }

    public void SelectNote(Note note)
    {
        Show();
        Activate();
        _selectedNote = note;
        LoadNote(note);
    }

    private void RenderTabStrip()
    {
        TabStripPanel.Children.Clear();
        foreach (var tab in _tabs)
        {
            var rb = new RadioButton
            {
                Content = tab.Name,
                Style = (Style)FindResource("TabBtn"),
                IsChecked = (tab == _selectedTab),
                Tag = tab
            };
            rb.Checked += (s, e) =>
            {
                if (rb.Tag is Tab t) LoadTab(t);
            };
            TabStripPanel.Children.Add(rb);
        }
    }

    private void LoadTab(Tab tab)
    {
        _selectedTab = tab;
        RenderTabStrip();

        var notes = _noteManager.Notes.Where(n => n.TabId == tab.Id || n.TabId == "default").ToList();
        if (notes.Count == 0)
        {
            var newNote = _noteManager.CreateNoteForTab(tab.Id);
            notes.Add(newNote);
        }

        RenderNotesList(notes);
        LoadNote(notes[0]);
    }

    private void RenderNotesList(List<Note> notes)
    {
        NotesListPanel.Children.Clear();
        foreach (var note in notes)
        {
            var card = new RadioButton
            {
                Style = (Style)FindResource("NoteCardBtn"),
                IsChecked = (note == _selectedNote),
                Tag = note
            };

            var sp = new StackPanel();
            var titleBlock = new TextBlock
            {
                Text = note.Title,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var previewText = string.IsNullOrWhiteSpace(note.Markdown) ? "(Empty note)" : note.Markdown.Replace("\n", " ").Trim();
            if (previewText.Length > 30) previewText = previewText.Substring(0, 27) + "...";
            var snippetBlock = new TextBlock
            {
                Text = previewText,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            sp.Children.Add(titleBlock);
            sp.Children.Add(snippetBlock);
            card.Content = sp;

            card.Checked += (s, e) =>
            {
                if (card.Tag is Note n) LoadNote(n);
            };

            NotesListPanel.Children.Add(card);
        }
    }

    private void LoadNote(Note note)
    {
        _selectedNote = note;
        _suppressTextChange = true;

        EditorBox.Text = !string.IsNullOrWhiteSpace(note.Markdown)
            ? note.Markdown
            : (!string.IsNullOrWhiteSpace(note.Rtf) && !note.Rtf.StartsWith("{\\rtf") ? note.Rtf : "");

        _suppressTextChange = false;

        SliderOpacity.Value = note.Opacity;
        TxtOpacityVal.Text = $"{(int)(note.Opacity * 100)}%";
        SliderFontSize.Value = note.FontSize;
        TxtFontSizeVal.Text = $"{note.FontSize}pt";

        HighlightSelectedTint(note.Tint);

        if (_isPreviewMode)
        {
            UpdatePreview();
        }
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChange || _selectedNote is null) return;

        _selectedNote.Markdown = EditorBox.Text;
        _selectedNote.Rtf = EditorBox.Text;
        _noteManager.NotifyNoteChanged(_selectedNote);

        // Update list card title
        if (_selectedTab != null)
        {
            var notes = _noteManager.Notes.Where(n => n.TabId == _selectedTab.Id || n.TabId == "default").ToList();
            RenderNotesList(notes);
        }

        if (_isPreviewMode) UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_selectedNote is null) return;
        PreviewViewer.Document = MarkdownRenderer.Render(EditorBox.Text, _selectedNote.FontSize, Brushes.White);
    }

    private void OnTogglePreviewClicked(object sender, RoutedEventArgs e)
    {
        TogglePreview();
    }

    private void TogglePreview()
    {
        _isPreviewMode = !_isPreviewMode;
        if (_isPreviewMode)
        {
            UpdatePreview();
            EditorBox.Visibility = Visibility.Collapsed;
            PreviewPanel.Visibility = Visibility.Visible;
            TxtPreview.Text = "Edit (Ctrl+P)";
        }
        else
        {
            PreviewPanel.Visibility = Visibility.Collapsed;
            EditorBox.Visibility = Visibility.Visible;
            TxtPreview.Text = "Preview (Ctrl+P)";
            EditorBox.Focus();
        }
    }

    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.P)
        {
            e.Handled = true;
            TogglePreview();
        }
    }

    // ── FORMATTING HELPERS ──
    private void WrapSelection(string prefix, string suffix)
    {
        if (_isPreviewMode) TogglePreview();
        int start = EditorBox.SelectionStart;
        int len = EditorBox.SelectionLength;
        string text = EditorBox.Text;

        if (len > 0)
        {
            string selected = text.Substring(start, len);
            EditorBox.Text = text.Substring(0, start) + prefix + selected + suffix + text.Substring(start + len);
            EditorBox.SelectionStart = start + prefix.Length;
            EditorBox.SelectionLength = len;
        }
        else
        {
            EditorBox.Text = text.Substring(0, start) + prefix + suffix + text.Substring(start);
            EditorBox.SelectionStart = start + prefix.Length;
            EditorBox.SelectionLength = 0;
        }
        EditorBox.Focus();
    }

    private void InsertAtLineStart(string prefix)
    {
        if (_isPreviewMode) TogglePreview();
        int caret = EditorBox.SelectionStart;
        int lineIndex = EditorBox.GetLineIndexFromCharacterIndex(caret);
        int lineStart = EditorBox.GetCharacterIndexFromLineIndex(lineIndex);

        EditorBox.Text = EditorBox.Text.Insert(lineStart, prefix);
        EditorBox.SelectionStart = caret + prefix.Length;
        EditorBox.Focus();
    }

    private void OnBoldClicked(object sender, RoutedEventArgs e) => WrapSelection("**", "**");
    private void OnItalicClicked(object sender, RoutedEventArgs e) => WrapSelection("*", "*");
    private void OnStrikethroughClicked(object sender, RoutedEventArgs e) => WrapSelection("~~", "~~");
    private void OnH1Clicked(object sender, RoutedEventArgs e) => InsertAtLineStart("# ");
    private void OnH2Clicked(object sender, RoutedEventArgs e) => InsertAtLineStart("## ");
    private void OnH3Clicked(object sender, RoutedEventArgs e) => InsertAtLineStart("### ");
    private void OnBulletListClicked(object sender, RoutedEventArgs e) => InsertAtLineStart("- ");
    private void OnNumberedListClicked(object sender, RoutedEventArgs e) => InsertAtLineStart("1. ");
    private void OnTaskCheckboxClicked(object sender, RoutedEventArgs e) => InsertAtLineStart("- [ ] ");
    private void OnCodeClicked(object sender, RoutedEventArgs e) => WrapSelection("`", "`");
    private void OnQuoteClicked(object sender, RoutedEventArgs e) => InsertAtLineStart("> ");
    private void OnLinkClicked(object sender, RoutedEventArgs e) => WrapSelection("[", "](https://)");
    private void OnHrClicked(object sender, RoutedEventArgs e) => InsertAtLineStart("---\n");

    // ── NOTE SETTINGS ──
    private void OnSelectTint(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string hex && _selectedNote != null)
        {
            _selectedNote.Tint = hex;
            HighlightSelectedTint(hex);
            _noteManager.NotifyNoteChanged(_selectedNote);
        }
    }

    private void HighlightSelectedTint(string hex)
    {
        foreach (var child in TintPanel.Children)
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
        if (_selectedNote is null) return;
        _selectedNote.Opacity = Math.Round(e.NewValue, 2);
        if (TxtOpacityVal != null) TxtOpacityVal.Text = $"{(int)(_selectedNote.Opacity * 100)}%";
        _noteManager.NotifyNoteChanged(_selectedNote);
    }

    private void OnFontSizeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_selectedNote is null) return;
        _selectedNote.FontSize = (int)e.NewValue;
        if (TxtFontSizeVal != null) TxtFontSizeVal.Text = $"{_selectedNote.FontSize}pt";
        _noteManager.NotifyNoteChanged(_selectedNote);
        if (_isPreviewMode) UpdatePreview();
    }

    private void OnDeleteNoteClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedNote is null) return;
        var toDelete = _selectedNote;
        _noteManager.DeleteNoteById(toDelete.Id);
        if (_selectedTab != null) LoadTab(_selectedTab);
    }

    private void OnAddNoteClicked(object sender, RoutedEventArgs e)
    {
        var tabId = _selectedTab?.Id ?? "default";
        var note = _noteManager.CreateNoteForTab(tabId);
        if (_selectedTab != null) LoadTab(_selectedTab);
        SelectNote(note);
    }

    private void OnAddTabClicked(object sender, RoutedEventArgs e)
    {
        var newTab = new Tab
        {
            Name = $"Tab {_tabs.Count + 1}",
            Order = _tabs.Count,
            IsDeployed = false
        };
        _tabs.Add(newTab);
        LoadTab(newTab);
    }

    private void OnDeployTabClicked(object sender, RoutedEventArgs e)
    {
        _noteManager.DeployActiveTab(_selectedTab?.Id ?? "default");
    }

    private void OnHideToTrayClicked(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
