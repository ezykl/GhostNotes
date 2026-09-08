using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace GhostNotes.Services;

public static class IconBuilder
{
    public static void EnsureIcon(string outputPath)
    {
        if (File.Exists(outputPath)) return;

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        int[] sizes = { 16, 32, 48, 64, 128, 256 };
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICO Header
        bw.Write((short)0); // reserved
        bw.Write((short)1); // type 1 = ICO
        bw.Write((short)sizes.Length);

        // Prepare PNG buffers
        byte[][] pngBuffers = new byte[sizes.Length][];
        int offset = 6 + (16 * sizes.Length);

        for (int i = 0; i < sizes.Length; i++)
        {
            int sz = sizes[i];
            using var bmp = RenderGhostLogo(sz);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngBuffers[i] = ms.ToArray();
        }

        // Write Directory entries
        for (int i = 0; i < sizes.Length; i++)
        {
            int sz = sizes[i];
            bw.Write((byte)(sz >= 256 ? 0 : sz)); // width
            bw.Write((byte)(sz >= 256 ? 0 : sz)); // height
            bw.Write((byte)0); // color count
            bw.Write((byte)0); // reserved
            bw.Write((short)1); // color planes
            bw.Write((short)32); // bit count
            bw.Write((int)pngBuffers[i].Length); // bytes in resource
            bw.Write((int)offset); // offset of image data
            offset += pngBuffers[i].Length;
        }

        // Write Image data
        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write(pngBuffers[i]);
        }
    }

    public static Bitmap RenderGhostLogo(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float s = size / 512f;

        // Document background with folded corner
        using (var docPath = new GraphicsPath())
        {
            float l = 104 * s, t = 76 * s, r = 408 * s, b = 424 * s, f = 64 * s;
            float rad = 28 * s;
            docPath.AddArc(l, t, rad * 2, rad * 2, 180, 90);
            docPath.AddArc(r - rad * 2, t, rad * 2, rad * 2, 270, 90);
            docPath.AddLine(r, t + rad, r, b - f);
            docPath.AddLine(r - f, b, l + rad, b);
            docPath.AddArc(l, b - rad * 2, rad * 2, rad * 2, 90, 90);
            docPath.CloseFigure();

            using var brush = new LinearGradientBrush(
                new PointF(l, t), new PointF(r, b),
                Color.FromArgb(235, 56, 189, 248),
                Color.FromArgb(215, 14, 165, 233));
            g.FillPath(brush, docPath);

            using var pen = new Pen(Color.FromArgb(255, 186, 230, 253), Math.Max(1.5f, 5f * s));
            g.DrawPath(pen, docPath);
        }

        // Folded tab corner
        using (var foldPath = new GraphicsPath())
        {
            float r = 408 * s, b = 424 * s, f = 64 * s;
            foldPath.AddLine(r - f, b, r - f, b - f);
            foldPath.AddLine(r - f, b - f, r, b - f);
            foldPath.CloseFigure();
            using var foldBrush = new SolidBrush(Color.FromArgb(245, 125, 211, 252));
            g.FillPath(foldBrush, foldPath);
        }

        // Ghost body
        using (var ghostPath = new GraphicsPath())
        {
            float cx = 256 * s, cy = 205 * s;
            float gw = 190 * s, gh = 210 * s;
            ghostPath.AddArc(cx - gw / 2, cy - gh / 2, gw, gh * 0.75f, 180, 180);
            
            // Wavy bottom
            float botY = cy + gh * 0.45f;
            float leftX = cx - gw / 2;
            float rightX = cx + gw / 2;
            ghostPath.AddLine(rightX, cy - gh * 0.1f, rightX, botY);
            
            float waveW = gw / 4f;
            ghostPath.AddBezier(rightX, botY, rightX - waveW * 0.5f, botY + 18 * s, rightX - waveW, botY, rightX - waveW, botY);
            ghostPath.AddBezier(rightX - waveW, botY, rightX - waveW * 1.5f, botY - 14 * s, rightX - waveW * 2, botY, rightX - waveW * 2, botY);
            ghostPath.AddBezier(rightX - waveW * 2, botY, rightX - waveW * 2.5f, botY + 18 * s, rightX - waveW * 3, botY, rightX - waveW * 3, botY);
            ghostPath.AddBezier(rightX - waveW * 3, botY, rightX - waveW * 3.5f, botY - 14 * s, leftX, botY, leftX, botY);
            ghostPath.CloseFigure();

            using var ghostBrush = new LinearGradientBrush(
                new PointF(cx, cy - gh / 2), new PointF(cx, botY),
                Color.FromArgb(250, 255, 255, 255),
                Color.FromArgb(225, 224, 242, 254));
            g.FillPath(ghostBrush, ghostPath);

            using var ghostPen = new Pen(Color.FromArgb(240, 186, 230, 253), Math.Max(1.2f, 3.5f * s));
            g.DrawPath(ghostPen, ghostPath);

            // Ghost cute eyes
            float eyeR = Math.Max(2f, 12f * s);
            float eyeY = cy - 25 * s;
            using var eyeBrush = new SolidBrush(Color.FromArgb(240, 15, 23, 42));
            g.FillEllipse(eyeBrush, cx - 35 * s - eyeR, eyeY - eyeR, eyeR * 2, eyeR * 2);
            g.FillEllipse(eyeBrush, cx + 35 * s - eyeR, eyeY - eyeR, eyeR * 2, eyeR * 2);

            // Eye highlights
            float hlR = Math.Max(0.8f, 4f * s);
            using var hlBrush = new SolidBrush(Color.White);
            g.FillEllipse(hlBrush, cx - 33 * s, eyeY - eyeR + 2 * s, hlR * 2, hlR * 2);
            g.FillEllipse(hlBrush, cx + 37 * s, eyeY - eyeR + 2 * s, hlR * 2, hlR * 2);

            // Cute smile
            using var smilePen = new Pen(Color.FromArgb(230, 15, 23, 42), Math.Max(1.2f, 3.5f * s));
            smilePen.StartCap = LineCap.Round;
            smilePen.EndCap = LineCap.Round;
            g.DrawArc(smilePen, cx - 18 * s, eyeY + 4 * s, 36 * s, 24 * s, 25, 130);
        }

        return bmp;
    }
}
