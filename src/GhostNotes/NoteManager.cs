using System;
using System.Collections.Generic;
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
    private readonly Dictionary<string, AutosaveScheduler> _schedulers = new();
    private Point _nextCascade = new(96, 96);

    public IReadOnlyList<NoteWindow> Windows => _windows;

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
        foreach (var note in _repo.LoadAll())
        {
            var c = PositionClamp.Clamp(note.X, note.Y, note.Width, note.Height, ScreenRects());
            note.X = c.X;
            note.Y = c.Y;
            note.Width = c.W;
            note.Height = c.H;
            Attach(new NoteWindow(note, _guard));
        }
    }

    public NoteWindow CreateNote()
    {
        var note = new Note { X = _nextCascade.X, Y = _nextCascade.Y };
        _repo.Save(note);
        var window = Attach(new NoteWindow(note, _guard));
        _nextCascade = new Point(
            (_nextCascade.X + CascadeOffset) % SystemParameters.WorkArea.Width,
            (_nextCascade.Y + CascadeOffset) % SystemParameters.WorkArea.Height);
        window.Activate();
        return window;
    }

    public void DeleteNote(NoteWindow window)
    {
        _repo.Delete(window.Model.Id);
        if (_schedulers.Remove(window.Model.Id, out var scheduler))
        {
            scheduler.Cancel();
            scheduler.Dispose();
        }
        _windows.Remove(window);
        window.Close();
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
        _windows.Add(window);
        window.ModelChanged += (_, _) => OnNoteModelChanged(window.Model);
        window.GeometryChanged += (_, _) => OnNoteModelChanged(window.Model);
        window.DeleteRequested += (_, _) => DeleteNote(window);
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
    }

    private static (double X, double Y, double W, double H)[] ScreenRects()
    {
        var list = new List<(double, double, double, double)>();
        foreach (var s in System.Windows.Forms.Screen.AllScreens)
            list.Add((s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height));
        return list.ToArray();
    }
}
