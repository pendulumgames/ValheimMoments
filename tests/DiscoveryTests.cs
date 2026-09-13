using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using ValheimMoments;

namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; public static float Distance(Vector3 a, Vector3 b) { return Math.Abs(a.x - b.x); } }
    public class Transform { public Vector3 position; }
    public partial class Component { public Transform transform = new Transform(); }
    public static class Mathf { public static float Clamp(float value, float min, float max) { return Math.Max(min, Math.Min(max, value)); } }
}
public class AltBiome { public string m_name, m_nameOverride, m_namePrefix, m_nameSuffix; }
public class BiomeSector
{
    public int Biome = 1;
    public List<AltBiome> AltBiomes = new List<AltBiome>();
    public string Display = "Meadows";
    public static string GetBiomeName(int biome) { return "Meadows"; }
    public string GetName(bool debug) { return Display; }
}
public partial class Player
{
    public Action BiomeAction;
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateBiome(float delta) { BiomeAction?.Invoke(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void AddKnownBiome(BiomeSector sector) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void AddKnownLocationName(string name) { }
}
public class Trader : UnityEngine.Component
{
    public string m_name = "$npc_trader";
    public float m_greetRange = 10;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { }
}
internal static class DiscoveryTests
{
    private static int checks;
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
    private static void Ready(DiscoveryHistory history)
    {
        var clock = Stopwatch.StartNew();
        while (!history.Ready && clock.ElapsedMilliseconds < 5000) { history.Tick(); Thread.Sleep(1); }
        Check(history.Ready, "History becomes ready within deadline");
    }
    private static void Reject(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
        bool rejected = false;
        try { DiscoveryJournal.Load(path); } catch (Exception e) { rejected = e is IOException || e is InvalidDataException || e is ArgumentException; }
        Check(rejected, "Malformed history rejected");
    }
    internal static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ValheimMoments-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "journal.bin");
            var journal = new DiscoveryJournal();
            Check(journal.Visit("biome:1"), "First visit");
            Check(!journal.Visit("biome:1"), "Repeated visit");
            Check(!journal.Visit(" ") && !journal.Visit(new string('x', 193)), "Invalid keys bounded");
            Check(journal.Visit("location:石"), "Unicode identity");
            DiscoveryJournal.Save(path, journal.Snapshot());
            Check(!File.Exists(path + ".pending"), "Atomic save consumes temporary file");
            var loaded = DiscoveryJournal.Load(path);
            Check(!loaded.Dirty && loaded.Seen.SetEquals(journal.Seen), "Round trip");
            Check(loaded.Visit("trader:test"), "Extend loaded history");
            DiscoveryJournal.Save(path, loaded.Snapshot());
            Check(DiscoveryJournal.Load(path).Seen.Count == 3, "Atomic replacement");
            byte[] good = loaded.Snapshot();
            Reject(path, new byte[] { 0 });
            var bad = (byte[])good.Clone(); bad[0] = 0; Reject(path, bad);
            bad = new byte[good.Length + 1]; Buffer.BlockCopy(good, 0, bad, 0, good.Length); Reject(path, bad);
            Reject(path, new byte[262145]);
            for (int i = 0; i < 300; i++) journal.Visit("key:" + i);
            Check(journal.Seen.Count == 256 && !journal.Visit("beyond"), "Ledger cannot grow unbounded");
            File.Delete(path);

            // Construct the pre-fix on-disk format, including duplicate main biomes.
            var legacy = new DiscoveryJournal();
            legacy.Seen.Add("biome:1/");
            legacy.Seen.Add("biome:1/12:Dark Meadows/8:Mushroom");
            legacy.Seen.Add("biome:4/17:Fortress Mountain");
            legacy.Seen.Add("trader:test");
            DiscoveryJournal.Save(path, legacy.Snapshot());
            var normalized = DiscoveryJournal.Load(path);
            Check(!normalized.Dirty && normalized.Seen.Count == 4, "Legacy variant evidence retained for migration");
            Check(!normalized.Visit("biome:1", "biome:1/"), "Known base biome migrates silently");
            Check(!normalized.Visit("biome:1/name:dark", "biome:1/12:Dark Meadows/8:Mushroom"), "Known named sub-biome migrates silently");
            Check(normalized.Visit("biome:1/name:other"), "Unseen named sub-biome remains independent of base and known variants");
            Check(normalized.Visit("biome:2"), "New main biome still qualifies");
            DiscoveryJournal.Save(path, normalized.Snapshot());
            Check(!DiscoveryJournal.Load(path).Visit("biome:1/name:dark"), "Named sub-biome history survives a process restart");
            File.Delete(path);

            int errors = 0;
            var history = new DiscoveryHistory(folder, _ => errors++);
            Check(!history.Use(0, 1) && !history.Visit("invalid"), "No identity means no discoveries");
            Check(history.Use(1, 10), "First character/world context");
            Check(!history.Visit("startup"), "Pending load visits are baseline only");
            Ready(history);
            Check(!history.Visit("startup") && history.Visit("new"), "Startup seeded, later visit accepted");
            history.Flush().GetAwaiter().GetResult(); history.Tick();
            history.Use(1, 20); Ready(history);
            Check(history.Visit("new"), "Same character in another world is independent");
            history.Use(2, 10); Ready(history);
            Check(history.Visit("new"), "Another character in original world is independent");
            history.Use(1, 10); Ready(history);
            Check(!history.Visit("new"), "Returning context retains prior discovery");
            history.Flush().GetAwaiter().GetResult(); history.Tick();
            history.Use(3, 10); history.Tick(); history.Use(1, 10);
            Check(!history.Ready && !history.Visit("while-switching"), "Context switch during loading cannot emit");
            Ready(history);
            Check(!history.Visit("while-switching"), "Visits during switch back baseline safely");
            history.Flush().GetAwaiter().GetResult(); history.Tick();
            var reopened = new DiscoveryHistory(folder, _ => errors++);
            reopened.Use(1, 10); Ready(reopened);
            Check(!reopened.Visit("new"), "Restart deduplication");
            string oldIdentity;
            var sectorIdentity = new BiomeSector();
            string mainBiome = "biome:" + DiscoveryDetector.BiomeIdentity(sectorIdentity, out oldIdentity);
            sectorIdentity.AltBiomes.Add(new AltBiome { m_name = "Dark Meadows", m_namePrefix = "$dark" });
            string namedBiome = "biome:" + DiscoveryDetector.BiomeIdentity(sectorIdentity, out oldIdentity);
            Check(namedBiome != mainBiome, "Named sub-biome is distinct from main biome");
            sectorIdentity.AltBiomes.Add(new AltBiome { m_name = "Mushroom" });
            Check("biome:" + DiscoveryDetector.BiomeIdentity(sectorIdentity, out oldIdentity) == namedBiome, "Hidden modifier does not change named sub-biome identity");
            sectorIdentity.Display = "Localized name";
            Check("biome:" + DiscoveryDetector.BiomeIdentity(sectorIdentity, out oldIdentity) == namedBiome, "Translation cannot rediscover named sub-biome");
            sectorIdentity.AltBiomes[0].m_namePrefix = "$misty";
            string anotherBiome = "biome:" + DiscoveryDetector.BiomeIdentity(sectorIdentity, out oldIdentity);
            Check(anotherBiome != namedBiome, "Different named sub-biome remains discoverable");
            var allDiscoveries = new[] { mainBiome, namedBiome, anotherBiome, "trader:$npc_hildir", "trader:$npc_haldor", "location:$dungeon" };
            foreach (string discovery in allDiscoveries) Check(reopened.Visit(discovery), "Each discovery initially qualifies: " + discovery);
            reopened.Flush().GetAwaiter().GetResult(); reopened.Tick();
            reopened.Use(0, 0); reopened.Tick(); reopened.Use(1, 10); Ready(reopened);
            foreach (string discovery in allDiscoveries) Check(!reopened.Visit(discovery), "Reconnect with game open retains: " + discovery);
            var relaunched = new DiscoveryHistory(folder, _ => errors++);
            relaunched.Use(1, 10); Ready(relaunched);
            foreach (string discovery in allDiscoveries) Check(!relaunched.Visit(discovery), "Game restart retains: " + discovery);
            string persistentFolder = Path.Combine(folder, "persistent");
            var migrated = new DiscoveryHistory(persistentFolder, _ => errors++, folder);
            migrated.Use(1, 10); Ready(migrated);
            Check(!migrated.Visit("new"), "Upgrade migrates install-folder history before accepting discoveries");
            migrated.Flush().GetAwaiter().GetResult(); migrated.Tick();
            var afterRestart = new DiscoveryHistory(persistentFolder, _ => errors++, Path.Combine(folder, "removed-plugin"));
            afterRestart.Use(1, 10); Ready(afterRestart);
            Check(!afterRestart.Visit("new"), "Restart after plugin replacement retains migrated history");
            Check(afterRestart.Visit("genuinely-new"), "Migration does not suppress unseen discoveries");
            afterRestart.Flush().GetAwaiter().GetResult(); afterRestart.Tick();
            Check(!DiscoveryJournal.Load(Path.Combine(folder, 1L.ToString("X16") + "-" + 10L.ToString("X16") + ".bin")).Seen.Contains("genuinely-new"), "Legacy journal remains untouched");
            string broken = Path.Combine(folder, 9L.ToString("X16") + "-" + 10L.ToString("X16") + ".bin");
            File.WriteAllText(broken, "broken");
            history.Use(9, 10);
            var deadline = Stopwatch.StartNew();
            while (errors == 0 && deadline.ElapsedMilliseconds < 5000) { history.Tick(); Thread.Sleep(1); }
            Check(errors == 1 && !history.Ready && !history.Visit("no-replay"), "Corruption fails closed");
            history.Flush().GetAwaiter().GetResult();
            Check(File.ReadAllText(broken) == "broken", "Corrupt history is not overwritten");
            string blockedFolder = Path.Combine(folder, "blocked-folder");
            File.WriteAllText(blockedFolder, "not a directory");
            var unwritable = new DiscoveryHistory(blockedFolder, _ => { });
            unwritable.Use(1, 10); Ready(unwritable);
            Check(unwritable.Visit("cannot-commit"), "Visit is staged before disk commit");
            var commit = unwritable.Flush();
            bool commitFailed = false;
            try { commit.GetAwaiter().GetResult(); } catch (IOException) { commitFailed = true; }
            unwritable.Tick();
            Check(commitFailed && commit.IsFaulted && !unwritable.Ready, "Failed durable commit cannot authorize an announcement or further visits");
            for (int i = Directory.GetFiles(folder).Length; i < 128; i++) File.WriteAllText(Path.Combine(folder, "capacity-" + i), "");
            bool full = false;
            try { DiscoveryJournal.Load(Path.Combine(folder, "new-context.bin")); } catch (IOException) { full = true; }
            Check(full, "Context-file capacity fails closed without evicting old history");
        }
        finally { Directory.Delete(folder, true); }

        var selector = new SpecialEnemies();
        var kill = new BossKill { EnemyKey = "$enemy_troll", FirstKill = true, Special = true };
        Check(!selector.Eligible(kill, false, 0), "No implicit special enemies");
        selector.Configure(" $enemy_troll; $enemy_wraith, $enemy_troll\n$enemy_other");
        Check(selector.Eligible(kill, true, 0), "Configured first credited kill");
        kill.FirstKill = false;
        Check(!selector.Eligible(kill, true, 0) && selector.Eligible(kill, false, 0), "First-only policy");
        kill.BossNumber = 1; Check(!selector.Eligible(kill, false, 0), "Boss never double-captured"); kill.BossNumber = 0;
        selector.Captured(kill.EnemyKey, 10, 60);
        Check(!selector.Eligible(kill, false, 69) && selector.Eligible(kill, false, 70), "Cooldown boundary");
        Check(selector.Eligible(new BossKill { EnemyKey = "$enemy_wraith" }, false, 11), "Per-enemy cooldown");
        Check(!selector.Eligible(new BossKill { EnemyKey = "$ENEMY_TROLL" }, false, 80), "Keys are exact case-sensitive IDs");
        selector.Clear(); Check(selector.Eligible(kill, false, 0), "Session resets cooldown");
        selector.Configure("*"); Check(!selector.Eligible(kill, false, 0), "No implicit wildcard");
        selector.Configure(new string('x', 4097)); Check(!selector.Eligible(kill, false, 0), "Overlong configuration fails closed");
        var keys = new List<string>(); for (int i = 0; i < 65; i++) keys.Add("enemy" + i);
        selector.Configure(string.Join(",", keys));
        Check(selector.Eligible(new BossKill { EnemyKey = "enemy63" }, false, 0) && !selector.Eligible(new BossKill { EnemyKey = "enemy64" }, false, 0), "Selection capped at 64 unique keys");
        Check(RelayProtocol.ValidKind("discovery") && RelayProtocol.ValidKind("special") && !RelayProtocol.ValidKind("arbitrary"), "Relay admits only supported kinds");
        Check(EventMessages.Discovery("Found {discovery} by {player}", "Forest", "Ragnar") == "Found Forest by Ragnar", "Discovery caption tokens");
        string caption = EventMessages.RecordedPost(EventMessages.Discovery(new string('x', 1899) + "\uD83E\uDDED", "Forest", "Ragnar"), "Ragnar");
        Check(caption.Length <= 2000 && caption.Contains("**Recorded by:** Ragnar"), "Long caption reserves recorder space");
        Hooks();
        Console.WriteLine("PASS: " + checks + " discovery/special-enemy assertions (bounded persistence and real Harmony with behavioral stand-ins).");
    }
    private static void Hooks()
    {
        var harmony = new Harmony("valheimmoments.discovery.tests");
        var observations = new List<string>(); var identities = new List<string>(); int errors = 0;
        DiscoveryDetector.OnObserved = (kind, key, name, legacy) => { observations.Add(kind); identities.Add(key); };
        DiscoveryDetector.OnError = () => errors++;
        DiscoveryDetector.Install(harmony);
        try
        {
            var local = new Player(); Player.m_localPlayer = local;
            var sector = new BiomeSector();
            local.AddKnownBiome(sector); local.AddKnownLocationName("$place");
            Check(observations.Count == 0, "Direct/save-like calls outside exploration excluded");
            local.BiomeAction = () => { local.AddKnownBiome(sector); local.AddKnownLocationName("$place"); };
            local.UpdateBiome(1); local.UpdateBiome(1);
            Check(observations.Count == 4, "Previously known global events still reach per-world ledger");
            string original = identities[0]; sector.Display = "Prairies"; local.UpdateBiome(1);
            Check(identities[4] == original, "Display localization cannot reset identity");
            sector.AltBiomes.Add(new AltBiome { m_name = "variant" }); local.UpdateBiome(1);
            Check(identities[6] == original, "Sector modifiers cannot rediscover the same main biome");
            var remote = new Player(); remote.BiomeAction = () => remote.AddKnownBiome(sector); remote.UpdateBiome(1);
            Check(observations.Count == 8, "Remote player excluded");
            local.BiomeAction = () => { throw new InvalidOperationException(); };
            try { local.UpdateBiome(1); } catch (InvalidOperationException) { }
            local.AddKnownBiome(sector); Check(observations.Count == 8, "Exception restores exploration scope");
            var trader = new Trader(); trader.transform.position = new UnityEngine.Vector3 { x = 50 }; trader.Update();
            Check(observations.Count == 8, "Distant trader greeting another player excluded");
            DiscoveryDetector.Clear(); trader.transform.position = new UnityEngine.Vector3 { x = 5 }; trader.Update(); trader.Update();
            Check(observations.Count == 9 && observations[8] == "trader", "Local trader entry only once while near");
            Check(errors == 0, "Hook observations produce no errors");
            sector.AltBiomes[0].m_namePrefix = "$dark";
            local.BiomeAction = () => local.AddKnownBiome(sector);
            local.UpdateBiome(1);
            Check(observations.Count == 11 && observations[9] == "biome" && observations[10] == "subbiome", "Named areas report main biome independently of optional sub-biome");
            DiscoveryDetector.OnObserved = (kind, key, name, legacy) => { throw new Exception(); };
            local.BiomeAction = () => local.AddKnownBiome(sector); local.UpdateBiome(1);
            Check(errors == 1, "Optional discovery callback failure cannot break game exploration");
        }
        finally { harmony.UnpatchSelf(); DiscoveryDetector.Clear(); DiscoveryDetector.OnObserved = null; DiscoveryDetector.OnError = null; }
    }
}
