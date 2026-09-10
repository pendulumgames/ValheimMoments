using System;
using System.Collections.Generic;

namespace ValheimMoments
{
    // Holds metadata only. Unqualified kills never reserve frames or launch encoders.
    internal sealed class LootHighlights
    {
        private sealed class Candidate { internal BossKill Kill; internal double Deadline; }
        private readonly List<Candidate> pending = new List<Candidate>();
        internal int Count => pending.Count;
        internal void Add(BossKill kill, double now, double wait)
        {
            if (kill == null || kill.BossNumber > 0) return;
            foreach (var entry in pending) if (ReferenceEquals(entry.Kill, kill) ||
                (kill.Loot != null && ReferenceEquals(entry.Kill.Loot, kill.Loot))) return;
            if (pending.Count == 64) pending.RemoveAt(0);
            pending.Add(new Candidate { Kill = kill, Deadline = now + (double.IsNaN(wait) ? 12 : Math.Max(0, Math.Min(25, wait))) });
        }
        internal void Poll(double now, string minimumName, Action<BossKill> accept)
        {
            int minimum;
            if (!BossLootFilter.TryRank(minimumName, out minimum)) { Clear(); return; }
            for (int i = 0; i < pending.Count;)
            {
                var entry = pending[i];
                // None still requires a real item drop; empty kills are not loot highlights.
                bool qualifies = false;
                if (entry.Kill.Loot != null)
                    foreach (var item in entry.Kill.Loot.Items)
                        if (item.Quantity > 0 && item.Rank >= minimum) { qualifies = true; break; }
                if (qualifies && now <= entry.Deadline)
                {
                    pending.RemoveAt(i); // Consume before callback, including busy/rejected captures.
                    accept(entry.Kill);
                }
                else if (now >= entry.Deadline || (entry.Kill.Loot != null && entry.Kill.Loot.Observed && !entry.Kill.Loot.Pending)) pending.RemoveAt(i);
                else i++;
            }
        }
        internal void Clear() { pending.Clear(); }
    }
}
