using System;
using HarmonyLib;

namespace ValheimMoments
{
    internal static class LocalDamageDetector
    {
        internal static Action<Player, float, float, float, float, bool> OnDamage { get; set; }
        internal static Action OnError { get; set; }
        private sealed class State { internal Player Player; internal float Health, Maximum; }

        internal static void Install(Harmony harmony)
        {
            var method = AccessTools.DeclaredMethod(typeof(Character), "ApplyDamage",
                new[] { typeof(HitData), typeof(bool), typeof(bool), typeof(HitData.DamageModifier) });
            if (method == null) throw new MissingMethodException("Character.ApplyDamage");
            harmony.Patch(method, new HarmonyMethod(typeof(LocalDamageDetector), nameof(Before)),
                new HarmonyMethod(typeof(LocalDamageDetector), nameof(After)));
        }
        private static void Before(Character __instance, out State __state)
        {
            __state = null;
            try
            {
                var player = __instance as Player;
                if (OnDamage == null || player == null || player != Player.m_localPlayer ||
                    !player.IsOwner() || player.IsDead()) return;
                __state = new State { Player = player, Health = player.GetHealth(), Maximum = player.GetMaxHealth() };
            }
            catch { Error(); }
        }
        private static void After(State __state)
        {
            try
            {
                if (__state == null || __state.Player != Player.m_localPlayer || !__state.Player.IsOwner()) return;
                var player = __state.Player;
                float after = player.GetHealth();
                if (after < __state.Health)
                    OnDamage?.Invoke(player, __state.Health, after, __state.Maximum, player.GetMaxHealth(), !player.IsDead());
            }
            catch { Error(); }
        }
        private static void Error() { try { OnError?.Invoke(); } catch { } }
    }
}
