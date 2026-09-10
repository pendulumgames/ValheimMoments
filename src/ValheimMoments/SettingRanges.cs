using System;
using BepInEx.Configuration;

namespace ValheimMoments
{
    internal static class SettingRanges
    {
        private sealed class FiniteRange : AcceptableValueRange<double>
        {
            private readonly double fallback;
            internal FiniteRange(double min, double max, double fallback) : base(min, max) { this.fallback = fallback; }
            public override object Clamp(object value)
            { double n = (double)value; return base.Clamp(double.IsNaN(n) || double.IsInfinity(n) ? fallback : n); }
            public override bool IsValid(object value)
            { double n = (double)value; return !double.IsNaN(n) && !double.IsInfinity(n) && base.IsValid(n); }
        }
        internal static AcceptableValueBase For(string section, string key, object fallback)
        {
            if (section == "Capture")
            {
                if (key == "Width") return new AcceptableValueRange<int>(16, 1920);
                if (key == "Height") return new AcceptableValueRange<int>(16, 1080);
                if (key == "FPS") return new AcceptableValueRange<int>(1, 30);
                if (key == "WebPQuality") return new AcceptableValueRange<int>(1, 100);
                if (key == "MemoryBudgetMiB") return new AcceptableValueRange<int>(16, 512);
                if (key == "PreEventSeconds") return new FiniteRange(1, 30, (double)fallback);
            }
            if (key == "PostEventSeconds") return new FiniteRange(0, 30, (double)fallback);
            if (key == "LootWaitSeconds") return new FiniteRange(0, 25, (double)fallback);
            if (key == "MaxLootItemsShown") return new AcceptableValueRange<int>(1, 20);
            if (section == "Discord" && key == "MaxUploadMiB") return new AcceptableValueRange<int>(1, 100);
            return null;
        }
    }
}
