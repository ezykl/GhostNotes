using System;

namespace GhostNotes.Models;

public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Rtf { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 220;
    public string Tint { get; set; } = "#FFF59D";
    public double Opacity { get; set; } = 0.85;
    public int FontSize { get; set; } = 14;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
