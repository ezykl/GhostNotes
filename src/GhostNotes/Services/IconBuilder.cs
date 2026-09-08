using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace GhostNotes.Services;

public static class IconBuilder
{
    public static void EnsureIcon(string outputPath, bool force = false)
    {
        if (File.Exists(outputPath) && !force) return;

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
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return RenderGhostLogoSta(size);
        }

        Bitmap? bmp = null;
        var t = new Thread(() => { bmp = RenderGhostLogoSta(size); });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return bmp ?? new Bitmap(size, size);
    }

    private static Bitmap RenderGhostLogoSta(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double scale = size / 26.0;
            dc.PushTransform(new ScaleTransform(scale, scale));

            var geoBody = Geometry.Parse("M8.14779 8.97833C8.14779 5.91296 10.6328 3.42798 13.6981 3.42798C16.7635 3.42798 19.2485 5.91296 19.2485 8.97834V11.9777C19.2485 16.7242 15.4007 20.572 10.6542 20.572H4.7511C6.81602 19.6584 8.14779 17.6129 8.14779 15.3549V8.97833Z");
            var geoStroke = Geometry.Parse("M19.2487 11.9775V8.97851C19.2487 6.009 16.9168 3.58415 13.9843 3.43537L13.6987 3.4279C10.6334 3.4279 8.14812 5.91313 8.14812 8.97851V15.3545L8.14404 15.566C8.06394 17.7396 6.75176 19.687 4.7514 20.572H10.6542L10.8765 20.5693C15.5204 20.4515 19.2487 16.6498 19.2487 11.9775ZM19.9449 11.9775C19.9449 17.1085 15.7852 21.2682 10.6542 21.2682H4.7514C4.42246 21.2682 4.13827 21.0379 4.07015 20.7161C4.00212 20.3942 4.16903 20.068 4.46992 19.9349C6.28268 19.1328 7.45192 17.3368 7.45192 15.3545V8.97851C7.45192 5.52863 10.2489 2.73169 13.6987 2.73169C17.1485 2.73184 19.9449 5.52872 19.9449 8.97851V11.9775Z");
            var geoEyes = Geometry.Parse("M13.1163 7.70898C13.7329 7.70898 14.2334 8.35251 14.2335 9.14648C14.2335 9.94065 13.733 10.585 13.1163 10.585C12.4998 10.5847 12.0001 9.9405 12.0001 9.14648C12.0003 8.35265 12.4999 7.70922 13.1163 7.70898ZM16.6613 7.70898C17.2778 7.70898 17.7783 8.35249 17.7784 9.14648C17.7784 9.94065 17.2779 10.585 16.6613 10.585C16.0447 10.5847 15.545 9.94051 15.545 9.14648C15.5452 8.35263 16.0448 7.7092 16.6613 7.70898Z");

            var bodyBrush = new LinearGradientBrush(
                System.Windows.Media.Color.FromArgb(245, 255, 255, 255),
                System.Windows.Media.Color.FromArgb(210, 96, 96, 96),
                new Point(0.5, 0.1),
                new Point(0.5, 0.9));

            var strokeBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248));
            var eyesBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 15, 23, 42));

            dc.DrawGeometry(bodyBrush, null, geoBody);
            dc.DrawGeometry(strokeBrush, null, geoStroke);
            dc.DrawGeometry(eyesBrush, null, geoEyes);
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }
}
