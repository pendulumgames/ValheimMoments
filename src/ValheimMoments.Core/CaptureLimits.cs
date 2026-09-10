using System;

namespace ValheimMoments.Core
{
    public sealed class CaptureLimits
    {
        public int Width, Height, FPS, Quality, BudgetMiB;
        public long PoolBytes, ClipBytes;
        private static int Clamp(int n, int min, int max) { return Math.Max(min, Math.Min(max, n)); }
        public static double Seconds(double n, double fallback, double min, double max)
        { return double.IsNaN(n) || double.IsInfinity(n) ? fallback : Math.Max(min, Math.Min(max, n)); }

        public static CaptureLimits Fit(int width, int height, int fps, int quality, int budget, double pre, double maximumPost)
        {
            var result = new CaptureLimits { Width = Clamp(width, 16, 1920), Height = Clamp(height, 16, 1080),
                FPS = Clamp(fps, 1, 30), Quality = Clamp(quality, 1, 100), BudgetMiB = Clamp(budget, 16, 512) };
            pre = Seconds(pre, 5, 1, 30); maximumPost = Seconds(maximumPost, 4, 0, 30);
            // Portrait through ultrawide, but no extreme strips. Reduce, never enlarge.
            result.Width = Math.Min(result.Width, result.Height * 3);
            result.Height = Math.Min(result.Height, result.Width * 2);
            while (true)
            {
                long pixels = (long)result.Width * result.Height * 4;
                long before = (long)Math.Ceiling(pre * result.FPS), after = (long)Math.Ceiling(maximumPost * result.FPS);
                result.PoolBytes = pixels * (2 * before + after + 1);
                result.ClipBytes = pixels * (before + after);
                if (result.PoolBytes <= result.BudgetMiB * 1048576L && result.ClipBytes <= 256L * 1048576) return result;
                result.Width = Math.Max(16, (int)(result.Width * 0.9));
                result.Height = Math.Max(16, (int)(result.Height * 0.9));
            }
        }
    }
}
