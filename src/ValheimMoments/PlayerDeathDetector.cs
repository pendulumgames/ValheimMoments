using System;
using System.Reflection;
using HarmonyLib;

namespace ValheimMoments
{
    // Exact signatures and dead-state behavior verified in the installed game DLL.
    internal static class PlayerDeathDetector
    {
        internal static Action<Player, string> OnLocalDeath;
        internal static Action OnError;
        private sealed class DeathState { internal bool WasAlive; internal string Cause; }

        internal static void Install(Harmony harmony)
        {
            var original = typeof(Player).GetMethod("OnDeath", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (original == null || original.ReturnType != typeof(void)) throw new MissingMethodException("Expected Player.OnDeath() was not found.");
            harmony.Patch(original,
                prefix: new HarmonyMethod(typeof(PlayerDeathDetector), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(PlayerDeathDetector), nameof(Postfix)));
        }

        private static void Prefix(Player __instance, out DeathState __state)
        {
            __state = null;
            try
            {
                if (__instance != null && __instance == Player.m_localPlayer && !__instance.IsDead())
                    __state = new DeathState { WasAlive = true, Cause = DeathCause.Read(__instance) };
            }
            catch { ReportError(); }
        }

        private static void Postfix(Player __instance, DeathState __state)
        {
            try
            {
                // Non-owner returns, duplicate calls and skipped originals must not
                // create another clip. This never suppresses or alters game behavior.
                if (__state != null && __state.WasAlive && __instance != null && __instance == Player.m_localPlayer && __instance.IsDead())
                    OnLocalDeath?.Invoke(__instance, __state.Cause);
            }
            catch { ReportError(); }
        }

        private static void ReportError() { try { OnError?.Invoke(); } catch { } }
    }
}
