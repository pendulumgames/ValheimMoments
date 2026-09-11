using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMoments
{
    internal static class GalleryInput
    {
        internal static Func<bool> Open;
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Player), "TakeInput"), postfix: new HarmonyMethod(typeof(GalleryInput), nameof(Input)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(GameCamera), "UpdateMouseCapture"), prefix: new HarmonyMethod(typeof(GalleryInput), nameof(Cursor)));
        }
        private static void Input(ref bool __result) { if (Open?.Invoke() == true) __result = false; }
        private static bool Cursor()
        {
            if (Open?.Invoke() != true) return true;
            ZCursor.LockState = CursorLockMode.None; ZCursor.Show(); return false;
        }
    }
}
