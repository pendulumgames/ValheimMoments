using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimEventClips
{
    internal static class BossLootDetector
    {
        private const string CreditRpc = "ValheimMoments_BossLootCredit_v2", ResultRpc = "ValheimMoments_BossLootResult_v2";
        private static readonly FieldInfo DropCharacter = typeof(CharacterDrop).GetField("m_character", BindingFlags.Instance | BindingFlags.NonPublic);
        private sealed class Context
        {
            internal Character Character;
            internal BossLoot Loot = new BossLoot();
            internal HashSet<long> Recipients = new HashSet<long>();
            internal HashSet<ItemDrop.ItemData> EpicItems = new HashSet<ItemDrop.ItemData>();
            internal int PendingRagdolls;
        }
        [ThreadStatic] private static Context current;
        private static readonly LootInbox inbox = new LootInbox();
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static ZRoutedRpc registered;
        private static bool enabled;
        private static ConditionalWeakTable<object, Context> ragdolls = new ConditionalWeakTable<object, Context>();
        internal static Action<int> OnObserved;
        internal static Action OnError;

        internal static void EnableEpic(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Ragdoll), "Setup"), prefix: new HarmonyMethod(typeof(BossLootDetector), nameof(BeforeRagdollSetup)));
            harmony.Patch(AccessTools.Method(typeof(Ragdoll), "SpawnLoot"), prefix: new HarmonyMethod(typeof(BossLootDetector), nameof(BeforeRagdollLoot)), finalizer: new HarmonyMethod(typeof(BossLootDetector), nameof(AfterRagdollLoot)));
        }
        private static void BeforeRagdollSetup(object __instance, CharacterDrop characterDrop)
        {
            try
            {
                if (current == null || !ReferenceEquals(DropCharacter.GetValue(characterDrop), current.Character)) return;
                ragdolls.Remove(__instance); ragdolls.Add(__instance, current);
                current.PendingRagdolls++;
                current.Loot.Pending = true;
            }
            catch { Error(); }
        }
        private static void BeforeRagdollLoot(object __instance, out Context __state)
        {
            __state = current;
            Context found; current = enabled && ragdolls.TryGetValue(__instance, out found) ? found : null;
        }
        private static void AfterRagdollLoot(object __instance, Context __state, Exception __exception)
        {
            var finished = current; current = __state;
            try
            {
                if (finished == null) return;
                ragdolls.Remove(__instance); finished.PendingRagdolls = Math.Max(0, finished.PendingRagdolls - 1); finished.Loot.Pending = finished.PendingRagdolls > 0;
                if (__exception != null) finished.Loot.Incomplete = true;
                Publish(finished);
                if (!finished.Loot.Pending) finished.EpicItems.Clear();
            }
            catch { Error(); }
        }
        internal static void RecordEpic(List<GameObject> objects)
        {
            if (!enabled || current == null || objects == null) return;
            foreach (var obj in objects)
            {
                try
                {
                    var item = obj == null ? null : obj.GetComponent<ItemDrop>()?.m_itemData;
                    if (current.EpicItems.Count >= 256) { current.Loot.Incomplete = true; break; }
                    if (item == null || !current.EpicItems.Add(item)) continue;
                    current.Loot.AddEpic(EpicLootAdapter.Read(item, obj.name));
                    OnObserved?.Invoke(current.Loot.Items.Count);
                }
                catch { current.Loot.Incomplete = true; Error(); }
            }
        }

        internal static void Install(Harmony harmony)
        {
            if (DropCharacter == null) throw new MissingFieldException("CharacterDrop.m_character");
            enabled = true;
            harmony.Patch(AccessTools.Method(typeof(Character), "OnDeath", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(BossLootDetector), nameof(BeforeDeath)),
                finalizer: new HarmonyMethod(typeof(BossLootDetector), nameof(AfterDeath)));
            harmony.Patch(AccessTools.Method(typeof(CharacterDrop), "GenerateDropList", Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(BossLootDetector), nameof(AfterRoll)) { priority = Priority.Last });
            harmony.Patch(AccessTools.Method(typeof(Game), "RegisterKill", new[] { typeof(long), typeof(string), typeof(int), typeof(KillModifiers), typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(BossLootDetector), nameof(BeforeCredit)));
            foreach (var constructor in AccessTools.GetDeclaredConstructors(typeof(ZRoutedRpc)))
                harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(BossLootDetector), nameof(Register)));
            Register(ZRoutedRpc.instance);
        }
        private static void Register(ZRoutedRpc __instance)
        {
            try
            {
                if (__instance == null || ReferenceEquals(registered, __instance)) return;
                __instance.Register<string, string>(CreditRpc, (sender, enemy, token) => { if (enabled) inbox.Announce(sender, enemy, token, clock.Elapsed.TotalSeconds); });
                __instance.Register<string, string>(ResultRpc, (sender, token, payload) => { if (enabled) inbox.Complete(sender, token, payload, clock.Elapsed.TotalSeconds); });
                registered = __instance; inbox.Clear();
            }
            catch { Error(); }
        }
        private static void BeforeDeath(Character __instance, out Context __state)
        {
            __state = current; current = null;
            try { if (enabled && __instance.IsOwner() && (__instance.IsBoss() || BossKillDetector.ObserveOrdinary?.Invoke() == true)) current = new Context { Character = __instance }; }
            catch { Error(); }
        }
        private static void AfterDeath(Context __state, Exception __exception)
        {
            var finished = current; current = __state;
            try
            {
                if (finished == null) return;
                if (__exception != null) finished.Loot.Incomplete = true;
                Publish(finished);
            }
            catch { Error(); }
        }
        private static void Publish(Context context)
        {
            context.Loot.Revision++;
            string payload = context.Loot.Encode();
            foreach (long peer in context.Recipients)
            {
                try { ZRoutedRpc.instance.InvokeRoutedRPC(peer, ResultRpc, new object[] { context.Loot.Id, payload }); }
                catch { Error(); }
            }
        }
        private static void BeforeCredit(long playerPeerID, string enemyName, int bossNumber)
        {
            try
            {
                if (current == null || current.Character.m_name != enemyName) return;
                if (!current.Recipients.Add(playerPeerID)) return;
                ZRoutedRpc.instance.InvokeRoutedRPC(playerPeerID, CreditRpc, new object[] { enemyName, current.Loot.Id });
            }
            catch { Error(); }
        }
        internal static BossLoot Take(long sender, string enemy)
        {
            if (sender == 0 && current != null && current.Character.m_name == enemy) return current.Loot;
            return inbox.Take(sender, enemy, clock.Elapsed.TotalSeconds);
        }
        private static void AfterRoll(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
        {
            try
            {
                if (current == null || !ReferenceEquals(DropCharacter.GetValue(__instance), current.Character)) return;
                if (__result == null) { current.Loot.Incomplete = true; return; }
                current.Loot.Observed = true;
                foreach (var drop in __result)
                {
                    if (drop.Key == null || drop.Value <= 0) { current.Loot.Incomplete = true; continue; }
                    var item = drop.Key.GetComponent<ItemDrop>();
                    if (item == null || item.m_itemData?.m_shared == null) { current.Loot.Incomplete = true; continue; }
                    current.Loot.Add(drop.Key.name, item.m_itemData.m_shared.m_name, (long)drop.Value * item.m_itemData.m_stack);
                }
                OnObserved?.Invoke(current.Loot.Items.Count);
            }
            catch { if (current != null) current.Loot.Incomplete = true; Error(); }
        }
        private static void Error() { try { OnError?.Invoke(); } catch { } }
        internal static void Clear() { enabled = false; current = null; ragdolls = new ConditionalWeakTable<object, Context>(); inbox.Clear(); OnObserved = null; OnError = null; }
    }
}
