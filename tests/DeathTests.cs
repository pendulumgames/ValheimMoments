using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ValheimMoments;

// A behavioral stand-in, not evidence of game API compatibility. The production
// plugin separately compiles against the inspected, real game assembly.
public class Character
{
    public string m_name;
    public bool Boss, Owner = true;
    public bool IsBoss() { return Boss; }
    public bool IsOwner() { return Owner; }
    public Action DeathAction;
    [MethodImpl(MethodImplOptions.NoInlining)] public virtual void OnDeath() { DeathAction?.Invoke(); }
    protected HitData m_lastHit;
    public string Name;
    public virtual string GetHoverName() { return Name; }
    public void SetHit(HitData hit) { m_lastHit = hit; }
    public Action<HitData> DamageAction;
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_Damage(long sender, HitData hit) { DamageAction?.Invoke(hit); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void ApplyDamage(HitData hit, bool show, bool trigger, HitData.DamageModifier modifier) { m_lastHit = hit; }
}
public class HitData
{
    public ZDOID m_attacker;
    public enum DamageModifier { Normal }
    public struct DamageTypes { public float m_fire, m_spirit, m_poison; }
    public DamageTypes m_damage;
    public enum HitType { Undefined, EnemyHit, PlayerHit, Fall, Drowning, Burning, Freezing, Poisoned, Water, Smoke, EdgeOfWorld, Impact, Cart, Tree, Self, Structural, Turret, Boat, Stalagtite, Catapult, CinderFire, AshlandsOcean, AshlandsLava, Incinerator, DrawBridge }
    public HitType m_hitType;
    public Character Attacker;
    public Character GetAttacker() { return Attacker; }
}
public class Player : Character
{
    public static Player m_localPlayer;
    public bool Dead;
    public string GetPlayerName() { return Name; }
    public bool IsDead() { return Dead; }
    [MethodImpl(MethodImplOptions.NoInlining)] public override void OnDeath() { if (Owner) Dead = true; }
}
internal static class DeathTests
{
    private static int checks;
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
    public static void Main()
    {
        BossTests.Run();
        LootTests.Run();
        FilterTests.Run();
        EpicTests.Run();
        RelayTests.Run();
        PeriodicTests.Run();
        var harmony = new Harmony("valheimmoments.tests");
        int events = 0;
        int errors = 0;
        PlayerDeathDetector.OnError = () => errors++;
        string recordedCause = null;
        PlayerDeathDetector.OnLocalDeath = (p, cause) => { events++; recordedCause = cause; };
        PlayerDeathDetector.Install(harmony);
        try
        {
            var local = new Player(); Player.m_localPlayer = local;
            new Player().OnDeath(); Check(events == 0, "Remote player excluded");
            local.Owner = false; local.OnDeath(); Check(events == 0, "Non-owner early return excluded");
            local.SetHit(new HitData { m_hitType = HitData.HitType.EnemyHit, Attacker = new Character { Name = "Greydwarf" } });
            local.Owner = true; local.OnDeath(); Check(events == 1 && local.Dead, "Local death fires and original runs");
            Check(recordedCause == "Greydwarf", "Named attacker captured");
            local.OnDeath(); Check(events == 1, "Repeated death ignored");
            local.Dead = false; local.OnDeath(); Check(events == 2, "Respawn permits next death");
            PlayerDeathDetector.OnLocalDeath = (p, cause) => { throw new Exception("test"); };
            local.Dead = false; local.OnDeath(); Check(local.Dead, "Callback failure cannot break death");
            Check(errors == 1, "Callback error reported safely");
        }
        finally { harmony.UnpatchSelf(); PlayerDeathDetector.OnLocalDeath = null; }
        Check(EventMessages.Death("{player} died", true, "", "Ragnar") == "Ragnar died", "Character name");
        Check(EventMessages.Death("{player} died", true, "Custom", "Ragnar") == "Custom died", "Override");
        Check(EventMessages.Death("{player} died", false, "Custom", "Ragnar") == "A player died", "Name hidden");
        Check(EventMessages.Death(new string('x', 2100), true, "", "").Length == 2000, "Discord length bound");
        Check(EventMessages.Death("{player} died", true, "", "Ragnar", true, "Greydwarf") == "Ragnar died\nCause: Greydwarf", "Existing templates gain cause");
        Check(EventMessages.Death("{player}: {cause}", true, "", "Ragnar", true, "poison") == "Ragnar: poison", "Cause placeholder");
        Check(EventMessages.Death("{player} died", true, "", "Ragnar", false, "poison") == "Ragnar died", "Cause disabled");
        Check(DeathCause.Label(HitData.HitType.Tree) == "a falling tree", "Tree category");
        Check(DeathCause.Label(HitData.HitType.Drowning) == "drowning", "Drowning category");
        Check(DeathCause.Label((HitData.HitType)999) == "unknown cause", "Unknown category fallback");
        var victim = new Player();
        Check(DeathCause.Read(victim) == "unknown cause", "Missing hit fallback");
        victim.SetHit(new HitData { m_hitType = HitData.HitType.EnemyHit });
        Check(DeathCause.Read(victim) == "an enemy (attacker unavailable)", "Missing attacker fallback");
        victim.SetHit(new HitData { m_hitType = HitData.HitType.Poisoned, Attacker = new Character { Name = "Earlier attacker" } });
        Check(DeathCause.Read(victim) == "poison", "Status cause takes precedence");
        Console.WriteLine("PASS: " + checks + " death/message assertions using real Harmony and a behavioral Player stand-in.");
    }
}
