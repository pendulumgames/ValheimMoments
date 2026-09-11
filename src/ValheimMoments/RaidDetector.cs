using System;
using HarmonyLib;

namespace ValheimMoments
{
    internal enum RaidTransition { Entered, Left, Ended }

    // SetActiveEvent's end flag also occurs on replacement/reset. It is NOT victory.
    // Reference identity is local to this session, never a shared multiplayer ID.
    internal static class RaidDetector
    {
        internal static Action<RandomEvent, RaidTransition> OnTransition { get; set; }
        internal static Action OnError { get; set; }
        private sealed class State { internal RandomEvent Before; internal bool WasRandom; }
        internal static void Install(Harmony harmony)
        {
            var method = AccessTools.DeclaredMethod(typeof(RandEventSystem), "SetActiveEvent", new[] { typeof(RandomEvent), typeof(bool) });
            if (method == null) throw new MissingMethodException("RandEventSystem.SetActiveEvent");
            harmony.Patch(method, new HarmonyMethod(typeof(RaidDetector), nameof(Before)), new HarmonyMethod(typeof(RaidDetector), nameof(After)));
        }
        private static void Before(RandEventSystem __instance, out State __state)
        {
            __state = null;
            try
            {
                if (OnTransition == null || Player.m_localPlayer == null) return;
                var active = __instance.GetActiveEvent();
                __state = new State { Before = active, WasRandom = active != null && ReferenceEquals(active, __instance.GetCurrentRandomEvent()) };
            }
            catch { Error(); }
        }
        private static void After(RandEventSystem __instance, bool __1, State __state)
        {
            try
            {
                if (__state == null || Player.m_localPlayer == null) return;
                var active = __instance.GetActiveEvent();
                if (ReferenceEquals(active, __state.Before)) return;
                if (__state.WasRandom) Emit(__state.Before, __1 ? RaidTransition.Ended : RaidTransition.Left);
                if (active != null && ReferenceEquals(active, __instance.GetCurrentRandomEvent()) && !Player.m_localPlayer.IsDead())
                    Emit(active, RaidTransition.Entered);
            }
            catch { Error(); }
        }
        private static void Emit(RandomEvent raid, RaidTransition transition)
        { try { OnTransition?.Invoke(raid, transition); } catch { Error(); } }
        private static void Error() { try { OnError?.Invoke(); } catch { } }
    }
}
