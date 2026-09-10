using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ValheimMoments;

// Behavioral stand-ins; production separately builds against installed game DLLs.
public class PlayerProfile
{
    public string Name = "Ragnar";
    public class Stats { public Dictionary<string, float>[] m_enemyStats = { new Dictionary<string, float>() }; }
    public Stats[] m_playerStats = { new Stats() };
    public string GetName() { return Name; }
}
public class Game
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void RegisterKill(long playerPeerID, string enemyName, int bossNumber, KillModifiers modifiers, int attackers, bool cheatsUsed) { }
    public PlayerProfile Profile = new PlayerProfile();
    public bool Skip;
    public PlayerProfile GetPlayerProfile() { return Profile; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void RPC_RegisterKill(long sender, string enemyName, int bossNumber, int modifiers, int attackers, bool cheatsUsed)
    {
        if (Skip) return;
        var counts = Profile.m_playerStats[0].m_enemyStats[0];
        float count; counts.TryGetValue(enemyName, out count); counts[enemyName] = count + 1;
    }
}
public enum KillModifiers { None }
public struct ZDOID
{
    public long Id;
    public bool IsNone() { return Id == 0; }
    public static bool operator ==(ZDOID a, ZDOID b) { return a.Id == b.Id; }
    public static bool operator !=(ZDOID a, ZDOID b) { return !(a == b); }
    public override bool Equals(object obj) { return obj is ZDOID && this == (ZDOID)obj; }
    public override int GetHashCode() { return Id.GetHashCode(); }
}
public class ZNet
{
    public static ZNet instance;
    public bool Server;
    public List<ZNetPeer> Peers = new List<ZNetPeer>();
    public bool IsServer() { return Server; }
    public List<ZNetPeer> GetPeers() { return Peers; }
    public ZNetPeer GetServerPeer() { return Server || Peers.Count == 0 ? null : Peers[0]; }
    public class PlayerInfo { public ZDOID m_characterID; public string m_name; }
    public List<PlayerInfo> Players = new List<PlayerInfo>();
    public List<PlayerInfo> GetPlayerList() { return Players; }
}
public class ZRoutedRpc
{
    public static ZRoutedRpc instance;
    public Action<long, string, string> Receiver;
    public Dictionary<string, Action<long, string, string>> Receivers = new Dictionary<string, Action<long, string, string>>();
    [MethodImpl(MethodImplOptions.NoInlining)] public ZRoutedRpc() { instance = this; }
    public void Register<T, U>(string name, Action<long, T, U> callback) { Receiver = (Action<long, string, string>)(object)callback; Receivers.Add(name, Receiver); }
    public void InvokeRoutedRPC(long peer, string name, object[] args) { Receivers[name](42, (string)args[0], (string)args[1]); }
}
internal static class BossTests
{
    static int checks;
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
    internal static void Run()
    {
        var harmony = new Harmony("valheimmoments.boss.tests");
        var events = new List<BossKill>(); int errors = 0;
        BossKillDetector.OnKill = events.Add;
        BossKillDetector.OnError = () => errors++;
        BossKillDetector.Install(harmony);
        try
        {
            var game = new Game();
            game.RPC_RegisterKill(0, "$enemy_boar", 0, 0, 1, false);
            Check(events.Count == 0, "Ordinary enemy excluded");
            game.RPC_RegisterKill(0, "$enemy_eikthyr", 1, 0, 2, false);
            Check(events.Count == 1 && events[0].FirstKill, "First credited boss kill");
            Check(events[0].EnemyKey == "$enemy_eikthyr" && events[0].PlayerName == "Ragnar", "Boss identity and player");
            game.RPC_RegisterKill(0, "$enemy_eikthyr", 1, 0, 2, false);
            Check(events.Count == 2 && !events[1].FirstKill, "Repeat distinguished");
            game.Profile.m_playerStats[0].m_enemyStats[0]["$enemy_elder"] = 4;
            game.RPC_RegisterKill(0, "$enemy_elder", 2, 0, 1, false);
            Check(!events[2].FirstKill, "Existing loaded history respected");
            game.Profile = new PlayerProfile();
            game.RPC_RegisterKill(0, "$enemy_eikthyr", 1, 0, 1, false);
            Check(events[3].FirstKill, "Different character independent");
            game.Skip = true;
            game.RPC_RegisterKill(0, "$enemy_eikthyr", 1, 0, 1, false);
            Check(events.Count == 4, "No event without applied credit");
            game.Skip = false;
            BossKillDetector.OnKill = k => { throw new Exception("test"); };
            game.RPC_RegisterKill(0, "$enemy_eikthyr", 1, 0, 1, false);
            Check(errors == 1 && game.Profile.m_playerStats[0].m_enemyStats[0]["$enemy_eikthyr"] == 2, "Callback failure isolated");
            float count;
            game.Profile.m_playerStats = null;
            Check(!BossKillDetector.TryCount(game.Profile, "boss", out count), "Missing history fails closed");
            game.Profile = new PlayerProfile();
            game.Profile.m_playerStats[0].m_enemyStats[0]["boss"] = float.NaN;
            Check(!BossKillDetector.TryCount(game.Profile, "boss", out count), "Invalid history fails closed");
        }
        finally { harmony.UnpatchSelf(); BossKillDetector.OnKill = null; BossKillDetector.OnError = null; }
        Check(EventMessages.Boss("{player} defeated {boss}", "Eikthyr", "Ragnar") == "Ragnar defeated Eikthyr", "Boss message placeholders");
        Check(EventMessages.Boss("{boss}/{player}", "", "") == "Boss/A player", "Missing name fallback");
        Check(EventMessages.Boss(new string('a', 1999) + "\uD83C\uDFC6x", "", "").Length == 1999, "Surrogate-safe Discord limit");
        string reason;
        Check(BossAttribution.Resolve(null, out reason) == null && reason == "no recorded damage", "Missing hit diagnostic");
        ZNet.instance = new ZNet();
        ZNet.instance.Players.Add(new ZNet.PlayerInfo { m_characterID = new ZDOID { Id = 42 }, m_name = "Astrid" });
        Check(BossAttribution.Resolve(new HitData { m_attacker = new ZDOID { Id = 42 } }, out reason) == "Astrid", "Exact attacker network ID fallback");
        Check(BossAttribution.Resolve(new HitData { m_attacker = new ZDOID { Id = 43 } }, out reason) == null, "Unknown ID never guessed");
        Check(BossAttribution.Resolve(new HitData { m_hitType = HitData.HitType.Poisoned }, out reason) == null && reason.Contains("Poisoned"), "Anonymous status damage remains unavailable with reason");
        Check(BossAttribution.Resolve(new HitData { m_attacker = new ZDOID { Id = 42 }, Attacker = new Character() }, out reason) == null, "Non-player attacker cannot be replaced by fallback");
        ZNet.instance = null;
        Check(EventMessages.Boss("{boss} defeated", "Eikthyr", "Ragnar", BossNameMode.Both, "Astrid") == "Eikthyr defeated\nKill credit: Ragnar\nFinal blow: Astrid", "Both names appended to legacy template");
        Check(EventMessages.Boss("{player}: {boss}", "Eikthyr", "Ragnar", BossNameMode.FinalBlow, "Astrid") == "Astrid: Eikthyr", "Final blow player placeholder");
        Check(EventMessages.Boss("{credit}/{killer}", "Eikthyr", "Ragnar", BossNameMode.Both, "Astrid") == "Ragnar/Astrid", "Explicit placeholders without duplicate lines");
        Check(EventMessages.Boss("Boss", "Eikthyr", "Ragnar", BossNameMode.KillCredit, "Astrid") == "Boss\nKill credit: Ragnar", "Credit-only mode");
        Check(EventMessages.Boss("Boss", "Eikthyr", "Ragnar", BossNameMode.FinalBlow) == "Boss\nFinal blow: unavailable", "No guessed final blow");
        var inbox = new AttributionInbox();
        inbox.Add(1, "boss", "Astrid", 0);
        Check(inbox.Take(2, "boss", 1) == null, "Metadata sender isolated");
        Check(inbox.Take(1, "other", 1) == null, "Metadata enemy isolated");
        Check(inbox.Take(1, "boss", 1) == "Astrid" && inbox.Take(1, "boss", 1) == null, "Metadata consumed once");
        inbox.Add(1, "boss", "Astrid", 0);
        Check(inbox.Take(1, "boss", 6) == null, "Stale attribution expires");
        for (int i = 0; i < 65; i++) inbox.Add(i, "boss", "Astrid", 10);
        Check(inbox.Take(0, "boss", 10) == null && inbox.Take(64, "boss", 10) == "Astrid", "Inbox bounded");
        BossAttribution.Install(harmony);
        try
        {
            var router = new ZRoutedRpc();
            var victim = new Character { Boss = true, m_name = "boss" };
            ZNet.instance = new ZNet();
            ZNet.instance.Players.Add(new ZNet.PlayerInfo { m_characterID = new ZDOID { Id = 1 }, m_name = "Ragnar" });
            ZNet.instance.Players.Add(new ZNet.PlayerInfo { m_characterID = new ZDOID { Id = 2 }, m_name = "Astrid" });
            ZNet.instance.Players.Add(new ZNet.PlayerInfo { m_characterID = new ZDOID { Id = 3 }, m_name = "Spectator" });
            var creditView = new ZNetView(); victim.SetCreditView(creditView);
            creditView.Data.Flags.Add(ZDOVars.s_attackers.ToString() + "Ragnar");
            creditView.Data.Flags.Add(ZDOVars.s_attackers.ToString() + "Astrid");
            creditView.Data.Flags.Add(ZDOVars.s_attackers.ToString() + "Disconnected");
            Check(BossAttribution.ResolveCredits(victim) == "Astrid, Ragnar", "Roster matches vanilla attacker flags; spectator and disconnected names excluded");
            victim.SetHit(new HitData { Attacker = new Player { Name = "Astrid" } });
            victim.DeathAction = () => {
                Check(BossAttribution.Take(0, "boss") == "Astrid", "Local final blow read during death");
                Check(BossAttribution.TakeCredits(0, "boss") == "Astrid, Ragnar", "Local owner reads complete roster during death");
                new Game().RegisterKill(99, "boss", 1, KillModifiers.None, 2, false);
                Check(BossAttribution.TakeCredits(43, "boss") == null, "Credit roster is sender scoped");
                Check(BossAttribution.TakeCredits(42, "other") == null, "Credit roster is enemy scoped");
                Check(BossAttribution.TakeCredits(42, "boss") == "Astrid, Ragnar" && BossAttribution.TakeCredits(42, "boss") == null, "Remote roster delivered before credit and consumed once");
                Check(BossAttribution.Take(42, "boss") == "Astrid", "Remote attribution sent with credited kill");
            };
            victim.OnDeath();
            Check(BossAttribution.Take(0, "boss") == null, "Context cleared after death");
            Check(BossAttribution.TakeCredits(0, "boss") == null, "Roster context also cleared after death");
            BossKillDetector.Install(harmony);
            BossKill recorded = null;
            BossKillDetector.OnKill = k => recorded = k;
            victim.DeathAction = () => new Game().RPC_RegisterKill(0, "boss", 1, 0, 2, false);
            victim.OnDeath();
            Check(recorded != null && recorded.FinalBlowName == "Astrid" && recorded.PlayerName == "Ragnar", "Credited event carries distinct final-blow identity");
            Check(recorded.CreditNames == "Astrid, Ragnar" && recorded.FirstKill, "Full roster does not change recorder or personal first-kill state");
            var remoteGame = new Game { Profile = new PlayerProfile { Name = "Astrid" } };
            victim.DeathAction = () => { remoteGame.RegisterKill(99, "boss", 1, KillModifiers.None, 2, false); remoteGame.RPC_RegisterKill(42, "boss", 1, 0, 2, false); };
            victim.OnDeath();
            Check(recorded.PlayerName == "Astrid" && recorded.CreditNames == "Astrid, Ragnar" && recorded.FinalBlowName == "Astrid", "Other recorder receives both credited players with independent final blow");
            string caption = EventMessages.RecordedPost(EventMessages.Boss("Boss", "boss", BossAttribution.CreditLabel(recorded.CreditNames, recorded.PlayerName), BossNameMode.Both, recorded.FinalBlowName), recorded.PlayerName);
            Check(caption == "# Boss\n**Recorded by:** Astrid\n**Kill credit:** Astrid, Ragnar\n**Final blow:** Astrid", "Discord post keeps roster, recorder and final blow distinct");
            remoteGame.Skip = true; recorded = null; victim.OnDeath();
            Check(recorded == null, "Roster metadata cannot create a clip when vanilla denies local credit");
            remoteGame.Skip = false;
            victim.SetHit(new HitData { Attacker = new Character { Name = "Another creature" } });
            victim.OnDeath();
            Check(string.IsNullOrEmpty(recorded.FinalBlowName), "Non-player last hit never labeled a player");
            victim.DeathAction = () => { throw new InvalidOperationException("test"); };
            try { victim.OnDeath(); } catch (InvalidOperationException) { }
            Check(BossAttribution.Take(0, "boss") == null, "Context cleared on original exception");
            Check(BossAttribution.TakeCredits(0, "boss") == null, "Roster context cleared on original exception");
            victim.Owner = false;
            victim.DeathAction = () => { Check(BossAttribution.Take(0, "boss") == null, "Non-owner cannot assert final blow"); Check(BossAttribution.TakeCredits(0, "boss") == null, "Non-owner cannot assert roster"); };
            victim.OnDeath();
            creditView.Data.Flags.Clear();
            Check(BossAttribution.ResolveCredits(victim) == null, "No credited-player flags produces unknown roster, not nearby names");
            Check(BossAttribution.CreditLabel(null, "Ragnar") == "Ragnar (full list unavailable)", "Missing remote metadata explicitly marks incomplete credit");
            Check(BossAttribution.FormatCredits(new[] { "Ragnar", "Astrid", "Ragnar", "*Viking*\n`One`" }) == "Astrid, Ragnar, Viking One", "Roster formatting deduplicates and removes line/Markdown injection");
            var many = new List<string>(); for (int i = 0; i < 40; i++) many.Add(i.ToString("D2") + new string('a', 78));
            string bounded = BossAttribution.FormatCredits(many);
            Check(bounded.Length <= 1024 && bounded.Contains("more)"), "Large roster stays bounded with explicit omitted count");
            var rosterInbox = new AttributionInbox(1024); rosterInbox.Add(1, "boss", bounded, 0);
            Check(rosterInbox.Take(1, "boss", 1) == bounded, "Full bounded roster fits its independent metadata inbox");
            rosterInbox.Add(1, "boss", new string('x', 1025), 0);
            Check(rosterInbox.Take(1, "boss", 1) == null, "Oversized roster rejected");
            rosterInbox.Add(1, "boss", "Astrid, Ragnar", 0);
            Check(rosterInbox.Take(1, "boss", 6) == null, "Stale roster expires");
        }
        finally { harmony.UnpatchSelf(); BossAttribution.Clear(); BossKillDetector.OnKill = null; ZNet.instance = null; }
        Console.WriteLine("PASS: " + checks + " boss assertions using real Harmony and behavioral game stand-ins.");
    }
}
