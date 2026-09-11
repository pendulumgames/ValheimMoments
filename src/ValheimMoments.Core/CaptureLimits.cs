using System;

namespace ValheimMoments.Core
{
    public enum CaptureSizePreset { Tiny, Small, Medium, Balanced, Large, Ultra, Custom }
    public static class CaptureSizes
    {
        public static void Resolve(CaptureSizePreset preset, int screenWidth, int screenHeight, int customWidth, int customHeight, out int width, out int height)
        {
            if (preset == CaptureSizePreset.Custom) { width = customWidth; height = customHeight; return; }
            int[] widths = { 480, 640, 854, 960, 1280, 1920 }, heights = { 270, 360, 480, 540, 720, 1080 };
            int index = (int)preset;
            if (index < 0 || index >= widths.Length) index = 1;
            double ratio = screenWidth > 0 && screenHeight > 0 ? (double)screenWidth / screenHeight : 16.0 / 9;
            ratio = Math.Max(0.5, Math.Min(3, ratio));
            Dimensions(ratio, (int)Math.Round(Math.Sqrt((double)widths[index] * heights[index] * ratio)), out width, out height);
        }
        internal static void Dimensions(double ratio, int requestedWidth, out int width, out int height)
        {
            int minimum = Math.Max(480, (int)Math.Ceiling(270 * ratio));
            int maximum = Math.Min(1920, (int)Math.Floor(1080 * ratio));
            width = Math.Max(minimum, Math.Min(maximum, requestedWidth));
            height = Math.Max(270, Math.Min(1080, (int)Math.Round(width / ratio)));
        }
    }
    public sealed class CaptureLimits
    {
        public int Width, Height, FPS, Quality, BudgetMiB;
        public long PoolBytes, ClipBytes;
        private static int Clamp(int n, int min, int max) { return Math.Max(min, Math.Min(max, n)); }
        public static double Seconds(double n, double fallback, double min, double max)
        { return double.IsNaN(n) || double.IsInfinity(n) ? fallback : Math.Max(min, Math.Min(max, n)); }

        public static CaptureLimits Fit(int width, int height, int fps, int quality, int budget, double pre, double maximumPost)
        {
            var result = new CaptureLimits { Width = Clamp(width, 480, 1920), Height = Clamp(height, 270, 1080),
                FPS = Clamp(fps, 1, 30), Quality = Clamp(quality, 1, 100), BudgetMiB = Clamp(budget, 48, 512) };
            pre = Seconds(pre, 5, 1, 30); maximumPost = Seconds(maximumPost, 4, 0, 30);
            double ratio = Math.Max(0.5, Math.Min(3, (double)result.Width / result.Height));
            CaptureSizes.Dimensions(ratio, result.Width, out result.Width, out result.Height);
            int minimumWidth, minimumHeight;
            CaptureSizes.Dimensions(ratio, 0, out minimumWidth, out minimumHeight);
            while (true)
            {
                long pixels = (long)result.Width * result.Height * 4;
                long before = (long)Math.Ceiling(pre * result.FPS), after = (long)Math.Ceiling(maximumPost * result.FPS);
                result.PoolBytes = pixels * (2 * before + after + 1);
                result.ClipBytes = pixels * (before + after);
                if (result.PoolBytes <= result.BudgetMiB * 1048576L && result.ClipBytes <= 256L * 1048576) return result;
                if (result.Width == minimumWidth && result.Height == minimumHeight)
                {
                    // A tall minimum canvas may still exceed the smallest budget.
                    // Fall back to a 480x270 padded canvas, never stretch the source.
                    if (result.FPS <= 1)
                    {
                        if (result.Width == 480 && result.Height == 270) throw new InvalidOperationException("Minimum capture cannot fit its memory budget.");
                        ratio = 16.0 / 9; minimumWidth = 480; minimumHeight = 270;
                        result.Width = 480; result.Height = 270; continue;
                    }
                    result.FPS--;
                    continue;
                }
                CaptureSizes.Dimensions(ratio, (int)(result.Width * 0.9), out result.Width, out result.Height);
            }
        }
    }
}
