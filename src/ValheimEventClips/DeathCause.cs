using System;
using System.Reflection;

namespace ValheimEventClips
{
    internal static class DeathCause
    {
        private static readonly FieldInfo LastHit = typeof(Character).GetField("m_lastHit", BindingFlags.Instance | BindingFlags.NonPublic);

        // Snapshot before OnDeath: later destruction must not erase attacker identity.
        internal static string Read(Player player)
        {
            try
            {
                var hit = LastHit?.GetValue(player) as HitData;
                if (hit == null) return "unknown cause";
                if (hit.m_hitType == HitData.HitType.EnemyHit || hit.m_hitType == HitData.HitType.PlayerHit || hit.m_hitType == HitData.HitType.Undefined)
                {
                    var attacker = hit.GetAttacker();
                    if (attacker != null)
                    {
                        string name = attacker.GetHoverName(); // Game localizes creature names here.
                        if (!string.IsNullOrWhiteSpace(name)) return name;
                    }
                }
                return Label(hit.m_hitType);
            }
            catch { return "unknown cause"; } // Cause extraction must never lose the death clip.
        }

        internal static string Label(HitData.HitType type)
        {
            switch (type)
            {
                case HitData.HitType.EnemyHit: return "an enemy (attacker unavailable)";
                case HitData.HitType.PlayerHit: return "another player (attacker unavailable)";
                case HitData.HitType.Fall: return "fall damage";
                case HitData.HitType.Drowning: return "drowning";
                case HitData.HitType.Burning: return "burning";
                case HitData.HitType.Freezing: return "freezing";
                case HitData.HitType.Poisoned: return "poison";
                case HitData.HitType.Water: return "water damage";
                case HitData.HitType.Smoke: return "smoke inhalation";
                case HitData.HitType.EdgeOfWorld: return "the edge of the world";
                case HitData.HitType.Impact: return "an impact";
                case HitData.HitType.Cart: return "a cart";
                case HitData.HitType.Tree: return "a falling tree";
                case HitData.HitType.Self: return "self-inflicted damage";
                case HitData.HitType.Structural: return "structural damage";
                case HitData.HitType.Turret: return "a turret";
                case HitData.HitType.Boat: return "a boat";
                case HitData.HitType.Stalagtite: return "a falling stalactite";
                case HitData.HitType.Catapult: return "a catapult";
                case HitData.HitType.CinderFire: return "cinder fire";
                case HitData.HitType.AshlandsOcean: return "the Ashlands ocean";
                case HitData.HitType.AshlandsLava: return "Ashlands lava";
                case HitData.HitType.Incinerator: return "an incinerator";
                case HitData.HitType.DrawBridge: return "a drawbridge";
                default: return "unknown cause";
            }
        }
    }
}
