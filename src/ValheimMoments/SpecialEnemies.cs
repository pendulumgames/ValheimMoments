using System;
using System.Collections.Generic;

namespace ValheimMoments
{
    internal sealed class SpecialEnemies
    {
        private string configuration;
        private readonly HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> next = new Dictionary<string, double>(StringComparer.Ordinal);
        internal void Configure(string value)
        {
            if (value == configuration) return;
            configuration = value; keys.Clear(); next.Clear();
            if (value == null || value.Length > 4096) return;
            foreach (string part in value.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string key = part.Trim();
                if (key.Length == 0 || key.Length > 128 || keys.Count >= 64) continue;
                keys.Add(key);
            }
        }
        internal bool Eligible(BossKill kill, bool firstOnly, double now)
        {
            if (kill == null || kill.BossNumber > 0 || !keys.Contains(kill.EnemyKey) || (firstOnly && !kill.FirstKill)) return false;
            double until;
            return !next.TryGetValue(kill.EnemyKey, out until) || now >= until;
        }
        internal void Captured(string key, double now, double cooldown)
        { if (keys.Contains(key)) next[key] = now + Math.Max(0, Math.Min(3600, double.IsNaN(cooldown) ? 60 : cooldown)); }
        internal void Clear() { next.Clear(); }
    }
}
