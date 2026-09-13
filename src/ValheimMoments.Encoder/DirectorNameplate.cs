using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

internal static class DirectorNameplate
{
    internal static byte[] Create(int width, int height, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            throw new ArgumentException("Invalid recorder label");
        int w = Math.Max(1, width * 3 / 5), h = Math.Max(4, height / 9);
        using (var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var gold = new SolidBrush(Color.FromArgb(255, 241, 211, 151)))
        using (var rule = new Pen(Color.FromArgb(235, 183, 141, 64), Math.Max(1, height / 540f)))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
        {
            graphics.Clear(Color.FromArgb(180, 8, 12, 13));
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.DrawLine(rule, w * 0.12f, 1, w * 0.88f, 1);
            using (var font = new Font("Georgia", Math.Max(8, height / 27f), FontStyle.Bold, GraphicsUnit.Pixel))
                graphics.DrawString(name, font, gold, new RectangleF(8, 2, Math.Max(1, w - 16), h - 2), format);
            var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { var pixels = new byte[w * h * 4]; Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); return pixels; }
            finally { bitmap.UnlockBits(data); }
        }
    }
    internal static void Apply(byte[] frame, int width, int height, byte[] plate)
    {
        int w = Math.Max(1, width * 3 / 5), h = Math.Max(4, height / 9), left = (width - w) / 2, top = height - h - height / 12;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int p = (y * w + x) * 4, f = ((top + y) * width + left + x) * 4, alpha = plate[p + 3];
            for (int c = 0; c < 3; c++) frame[f + c] = (byte)((plate[p + c] * alpha + frame[f + c] * (255 - alpha) + 127) / 255);
        }
    }
}
