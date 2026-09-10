using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ValheimMoments;

// Behavioral models of the inspected status-effect pool rules, not game binaries.
public class StatusEffect
{
    public Character m_character;
    public HitData LastTick;
    public bool ThrowOnTick;
}
public class SE_Burning : StatusEffect
{
    public float m_fireDamageLeft, m_spiritDamageLeft;
    [MethodImpl(MethodImplOptions.NoInlining)] public bool AddFireDamage(float amount)
    { if (amount < 0.2f && m_fireDamageLeft == 0) return false; m_fireDamageLeft += amount; return true; }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool AddSpiritDamage(float amount)
    { if (amount < 0.2f && m_spiritDamageLeft == 0) return false; m_spiritDamageLeft += amount; return true; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateStatusEffect(float dt)
    {
        if (ThrowOnTick) throw new InvalidOperationException("tick failure");
        LastTick = new HitData { m_hitType = HitData.HitType.Burning,
            m_damage = new HitData.DamageTypes { m_fire = m_fireDamageLeft > 0 ? 1 : 0, m_spirit = m_spiritDamageLeft > 0 ? 1 : 0 } };
        m_fireDamageLeft = Math.Max(0, m_fireDamageLeft - 1); m_spiritDamageLeft = Math.Max(0, m_spiritDamageLeft - 1);
        m_character.ApplyDamage(LastTick, true, false, HitData.DamageModifier.Normal);
    }
}
public class SE_Poison : StatusEffect
{
    public float m_damageLeft;
    [MethodImpl(MethodImplOptions.NoInlining)] public void AddDamage(float amount) { if (amount >= m_damageLeft) m_damageLeft = amount; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateStatusEffect(float dt)
    {
        LastTick = new HitData { m_hitType = HitData.HitType.Poisoned, m_damage = new HitData.DamageTypes { m_poison = 1 } };
        m_damageLeft = Math.Max(0, m_damageLeft - 1);
        m_character.ApplyDamage(LastTick, true, false, HitData.DamageModifier.Normal);
    }
}
internal static class PeriodicTests
{
    private static int checks;
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    private static HitData Hit(long id, string name) { return new HitData { m_attacker = new ZDOID { Id = id }, Attacker = new Player { Name = name } }; }
    private static string Resolve(HitData hit) { string reason; return BossAttribution.Resolve(hit, out reason); }
    internal static void Run()
    {
        var harmony = new Harmony("valheimmoments.periodic.tests");
        PeriodicAttribution.Install(harmony);
        try
        {
            var boss = new Character { Boss = true };
            var spirit = new SE_Burning { m_character = boss };
            boss.DamageAction = h => spirit.AddSpiritDamage(5);
            var original = Hit(1, "Ragnar"); boss.RPC_Damage(0, original);
            spirit.UpdateStatusEffect(1);
            Check(Resolve(spirit.LastTick) == "Ragnar", "Single-source Spirit tick attributed");
            Check(spirit.LastTick.m_attacker.IsNone() && original.m_attacker.Id == 1 && spirit.m_spiritDamageLeft == 4, "No game damage or attacker fields modified");
            var savedTick = spirit.LastTick;
            boss.RPC_Damage(0, Hit(1, "Ragnar")); spirit.UpdateStatusEffect(1);
            Check(Resolve(spirit.LastTick) == "Ragnar", "Same source stacking remains known");
            boss.RPC_Damage(0, Hit(2, "Astrid")); spirit.UpdateStatusEffect(1);
            Check(Resolve(spirit.LastTick) == null, "Mixed Spirit sources remain unavailable");
            Check(Resolve(savedTick) == "Ragnar", "Earlier exact tick retains immutable source");
            spirit.m_spiritDamageLeft = 0; boss.RPC_Damage(0, Hit(2, "Astrid")); spirit.UpdateStatusEffect(1);
            Check(Resolve(spirit.LastTick) == "Astrid", "Drained effect starts a fresh source");
            spirit.AddSpiritDamage(3); spirit.UpdateStatusEffect(1);
            Check(Resolve(spirit.LastTick) == null, "Unscoped environmental addition taints existing pool");
            var poison = new SE_Poison { m_character = boss };
            boss.DamageAction = h => poison.AddDamage(10); boss.RPC_Damage(0, Hit(1, "Ragnar"));
            boss.DamageAction = h => poison.AddDamage(2); boss.RPC_Damage(0, Hit(2, "Astrid")); poison.UpdateStatusEffect(1);
            Check(Resolve(poison.LastTick) == "Ragnar", "Rejected weaker poison does not steal source");
            boss.DamageAction = h => poison.AddDamage(20); boss.RPC_Damage(0, Hit(2, "Astrid")); poison.UpdateStatusEffect(1);
            Check(Resolve(poison.LastTick) == "Astrid", "Stronger poison replaces source");
            boss.DamageAction = h => poison.AddDamage(poison.m_damageLeft); boss.RPC_Damage(0, Hit(1, "Ragnar")); poison.UpdateStatusEffect(1);
            Check(Resolve(poison.LastTick) == "Ragnar", "Equal poison replaces source as vanilla does");
            poison.AddDamage(50); poison.UpdateStatusEffect(1);
            Check(Resolve(poison.LastTick) == null, "Unknown poison replacement clears source");
            var fire = new SE_Burning { m_character = boss };
            boss.DamageAction = h => fire.AddFireDamage(10); boss.RPC_Damage(0, Hit(1, "SameName")); fire.UpdateStatusEffect(1);
            Check(Resolve(fire.LastTick) == "SameName", "Single-source fire supported");
            boss.DamageAction = h => fire.AddSpiritDamage(10); boss.RPC_Damage(0, Hit(2, "SameName")); fire.UpdateStatusEffect(1);
            Check(Resolve(fire.LastTick) == null, "Combined fire/Spirit from distinct IDs ambiguous despite same name");
            var other = new SE_Burning { m_character = new Character { Boss = true } };
            boss.DamageAction = h => other.AddSpiritDamage(10); boss.RPC_Damage(0, Hit(1, "Ragnar")); other.UpdateStatusEffect(1);
            Check(Resolve(other.LastTick) == null, "Different victim cannot inherit RPC context");
            var fresh = new SE_Burning { m_character = boss };
            boss.DamageAction = h => { fresh.AddSpiritDamage(3); throw new InvalidOperationException(); };
            try { boss.RPC_Damage(0, Hit(1, "Ragnar")); } catch (InvalidOperationException) { }
            fresh.AddSpiritDamage(4); fresh.UpdateStatusEffect(1);
            Check(Resolve(fresh.LastTick) == null, "RPC exception restores source context");
            fresh.ThrowOnTick = true;
            try { fresh.UpdateStatusEffect(1); } catch (InvalidOperationException) { }
            var unrelated = new HitData { m_hitType = HitData.HitType.Burning, m_damage = new HitData.DamageTypes { m_spirit = 1 } };
            boss.ApplyDamage(unrelated, true, false, HitData.DamageModifier.Normal);
            Check(Resolve(unrelated) == null, "Tick exception cannot attribute unrelated damage");
            Check(Resolve(Hit(2, "Astrid")) == "Astrid", "Direct hit attribution unchanged");
            var inherited = new SE_Burning { m_character = boss, m_spiritDamageLeft = 5 };
            boss.DamageAction = h => inherited.AddSpiritDamage(3); boss.RPC_Damage(0, Hit(1, "Ragnar")); inherited.UpdateStatusEffect(1);
            Check(Resolve(inherited.LastTick) == null, "Preexisting unknown pool is not claimed by a later hit");
            var combined = new SE_Burning { m_character = boss };
            boss.DamageAction = h => { combined.AddFireDamage(3); combined.AddSpiritDamage(3); };
            boss.RPC_Damage(0, Hit(1, "Ragnar")); combined.UpdateStatusEffect(1);
            Check(Resolve(combined.LastTick) == "Ragnar", "Combined effects from one known ID are attributable");
            boss.Owner = false; combined.UpdateStatusEffect(1);
            Check(Resolve(combined.LastTick) == null, "Non-owner ticks not attributed");
            boss.Owner = true; combined.UpdateStatusEffect(1);
            Check(Resolve(combined.LastTick) == null, "Observed ownership loss invalidates old effect provenance");
            var rejected = new SE_Burning { m_character = boss };
            boss.DamageAction = h => rejected.AddSpiritDamage(0.1f); boss.RPC_Damage(0, Hit(1, "Ragnar"));
            rejected.AddSpiritDamage(5); rejected.UpdateStatusEffect(1);
            Check(Resolve(rejected.LastTick) == null, "Rejected tiny Spirit addition does not claim future damage");
            PeriodicAttribution.Clear();
            Check(Resolve(savedTick) == null, "Shutdown clears retained attribution");
        }
        finally { harmony.UnpatchSelf(); PeriodicAttribution.Clear(); }
        Console.WriteLine("PASS: " + checks + " periodic attribution assertions with real Harmony and behavioral status effects.");
    }
}
