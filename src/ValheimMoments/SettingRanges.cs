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
            if (section == "Close Calls" && key == "ThresholdPercent") return new FiniteRange(1, 15, (double)fallback);
            if (section == "Close Calls" && key == "RecoveryPercent") return new FiniteRange(16, 100, (double)fallback);
            if (section == "Close Calls" && key == "RecoverySeconds") return new FiniteRange(1, 120, (double)fallback);
            if (section == "Director" && key == "MaxPerspectives") return new AcceptableValueRange<int>(1, 3);
            if (section == "Director" && key == "MaxPostMiB") return new AcceptableValueRange<int>(1, 30);
            if (section == "Director" && key == "CollectionSeconds") return new FiniteRange(1, 30, (double)fallback);
            if (section == "Player Death" && key == "CaptureLimit") return new AcceptableValueRange<int>(1, 20);
            if (section == "Player Death" && key == "WindowSeconds") return new FiniteRange(1, 3600, (double)fallback);
            if (section == "Notifications" && key == "Volume") return new FiniteRange(0, 1, (double)fallback);
            if (key == "CooldownSeconds") return new FiniteRange(0, 3600, (double)fallback);
            if (section == "Capture")
            {
                if (key == "Width") return new AcceptableValueRange<int>(480, 1920);
                if (key == "Height") return new AcceptableValueRange<int>(270, 1080);
                if (key == "FPS") return new AcceptableValueRange<int>(1, 30);
                if (key == "WebPQuality") return new AcceptableValueRange<int>(1, 100);
                if (key == "MemoryBudgetMiB") return new AcceptableValueRange<int>(48, 512);
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
