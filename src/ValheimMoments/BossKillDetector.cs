using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ValheimMoments
{
    internal sealed class BossKill
    {
        internal string EnemyKey, PlayerName, FinalBlowName, CreditNames, EventId;
        internal int BossNumber;
        internal bool FirstKill;
        internal bool Acquired;
        internal bool Special;
        internal BossLoot Loot;
    }

    internal static class BossKillDetector
    {
        internal static Action<BossKill> OnKill;
        internal static Action<BossKill> OnLootKill;
        internal static Func<bool> ObserveOrdinary;
        internal static Action OnError;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo Stats = typeof(PlayerProfile).GetField("m_playerStats", Fields);
        private sealed class State
        {
            internal PlayerProfile Profile;
            internal string EnemyKey;
            internal int BossNumber;
            internal float Count;
            internal string FinalBlowName;
            internal string CreditNames;
            internal string EventId;
            internal BossLoot Loot;
        }

        internal static bool TryCount(PlayerProfile profile, string key, out float count)
        {
            count = 0;
            try
            {
                // Verified all-difficulties/all-modifiers bucket, also serialized
                // by PlayerProfile.SavePlayerToDisk and restored on load.
                var stats = Stats?.GetValue(profile) as Array;
                if (stats == null || stats.Length == 0) return false;
                object all = stats.GetValue(0);
                var field = all?.GetType().GetField("m_enemyStats", Fields);
                var enemies = field?.GetValue(all) as Dictionary<string, float>[];
                if (enemies == null || enemies.Length == 0 || enemies[0] == null || string.IsNullOrEmpty(key)) return false;
                enemies[0].TryGetValue(key, out count);
                return !float.IsNaN(count) && !float.IsInfinity(count) && count >= 0;
            }
            catch { return false; }
        }

        internal static void Install(Harmony harmony)
        {
            var method = typeof(Game).GetMethod("RPC_RegisterKill", Fields, null,
                new[] { typeof(long), typeof(string), typeof(int), typeof(int), typeof(int), typeof(bool) }, null);
            if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException("Expected Game.RPC_RegisterKill signature not found.");
            harmony.Patch(method, new HarmonyMethod(typeof(BossKillDetector), nameof(Prefix)), new HarmonyMethod(typeof(BossKillDetector), nameof(Postfix)));
        }

        private static void Prefix(Game __instance, long sender, string enemyName, int bossNumber, out State __state)
        {
            __state = null;
            try
            {
                // Consume metadata even when this category is disabled, so it cannot
                // be attached to a later credit after a configuration change.
                string eventId = BossAttribution.TakeEvent(sender, enemyName);
                if (bossNumber <= 0 && ObserveOrdinary?.Invoke() != true) return;
                string finalBlow = bossNumber > 0 ? BossAttribution.Take(sender, enemyName) : null;
                string credits = bossNumber > 0 ? BossAttribution.TakeCredits(sender, enemyName) : null;
                BossLoot loot = BossLootDetector.Take(sender, enemyName);
                var profile = __instance.GetPlayerProfile();
                float count;
                if (!TryCount(profile, enemyName, out count)) { ReportError(); return; }
                __state = new State { Profile = profile, EnemyKey = enemyName, BossNumber = bossNumber, Count = count, FinalBlowName = finalBlow, CreditNames = credits, Loot = loot, EventId = eventId };
            }
            catch { ReportError(); }
        }

        private static void Postfix(State __state)
        {
            if (__state == null) return;
            try
            {
                float after;
                if (!TryCount(__state.Profile, __state.EnemyKey, out after)) { ReportError(); return; }
                if (after <= __state.Count) return; // Original skipped / no credit applied.
                var callback = __state.BossNumber > 0 ? OnKill : OnLootKill;
                callback?.Invoke(new BossKill { EnemyKey = __state.EnemyKey, BossNumber = __state.BossNumber,
                    PlayerName = __state.Profile.GetName(), FirstKill = __state.Count == 0, FinalBlowName = __state.FinalBlowName, CreditNames = __state.CreditNames, Loot = __state.Loot, EventId = __state.EventId });
            }
            catch { ReportError(); }
        }

        private static void ReportError() { try { OnError?.Invoke(); } catch { } }
    }
}
