using System.Collections.Generic;
using System.Linq;

namespace GhostNotes.Services;

public static class PositionClamp
{
    private const double Margin = 24;

    public static (double X, double Y, double W, double H) Clamp(
        double x, double y, double w, double h,
        IReadOnlyList<(double X, double Y, double W, double H)> screens)
    {
        var original = (x, y, w, h);
        if (screens.Count == 0) return original;

        foreach (var s in screens)
        {
            bool intersects =
                x < s.X + s.W && x + w > s.X &&
                y < s.Y + s.H && y + h > s.Y;
            if (intersects) return original;
        }

        var best = screens.OrderBy(s => DistanceToScreen(x, y, w, h, s)).First();
        double outW = System.Math.Min(w, System.Math.Max(best.W - 2 * Margin, 180));
        double outH = System.Math.Min(h, System.Math.Max(best.H - 2 * Margin, 120));
        return (best.X + Margin, best.Y + Margin, outW, outH);
    }

    private static double DistanceToScreen(
        double x, double y, double w, double h,
        (double X, double Y, double W, double H) s)
    {
        double dx = x < s.X ? s.X - (x + w) : x > s.X + s.W ? x - (s.X + s.W) : 0;
        double dy = y < s.Y ? s.Y - (y + h) : y > s.Y + s.H ? y - (s.Y + s.H) : 0;
        return dx * dx + dy * dy;
    }
}

public static class FontZoom
{
    public const int Min = 8;
    public const int Max = 48;

    public static int Clamp(int current, int delta) =>
        System.Math.Clamp(current + delta, Min, Max);
}
