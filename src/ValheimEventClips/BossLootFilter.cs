using System;
using System.Collections.Generic;

namespace ValheimEventClips
{
    internal enum LootDecision { Accept, Wait, Reject }
    internal static class BossLootFilter
    {
        private static readonly Dictionary<string, int> ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal static void SetRarities(Type enumType)
        {
            ranks.Clear();
            foreach (var value in Enum.GetValues(enumType)) ranks[Enum.GetName(enumType, value)] = Convert.ToInt32(value);
        }
        internal static bool TryRank(string name, out int rank)
        {
            rank = -1;
            if (string.Equals(name?.Trim(), "None", StringComparison.OrdinalIgnoreCase)) return true;
            return ranks.TryGetValue(name?.Trim() ?? "", out rank);
        }
        internal static LootDecision Decide(BossLoot loot, int minimum, bool deadlineReached)
        {
            if (minimum < 0) return LootDecision.Accept;
            if (loot != null)
                foreach (var item in loot.Items) if (item.Rank >= minimum) return LootDecision.Accept;
            if (!deadlineReached && (loot == null || loot.Pending)) return LootDecision.Wait;
            return LootDecision.Reject;
        }
        internal static LootDecision Evaluate(BossLoot loot, bool enabled, bool firstKill, bool firstBypasses, string minimumName, bool deadlineReached, out string reason)
        {
            if (!enabled) { reason = "rarity filter disabled"; return LootDecision.Accept; }
            if (firstKill && firstBypasses) { reason = "first recorded boss kill bypasses rarity"; return LootDecision.Accept; }
            int minimum;
            if (!TryRank(minimumName, out minimum)) { reason = "rarity name unsupported or Epic Loot unavailable"; return LootDecision.Reject; }
            var decision = Decide(loot, minimum, deadlineReached);
            reason = decision == LootDecision.Accept ? "observed loot meets threshold (or threshold is None)" : decision == LootDecision.Wait ? "awaiting boss loot" : "no observed item met rarity threshold";
            return decision;
        }
    }
}
