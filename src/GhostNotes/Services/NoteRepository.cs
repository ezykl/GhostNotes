using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GhostNotes.Models;

namespace GhostNotes.Services;

public sealed class NoteRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GhostNotes", "notes");

    private readonly string _directory;

    public NoteRepository(string? directory = null)
    {
        _directory = directory ?? DefaultDirectory;
    }

    public List<Note> LoadAll()
    {
        var notes = new List<Note>();
        if (!Directory.Exists(_directory)) return notes;
        foreach (var path in Directory.GetFiles(_directory, "*.json"))
        {
            Note? note = null;
            try
            {
                note = JsonSerializer.Deserialize<Note>(File.ReadAllText(path), JsonOpts);
            }
            catch (JsonException) { }
            if (note is null)
            {
                TryRenameCorrupt(path);
                continue;
            }
            notes.Add(note);
        }
        return notes;
    }

    public void Save(Note note)
    {
        note.UpdatedAt = DateTime.UtcNow;
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(note.Id), JsonSerializer.Serialize(note, JsonOpts));
    }

    public void Delete(string id)
    {
        var path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathFor(string id) => Path.Combine(_directory, id + ".json");

    private void TryRenameCorrupt(string path)
    {
        try
        {
            var bad = path + ".bad";
            if (File.Exists(bad)) File.Delete(bad);
            File.Move(path, bad);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
