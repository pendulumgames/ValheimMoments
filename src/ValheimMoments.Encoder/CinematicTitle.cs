using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

internal static class CinematicTitle
{
    internal static byte[] Create(int width, int height, string caption)
    {
        if (caption == null || caption.Length > 512 || caption.Contains("\r")) throw new ArgumentException("Invalid cinematic caption");
        var lines = caption.Split('\n');
        if (lines.Length > 3) throw new ArgumentException("Too many cinematic caption lines");
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var title = new Font("Georgia", Math.Max(8, height / 17f), FontStyle.Bold, GraphicsUnit.Pixel))
        using (var detail = new Font("Georgia", Math.Max(7, height / 28f), FontStyle.Regular, GraphicsUnit.Pixel))
        using (var gold = new SolidBrush(Color.FromArgb(255, 241, 211, 151)))
        using (var white = new SolidBrush(Color.FromArgb(255, 237, 236, 226)))
        using (var shadow = new SolidBrush(Color.FromArgb(230, 0, 0, 0)))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
        {
            graphics.Clear(Color.Transparent); graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float y = height * 0.63f;
            for (int i = 0; i < lines.Length; i++)
            {
                var font = i == 0 ? title : detail;
                float h = font.Size * 1.45f;
                var rect = new RectangleF(width * 0.08f, y, width * 0.84f, h);
                var offset = rect; offset.Offset(1, 2); graphics.DrawString(lines[i], font, shadow, offset, format);
                graphics.DrawString(lines[i], font, i == 0 ? gold : white, rect, format); y += h;
            }
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { var pixels = new byte[width * height * 4]; Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); return pixels; }
            finally { bitmap.UnlockBits(data); }
        }
    }
    internal static void Apply(byte[] frame, int width, int height, byte[] title, int milliseconds, bool letterbox)
    {
        // Preserve the 16:9 file canvas; slide black bars inward to a 2.39:1 window.
        float progress = Math.Max(0, Math.Min(1, milliseconds / 850f));
        progress = progress * progress * (3 - 2 * progress);
        int bar = letterbox ? (int)Math.Round(Math.Max(0, (height - width / 2.39) / 2) * progress) : 0;
        float opacity = Math.Max(0, Math.Min(1, (milliseconds - 500) / 600f));
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int p = (y * width + x) * 4;
            if (y < bar || y >= height - bar) { frame[p] = frame[p + 1] = frame[p + 2] = 0; frame[p + 3] = 255; continue; }
            if (title == null) continue;
            int alpha = (int)(title[p + 3] * opacity);
            if (alpha == 0) continue;
            for (int c = 0; c < 3; c++) frame[p + c] = (byte)((title[p + c] * alpha + frame[p + c] * (255 - alpha) + 127) / 255);
        }
    }
}
