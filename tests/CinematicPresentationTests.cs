using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

internal static class CinematicPresentationTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Main(string[] args)
    {
        const int w = 640, h = 360;
        var original = new byte[w * h * 4];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        { int p = (y * w + x) * 4; original[p] = 60; original[p + 1] = (byte)(75 + y / 8); original[p + 2] = 24; original[p + 3] = 255; }
        var title = CinematicTitle.Create(w, h, "Eikthyr\n2 stars  |  Max health: 4,500");
        var frame = (byte[])original.Clone(); CinematicTitle.Apply(frame, w, h, title, 0, true);
        Check(Convert.ToBase64String(frame) == Convert.ToBase64String(original), "Initial frame must remain full 16:9");
        frame = (byte[])original.Clone(); CinematicTitle.Apply(frame, w, h, title, 425, true);
        Check(frame[0] == 0 && frame[(30 * w) * 4] == 60, "Half-open bars interpolate inward");
        frame = (byte[])original.Clone(); CinematicTitle.Apply(frame, w, h, title, 1200, true);
        Check(frame[0] == 0 && frame[((h - 1) * w) * 4] == 0, "Both letterbox bars must be black");
        Check(frame[(h / 2 * w) * 4] == 60, "Center scene is preserved");
        int changed = 0;
        for (int y = 210; y < 300; y++) for (int x = 60; x < w - 60; x++) { int p = (y * w + x) * 4; if (frame[p + 2] != original[p + 2]) changed++; }
        Check(changed > 100, "Boss title and stats must be visible");
        using (var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            var bits = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(frame, 0, bits.Scan0, frame.Length); } finally { bitmap.UnlockBits(bits); }
            bitmap.Save(Path.Combine(args[0], "cinematic-title-preview.png"), ImageFormat.Png);
        }
        frame = (byte[])original.Clone(); CinematicTitle.Apply(frame, w, h, title, 1200, false);
        Check(frame[0] == original[0], "Letterbox switch preserves full canvas");
        bool rejected = false; try { CinematicTitle.Create(w, h, "a\nb\nc\nd"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Reject unbounded caption lines");
        Console.WriteLine("PASS: cinematic title visibility, animated letterboxing, full-canvas option and caption bounds.");
    }
}
