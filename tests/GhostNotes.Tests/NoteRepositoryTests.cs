using System;
using System.IO;
using System.Text.Json;
using GhostNotes.Models;
using GhostNotes.Services;
using Xunit;

public sealed class NoteRepositoryTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "gn_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var repo = new NoteRepository(_dir);
        var note = new Note
        {
            Rtf = "{\\rtf1\\ansi hello}",
            X = 12.5,
            Y = 40,
            Width = 300,
            Height = 400,
            Tint = "#BBDEFB",
            Opacity = 0.7,
            FontSize = 18
        };
        repo.Save(note);

        var loaded = repo.LoadAll();
        var n = Assert.Single(loaded);
        Assert.Equal(note.Id, n.Id);
        Assert.Equal("{\\rtf1\\ansi hello}", n.Rtf);
        Assert.Equal(12.5, n.X);
        Assert.Equal(40, n.Y);
        Assert.Equal(300, n.Width);
        Assert.Equal(400, n.Height);
        Assert.Equal("#BBDEFB", n.Tint);
        Assert.Equal(0.7, n.Opacity);
        Assert.Equal(18, n.FontSize);
    }

    [Fact]
    public void CorruptFile_RenamedToBad_AndOthersStillLoad()
    {
        Directory.CreateDirectory(_dir);
        var good = JsonSerializer.Serialize(new Note { Rtf = "keep" });
        File.WriteAllText(Path.Combine(_dir, "good.json"), good);
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{{{ not json");

        var repo = new NoteRepository(_dir);
        var loaded = repo.LoadAll();

        var n = Assert.Single(loaded);
        Assert.Equal("keep", n.Rtf);
        Assert.True(File.Exists(Path.Combine(_dir, "bad.json.bad")));
        Assert.False(File.Exists(Path.Combine(_dir, "bad.json")));
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var repo = new NoteRepository(_dir);
        var note = new Note();
        repo.Save(note);
        repo.Delete(note.Id);
        Assert.Empty(repo.LoadAll());
    }

    [Fact]
    public void LoadAll_MissingDirectory_ReturnsEmpty()
    {
        Assert.Empty(new NoteRepository(_dir).LoadAll());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
