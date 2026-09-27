using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GhostNotes.Interop;
using GhostNotes.Models;
using GhostNotes.Services;
using Point = System.Windows.Point;

namespace GhostNotes;

public sealed class NoteManager : IDisposable
{
    private const double CascadeOffset = 28;

    private readonly NoteRepository _repo;
    private readonly CaptureGuard _guard;
    private readonly List<NoteWindow> _windows = new();
    private readonly List<Note> _notes = new();
    private readonly Dictionary<string, AutosaveScheduler> _schedulers = new();
    public static readonly string[] ColorPalette =
    {
        "#BBDEFB", // Sky Blue
        "#C8E6C9", // Mint Green
        "#F8BBD0", // Pastel Pink
        "#D1C4E9", // Lavender
        "#FFFFFF", // White
        "#FFF59D"  // Soft Yellow (displayed after others, completing the cycle)
    };

    private int _colorIndex = 0;
    private Point _nextCascade = new(100, 100);

    public string GetNextTint()
    {
        var tint = ColorPalette[Math.Abs(_colorIndex) % ColorPalette.Length];
        _colorIndex++;
        return tint;
    }

    public IReadOnlyList<NoteWindow> Windows => _windows;
    public IReadOnlyList<Note> Notes => _notes;
    public IEnumerable<Note> ClosedNotes => _notes.Where(n => n.IsClosed);

    public event EventHandler? NotesStateChanged;

    public bool AnyVisible
    {
        get
        {
            foreach (var w in _windows)
                if (w.IsVisible) return true;
            return false;
        }
    }

    public NoteManager(NoteRepository repo, CaptureGuard guard)
    {
        _repo = repo;
        _guard = guard;
    }

    public void RestoreAll()
    {
        _notes.Clear();
        var loaded = _repo.LoadAll().ToList();
        _notes.AddRange(loaded);
        _colorIndex = _notes.Count;

        int restoredCount = 0;
        foreach (var note in _notes)
        {
            var c = PositionClamp.Clamp(note.X, note.Y, note.Width, note.Height, ScreenRects());
            note.X = c.X;
            note.Y = c.Y;
            note.Width = c.W;
            note.Height = c.H;

            if (!note.IsClosed)
            {
                Attach(new NoteWindow(note, _guard));
                restoredCount++;
            }
        }

        // If all notes were closed, restore the most recent note with user content instead of losing content
        if (restoredCount == 0)
        {
            var noteToRestore = _notes
                .Where(n => !IsDefaultTemplate(n.Markdown))
                .OrderByDescending(n => n.UpdatedAt)
                .FirstOrDefault()
                ?? _notes.OrderByDescending(n => n.UpdatedAt).FirstOrDefault();

            if (noteToRestore != null)
            {
                noteToRestore.IsClosed = false;
                _repo.Save(noteToRestore);
                Attach(new NoteWindow(noteToRestore, _guard));
            }
            else
            {
                CreateNote();
            }
        }

        NotesStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public static bool IsDefaultTemplate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var nonTemplateLines = lines.Where(l =>
            !l.Equals("# Quick Note", StringComparison.OrdinalIgnoreCase) &&
            !l.Equals("Start typing here...", StringComparison.OrdinalIgnoreCase) &&
            !l.Equals("- Type markdown directly here", StringComparison.OrdinalIgnoreCase) &&
            !l.Equals("- Minimized notes stick to screen edges", StringComparison.OrdinalIgnoreCase) &&
            !l.Equals("- OBS & screen capture cannot see this!", StringComparison.OrdinalIgnoreCase)
        );
        return !nonTemplateLines.Any();
    }

    public NoteWindow CreateNote()
    {
        var note = new Note
        {
            X = _nextCascade.X,
            Y = _nextCascade.Y,
            Width = 320,
            Height = 220,
            Markdown = "",
            Tint = GetNextTint(),
            IsClosed = false,
            IsMinimized = false
        };
        _notes.Add(note);
        _repo.Save(note);

        _nextCascade = new Point(
            (_nextCascade.X + CascadeOffset) % Math.Max(300, SystemParameters.WorkArea.Width - 360),
            (_nextCascade.Y + CascadeOffset) % Math.Max(300, SystemParameters.WorkArea.Height - 280));

        var window = Attach(new NoteWindow(note, _guard));
        window.Activate();
        NotesStateChanged?.Invoke(this, EventArgs.Empty);
        return window;
    }

    public void CloseNote(NoteWindow window)
    {
        window.FlushEditorToModel();
        if (_schedulers.TryGetValue(window.Model.Id, out var scheduler))
        {
            scheduler.FlushNow();
        }
        window.Model.IsClosed = true;
        _repo.Save(window.Model);

        _windows.Remove(window);
        window.Close();

        NotesStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PermanentlyDeleteNote(NoteWindow window)
    {
        var id = window.Model.Id;
        var note = _notes.FirstOrDefault(n => n.Id == id);
        if (note != null) _notes.Remove(note);

        _windows.Remove(window);
        window.Close();

        _repo.Delete(id);
        if (_schedulers.Remove(id, out var scheduler))
        {
            scheduler.Cancel();
            scheduler.Dispose();
        }

        if (_windows.Count == 0 && _notes.Count(n => !n.IsClosed) == 0)
        {
            CreateNote();
        }

        NotesStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReopenNote(Note note)
    {
        note.IsClosed = false;
        _repo.Save(note);

        var existing = _windows.FirstOrDefault(w => w.Model.Id == note.Id);
        if (existing != null)
        {
            existing.ReapplyProtectionAndShow();
            existing.Activate();
        }
        else
        {
            var win = Attach(new NoteWindow(note, _guard));
            win.Activate();
        }

        NotesStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleVisibility()
    {
        if (AnyVisible)
        {
            foreach (var w in _windows) w.Hide();
        }
        else
        {
            foreach (var w in _windows) w.ReapplyProtectionAndShow();
        }
    }

    public void SaveAll()
    {
        foreach (var scheduler in _schedulers.Values) scheduler.FlushNow();
    }

    public void Dispose() => SaveAll();

    private NoteWindow Attach(NoteWindow window)
    {
        if (!_windows.Contains(window)) _windows.Add(window);
        window.ModelChanged += (_, _) => OnNoteModelChanged(window.Model);
        window.GeometryChanged += (_, _) => OnNoteModelChanged(window.Model);
        window.CloseRequested += (_, _) => CloseNote(window);
        window.PermanentDeleteRequested += (_, _) => PermanentlyDeleteNote(window);
        window.NewNoteRequested += (_, _) => CreateNote();
        window.Show();
        return window;
    }

    private void OnNoteModelChanged(Note note)
    {
        if (!_schedulers.TryGetValue(note.Id, out var scheduler))
        {
            scheduler = new AutosaveScheduler(() => _repo.Save(note), TimeSpan.FromMilliseconds(500));
            _schedulers[note.Id] = scheduler;
        }
        scheduler.Trigger();
        NotesStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static (double X, double Y, double W, double H)[] ScreenRects()
    {
        var list = new List<(double, double, double, double)>();
        double dpiX = 1.0, dpiY = 1.0;
        try
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            if (primary != null && primary.WorkingArea.Width > 0 && SystemParameters.WorkArea.Width > 0)
            {
                dpiX = primary.WorkingArea.Width / SystemParameters.WorkArea.Width;
                dpiY = primary.WorkingArea.Height / SystemParameters.WorkArea.Height;
                if (dpiX <= 0) dpiX = 1.0;
                if (dpiY <= 0) dpiY = 1.0;
            }
        }
        catch { }

        foreach (var s in System.Windows.Forms.Screen.AllScreens)
            list.Add((s.WorkingArea.X / dpiX, s.WorkingArea.Y / dpiY, s.WorkingArea.Width / dpiX, s.WorkingArea.Height / dpiY));
        return list.ToArray();
    }
}
