using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMoments
{
    internal static class DiscoveryDetector
    {
        internal static Action<string, string, string> OnObserved;
        internal static Action OnError;
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        [ThreadStatic] private static Player updating;
        private sealed class TraderState { internal Player Player; internal bool Near; internal double Next; }
        private static ConditionalWeakTable<Trader, TraderState> traders = new ConditionalWeakTable<Trader, TraderState>();
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Player), "UpdateBiome"), prefix: new HarmonyMethod(typeof(DiscoveryDetector), nameof(Begin)), finalizer: new HarmonyMethod(typeof(DiscoveryDetector), nameof(End)));
            harmony.Patch(AccessTools.Method(typeof(Player), "AddKnownBiome"), postfix: new HarmonyMethod(typeof(DiscoveryDetector), nameof(Biome)));
            harmony.Patch(AccessTools.Method(typeof(Player), "AddKnownLocationName"), postfix: new HarmonyMethod(typeof(DiscoveryDetector), nameof(Location)));
            harmony.Patch(AccessTools.Method(typeof(Trader), "Update"), postfix: new HarmonyMethod(typeof(DiscoveryDetector), nameof(TraderUpdated)));
        }
        private static void Begin(Player __instance, out Player __state) { __state = updating; updating = __instance; }
        private static Exception End(Player __state, Exception __exception) { updating = __state; return __exception; }
        private static void Biome(Player __instance, BiomeSector __0)
        {
            try
            {
                if (__instance != Player.m_localPlayer || !ReferenceEquals(updating, __instance) || __0 == null || (int)__0.Biome == 0) return;
                var variants = new List<string>();
                foreach (var alt in __0.AltBiomes) if (alt != null && !string.IsNullOrEmpty(alt.m_name)) variants.Add(alt.m_name.Length.ToString(CultureInfo.InvariantCulture) + ":" + alt.m_name);
                variants.Sort(StringComparer.Ordinal);
                string identity = ((int)__0.Biome).ToString(CultureInfo.InvariantCulture) + "/" + string.Join("/", variants);
                OnObserved?.Invoke("biome", identity, __0.GetName(false));
            }
            catch { OnError?.Invoke(); }
        }
        private static void Location(Player __instance, string __0)
        {
            if (__instance != Player.m_localPlayer || !ReferenceEquals(updating, __instance) || string.IsNullOrWhiteSpace(__0)) return;
            try { OnObserved?.Invoke("location", __0, __0); } catch { OnError?.Invoke(); }
        }
        private static void TraderUpdated(Trader __instance)
        {
            try
            {
                var player = Player.m_localPlayer;
                if (player == null || __instance == null) return;
                var state = traders.GetValue(__instance, _ => new TraderState());
                double now = clock.Elapsed.TotalSeconds;
                if (ReferenceEquals(state.Player, player) && now < state.Next) return;
                if (!ReferenceEquals(state.Player, player)) { state.Player = player; state.Near = false; }
                state.Next = now + 0.5;
                float range = Mathf.Clamp(__instance.m_greetRange, 1, 30);
                bool near = Vector3.Distance(player.transform.position, __instance.transform.position) <= range;
                if (near && !state.Near) OnObserved?.Invoke("trader", __instance.m_name, __instance.m_name);
                state.Near = near;
            }
            catch { OnError?.Invoke(); }
        }
        internal static void Clear() { updating = null; traders = new ConditionalWeakTable<Trader, TraderState>(); }
    }
}
