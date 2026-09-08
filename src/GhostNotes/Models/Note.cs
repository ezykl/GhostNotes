using System;

namespace GhostNotes.Models;

public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TabId { get; set; } = "default";
    public string Markdown { get; set; } = "";
    public string Rtf { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 220;
    public string Tint { get; set; } = "#FFF59D";
    public string FontColor { get; set; } = "#1E293B";
    public double Opacity { get; set; } = 0.85;
    public int FontSize { get; set; } = 14;
    public bool IsDeployed { get; set; } = true;
    public bool IsClosed { get; set; } = false;
    public bool IsMinimized { get; set; } = false;
    public double RestoreWidth { get; set; } = 320;
    public double RestoreHeight { get; set; } = 220;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string Title
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Markdown))
            {
                if (!string.IsNullOrWhiteSpace(Rtf) && !Rtf.StartsWith("{\\rtf"))
                    return Rtf.Trim();
                return "Untitled Note";
            }
            var lines = Markdown.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var trimmed = l.Trim();
                if (trimmed.StartsWith('#')) return trimmed.TrimStart('#', ' ').Trim();
                if (!string.IsNullOrWhiteSpace(trimmed)) return trimmed.Length > 40 ? trimmed.Substring(0, 37) + "..." : trimmed;
            }
            return "Untitled Note";
        }
    }
}
