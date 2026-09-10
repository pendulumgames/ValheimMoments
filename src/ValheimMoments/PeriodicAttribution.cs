using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace ValheimMoments
{
    // Read-only provenance: never write HitData, status effects or game credit.
    internal static class PeriodicAttribution
    {
        internal static Func<bool> Enabled;
        private sealed class Source { internal ZDOID Id; internal string Name; }
        private sealed class Pools { internal Source Fire, Spirit, Poison; }
        private sealed class DamageScope { internal Character Victim; internal Source Source; }
        private sealed class Addition { internal float Before; internal Source Source; internal FieldInfo Field; }
        private static ConditionalWeakTable<StatusEffect, Pools> pools = new ConditionalWeakTable<StatusEffect, Pools>();
        private static ConditionalWeakTable<HitData, Source> ticks = new ConditionalWeakTable<HitData, Source>();
        [ThreadStatic] private static DamageScope damage;
        [ThreadStatic] private static StatusEffect tick;
        private static readonly FieldInfo Victim = AccessTools.Field(typeof(StatusEffect), "m_character");
        private static readonly FieldInfo Fire = AccessTools.Field(typeof(SE_Burning), "m_fireDamageLeft");
        private static readonly FieldInfo Spirit = AccessTools.Field(typeof(SE_Burning), "m_spiritDamageLeft");
        private static readonly FieldInfo Poison = AccessTools.Field(typeof(SE_Poison), "m_damageLeft");

        internal static void Install(Harmony harmony)
        {
            if (Victim == null || Fire == null || Spirit == null || Poison == null)
                throw new MissingFieldException("Periodic damage layout changed");
            Patch(harmony, typeof(Character), "RPC_Damage", new[] { typeof(long), typeof(HitData) }, nameof(BeforeDamage), null, nameof(AfterDamage));
            Patch(harmony, typeof(SE_Burning), "AddFireDamage", new[] { typeof(float) }, nameof(BeforeAddition), nameof(AfterAddition), null);
            Patch(harmony, typeof(SE_Burning), "AddSpiritDamage", new[] { typeof(float) }, nameof(BeforeAddition), nameof(AfterAddition), null);
            Patch(harmony, typeof(SE_Poison), "AddDamage", new[] { typeof(float) }, nameof(BeforeAddition), nameof(AfterAddition), null);
            foreach (var type in new[] { typeof(SE_Burning), typeof(SE_Poison) })
                Patch(harmony, type, "UpdateStatusEffect", new[] { typeof(float) }, nameof(BeforeTick), null, nameof(AfterTick));
            Patch(harmony, typeof(Character), "ApplyDamage", new[] { typeof(HitData), typeof(bool), typeof(bool), typeof(HitData.DamageModifier) }, nameof(BeforeApply), null, null);
        }
        private static void Patch(Harmony harmony, Type type, string name, Type[] args, string prefix, string postfix, string finalizer)
        {
            var method = AccessTools.DeclaredMethod(type, name, args);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(PeriodicAttribution), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(PeriodicAttribution), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(PeriodicAttribution), finalizer));
        }
        private static void BeforeDamage(Character __instance, HitData __1, out DamageScope __state)
        {
            __state = damage; damage = null;
            try
            {
                if (Enabled?.Invoke() == false || !__instance.IsBoss() || !__instance.IsOwner()) return;
                Source source = null;
                if (__1 != null && !__1.m_attacker.IsNone())
                {
                    string reason;
                    string name = BossAttribution.Resolve(__1, out reason);
                    if (!string.IsNullOrWhiteSpace(name)) source = new Source { Id = __1.m_attacker, Name = name };
                }
                damage = new DamageScope { Victim = __instance, Source = source };
            }
            catch { }
        }
        private static void AfterDamage(DamageScope __state) { damage = __state; }
        private static void BeforeAddition(StatusEffect __instance, MethodBase __originalMethod, out Addition __state)
        {
            __state = null;
            try
            {
                var victim = Victim.GetValue(__instance) as Character;
                if (Enabled?.Invoke() == false || victim == null || !victim.IsBoss() || !victim.IsOwner()) return;
                var field = __originalMethod.Name == "AddFireDamage" ? Fire : __originalMethod.Name == "AddSpiritDamage" ? Spirit : Poison;
                __state = new Addition { Field = field, Before = (float)field.GetValue(__instance),
                    Source = damage != null && ReferenceEquals(victim, damage.Victim) ? damage.Source : null };
            }
            catch { }
        }
        private static Source Merge(Source existing, Source added)
        {
            return existing != null && added != null && existing.Id == added.Id ? added : null;
        }
        private static void AfterAddition(StatusEffect __instance, float __0, Addition __state)
        {
            try
            {
                if (__state == null || !(__0 > 0) || float.IsInfinity(__0)) return;
                float after = (float)__state.Field.GetValue(__instance);
                var state = pools.GetOrCreateValue(__instance);
                if (__state.Field == Poison)
                {
                    // Vanilla ignores weaker poison; equal/stronger poison replaces the pool.
                    if (__0 >= __state.Before && after == __0) state.Poison = __state.Source;
                }
                else if (after > __state.Before)
                {
                    Source previous = __state.Field == Fire ? state.Fire : state.Spirit;
                    Source next = __state.Before > 0 ? Merge(previous, __state.Source) : __state.Source;
                    if (__state.Field == Fire) state.Fire = next; else state.Spirit = next;
                }
            }
            catch { }
        }
        private static void BeforeTick(StatusEffect __instance, out StatusEffect __state)
        {
            __state = tick; tick = null;
            try
            {
                var victim = Victim.GetValue(__instance) as Character;
                if (Enabled?.Invoke() == false || victim == null || !victim.IsBoss() || !victim.IsOwner()) { pools.Remove(__instance); return; }
                tick = __instance;
            }
            catch { }
        }
        private static void AfterTick(StatusEffect __state) { tick = __state; }
        private static void BeforeApply(Character __instance, HitData __0)
        {
            try
            {
                if (__0 == null || tick == null || !__0.m_attacker.IsNone() ||
                    !ReferenceEquals(Victim.GetValue(tick), __instance) || !__instance.IsBoss() || !__instance.IsOwner()) return;
                Pools state;
                if (!pools.TryGetValue(tick, out state)) return;
                Source source = null;
                if (tick is SE_Poison && __0.m_hitType == HitData.HitType.Poisoned && __0.m_damage.m_poison > 0)
                    source = state.Poison;
                else if (tick is SE_Burning && __0.m_hitType == HitData.HitType.Burning)
                {
                    bool fire = __0.m_damage.m_fire > 0, spirit = __0.m_damage.m_spirit > 0;
                    source = fire && spirit ? Merge(state.Fire, state.Spirit) : fire ? state.Fire : spirit ? state.Spirit : null;
                }
                ticks.Remove(__0);
                if (source != null) ticks.Add(__0, source);
            }
            catch { }
        }
        internal static string Resolve(HitData hit)
        {
            Source source;
            return Enabled?.Invoke() != false && hit != null && ticks.TryGetValue(hit, out source) ? source.Name : null;
        }
        internal static void Clear()
        {
            damage = null; tick = null;
            Enabled = null;
            pools = new ConditionalWeakTable<StatusEffect, Pools>();
            ticks = new ConditionalWeakTable<HitData, Source>();
        }
    }
}
