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
    private const double CascadeOffset = 24;

    private readonly NoteRepository _repo;
    private readonly CaptureGuard _guard;
    private readonly List<NoteWindow> _windows = new();
    private readonly List<Note> _notes = new();
    private readonly Dictionary<string, AutosaveScheduler> _schedulers = new();
    private Point _nextCascade = new(120, 120);

    public IReadOnlyList<NoteWindow> Windows => _windows;
    public IReadOnlyList<Note> Notes => _notes;

    public event EventHandler<Note>? OpenManagerForNoteRequested;

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

        foreach (var note in _notes)
        {
            var c = PositionClamp.Clamp(note.X, note.Y, note.Width, note.Height, ScreenRects());
            note.X = c.X;
            note.Y = c.Y;
            note.Width = c.W;
            note.Height = c.H;

            if (note.IsDeployed)
            {
                Attach(new NoteWindow(note, _guard));
            }
        }
    }

    public Note CreateNoteForTab(string tabId)
    {
        var note = new Note
        {
            TabId = tabId,
            X = _nextCascade.X,
            Y = _nextCascade.Y,
            Markdown = "# New Note\n\nStart writing Markdown here...",
            IsDeployed = true
        };
        _notes.Add(note);
        _repo.Save(note);

        _nextCascade = new Point(
            (_nextCascade.X + CascadeOffset) % Math.Max(300, SystemParameters.WorkArea.Width - 340),
            (_nextCascade.Y + CascadeOffset) % Math.Max(300, SystemParameters.WorkArea.Height - 260));

        var window = Attach(new NoteWindow(note, _guard));
        return note;
    }

    public NoteWindow CreateNote()
    {
        var note = new Note
        {
            X = _nextCascade.X,
            Y = _nextCascade.Y,
            Markdown = "# Welcome to GhostNotes\n\n- Protected from screen recording & OBS\n- Clean Markdown sticky note\n- Double-click to edit in Manager",
            IsDeployed = true
        };
        _notes.Add(note);
        _repo.Save(note);
        var window = Attach(new NoteWindow(note, _guard));
        _nextCascade = new Point(
            (_nextCascade.X + CascadeOffset) % Math.Max(300, SystemParameters.WorkArea.Width - 340),
            (_nextCascade.Y + CascadeOffset) % Math.Max(300, SystemParameters.WorkArea.Height - 260));
        window.Activate();
        return window;
    }

    public void NotifyNoteChanged(Note note)
    {
        if (!_notes.Contains(note)) _notes.Add(note);

        if (!_schedulers.TryGetValue(note.Id, out var scheduler))
        {
            scheduler = new AutosaveScheduler(() => _repo.Save(note), TimeSpan.FromMilliseconds(500));
            _schedulers[note.Id] = scheduler;
        }
        scheduler.Trigger();

        // Live re-render overlay window if open
        var win = _windows.FirstOrDefault(w => w.Model.Id == note.Id);
        if (win != null)
        {
            win.RefreshContent();
            win.ApplyGlassBackground();
        }
    }

    public void DeleteNoteById(string id)
    {
        var note = _notes.FirstOrDefault(n => n.Id == id);
        if (note != null) _notes.Remove(note);

        var win = _windows.FirstOrDefault(w => w.Model.Id == id);
        if (win != null)
        {
            _windows.Remove(win);
            win.Close();
        }

        _repo.Delete(id);
        if (_schedulers.Remove(id, out var scheduler))
        {
            scheduler.Cancel();
            scheduler.Dispose();
        }
    }

    public void DeleteNote(NoteWindow window)
    {
        DeleteNoteById(window.Model.Id);
    }

    public void DeployActiveTab(string tabId)
    {
        bool hadVisible = false;
        foreach (var w in _windows.Where(w => w.Model.TabId == tabId).ToList())
        {
            if (w.IsVisible)
            {
                hadVisible = true;
                w.Hide();
            }
        }

        if (hadVisible) return;

        // Show or create overlays for this tab
        var notesInTab = _notes.Where(n => n.TabId == tabId || n.TabId == "default").ToList();
        foreach (var note in notesInTab)
        {
            var win = _windows.FirstOrDefault(w => w.Model.Id == note.Id);
            if (win == null)
            {
                win = Attach(new NoteWindow(note, _guard));
            }
            win.ReapplyProtectionAndShow();
        }
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
        window.DeleteRequested += (_, _) => DeleteNote(window);
        window.EditRequested += (_, _) => OpenManagerForNoteRequested?.Invoke(this, window.Model);
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
    }

    private static (double X, double Y, double W, double H)[] ScreenRects()
    {
        var list = new List<(double, double, double, double)>();
        foreach (var s in System.Windows.Forms.Screen.AllScreens)
            list.Add((s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height));
        return list.ToArray();
    }
}
