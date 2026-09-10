using System;
using System.Collections.Generic;

namespace ValheimMoments
{
    internal sealed class AcquisitionHighlights
    {
        private sealed class Batch { internal string Kind; internal double Due; internal BossKill Event; }
        private readonly List<Batch> batches = new List<Batch>();
        internal void Add(string kind, LootItem item, string player, double now)
        {
            if ((kind != "c" && kind != "w") || item == null || item.Quantity <= 0) return;
            var batch = batches.Find(b => b.Kind == kind);
            if (batch == null)
            {
                batch = new Batch { Kind = kind, Due = now + 0.25, Event = new BossKill {
                    Acquired = true, EnemyKey = kind == "c" ? "a treasure chest" : "the world",
                    PlayerName = player, Loot = new BossLoot() } };
                batches.Add(batch);
            }
            batch.Event.Loot.AddEpic(item);
        }
        internal void Poll(double now, string minimum, Func<string, bool> enabled, Action<BossKill> accept)
        {
            for (int i = 0; i < batches.Count;)
            {
                var batch = batches[i];
                if (now < batch.Due) { i++; continue; }
                batches.RemoveAt(i);
                int rank;
                if (enabled(batch.Kind) && BossLootFilter.TryRank(minimum, out rank) && batch.Event.Loot.Items.Exists(item => item.Rank >= rank))
                    accept(batch.Event);
            }
        }
        internal void Clear() { batches.Clear(); }
    }
}
