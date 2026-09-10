using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ValheimEventClips;
using UnityEngine;

namespace UnityEngine
{
    public class GameObject
    {
        public string name;
        public ItemDrop Item;
        public T GetComponent<T>() where T : class { return Item as T; }
    }
}
public class ItemDrop
{
    public ItemData m_itemData = new ItemData();
    public class ItemData
    {
        public int m_stack = 1;
        public SharedData m_shared = new SharedData();
        public class SharedData { public string m_name; }
    }
}
public class CharacterDrop
{
    private readonly Character m_character;
    public CharacterDrop(Character character) { m_character = character; }
    public List<KeyValuePair<GameObject, int>> Drops = new List<KeyValuePair<GameObject, int>>();
    public int Rolls;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<KeyValuePair<GameObject, int>> GenerateDropList() { Rolls++; GC.KeepAlive(m_character); return Drops; }
}
internal static class LootTests
{
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    internal static void Run()
    {
        var loot = new BossLoot { Observed = true };
        loot.Add("B", "Zebra", 1); loot.Add("A", "Antler", 2); loot.Add("A", "Antler", 1);
        Check(loot.Items.Count == 2 && loot.Items[1].Quantity == 3, "Duplicate prefabs grouped");
        Check(loot.Display(1, true, s => s) == "* Antler \u00D73\n+1 more item types", "Deterministic sorting and display cap");
        Check(loot.Display(5, false, s => s) == "* Antler\n* Zebra", "Quantity can be hidden");
        Check(new BossLoot().Display(5, true, s => s) == "unavailable", "Missing roll distinct from empty");
        Check(new BossLoot { Observed = true }.Display(5, true, s => s) == "No items generated.", "Verified empty roll");
        Check(loot.Display(5, true, s => "localized " + s).StartsWith("* localized Antler"), "Name localization applied");
        Check(EventMessages.FormatPost("Boss defeated!\nKill credit: Ragnar\nFinal blow: Astrid") == "# Boss defeated!\n**Kill credit:** Ragnar\n**Final blow:** Astrid", "Post heading and bold attribution");
        Check(EventMessages.FormatPost("# Boss defeated!\n**Kill credit:** Ragnar") == "# Boss defeated!\n**Kill credit:** Ragnar", "Existing user Markdown preserved without duplication");
        Check(EventMessages.Heading("## Generated Loot", 2) == "## Generated Loot", "Existing loot heading preserved");
        Check(EventMessages.FormatPost(new string('a', 1997) + "\uD83D\uDE00").Length == 1999, "Added heading retains surrogate-safe Discord limit");
        var decoded = BossLoot.Decode(loot.Encode());
        Check(decoded != null && decoded.Display(5, true, s => s) == loot.Display(5, true, s => s), "Wire round trip");
        Check(BossLoot.Decode("not base64") == null && BossLoot.Decode(new string('A', 65537)) == null, "Invalid and oversized payload rejected");
        var bounded = new BossLoot { Observed = true };
        for (int i = 0; i < 65; i++) bounded.Add(i.ToString(), "Item", 1);
        Check(bounded.Items.Count == 64 && bounded.Incomplete, "Snapshot bounded with partial marker");
        bounded.Add("0", "Item", int.MaxValue);
        Check(bounded.Items[0].Quantity == 1, "Quantity overflow rejected");
        var inbox = new LootInbox(); string token = Guid.NewGuid().ToString("N");
        inbox.Announce(42, "boss", token, 0);
        Check(inbox.Take(43, "boss", 1) == null, "Credit sender isolation");
        var pending = inbox.Take(42, "boss", 1);
        Check(pending != null && !pending.Observed && inbox.Take(42, "boss", 1) == null, "Credit consumed once before loot arrives");
        inbox.Complete(43, token, loot.Encode(), 2);
        Check(!pending.Observed, "Result sender isolation");
        inbox.Complete(42, Guid.NewGuid().ToString("N"), loot.Encode(), 2);
        Check(!pending.Observed, "Result kill token isolation");
        inbox.Complete(42, token, loot.Encode(), 2);
        Check(pending.Observed && pending.Items.Count == 2, "Late result enriches retained event");
        inbox.Complete(42, token, loot.Encode(), 3);
        Check(pending.Items.Count == 2, "Duplicate result ignored");
        loot.Pending = true; loot.Revision = 1;
        inbox.Complete(42, token, loot.Encode(), 3);
        Check(pending.Pending && pending.Revision == 1, "Newer pending revision replaces snapshot");
        loot.Pending = false; loot.Revision = 2; loot.Add("C", "Sword", 1);
        inbox.Complete(42, token, loot.Encode(), 3);
        Check(!pending.Pending && pending.Items.Count == 3, "Completed revision replaces rather than double counts");
        loot.Revision = 1; inbox.Complete(42, token, loot.Encode(), 3);
        Check(pending.Revision == 2, "Out-of-order older revision ignored");
        token = Guid.NewGuid().ToString("N"); inbox.Announce(42, "boss", token, 4);
        Check(inbox.Take(42, "boss", 10) == null, "Stale credit not attached to later kill");
        Check(EventMessages.Boss("Boss", "", "", loot: "Loot:\nAntler") == "Boss\n\nLoot:\nAntler", "Legacy templates gain loot");
        Check(EventMessages.Boss("{item_count}: {loot}", "", "", loot: "Antler", itemCount: "1") == "1: Antler", "Loot template placeholders");
        Check(EventMessages.Boss("Boss{loot}{item_count}", "", "") == "Boss", "Disabled loot removes placeholders");
        Check(EventMessages.Boss("Boss", "", "", loot: new string('x', 2100)).Length == 2000, "Enriched message respects Discord limit");

        var harmony = new Harmony("valheimmoments.loot.tests");
        BossLootDetector.Install(harmony); BossKillDetector.Install(harmony);
        BossKill recorded = null; int observations = 0;
        BossKillDetector.OnKill = k => recorded = k;
        BossLootDetector.OnObserved = count => observations++;
        try
        {
            var router = new ZRoutedRpc();
            var boss = new Character { Boss = true, m_name = "boss" };
            var dropper = new CharacterDrop(boss);
            var prefab = new GameObject { name = "Antler", Item = new ItemDrop() };
            prefab.Item.m_itemData.m_shared.m_name = "$item_antler";
            dropper.Drops.Add(new KeyValuePair<GameObject, int>(prefab, 3));
            var game = new Game();
            boss.DeathAction = () => {
                game.RPC_RegisterKill(0, "boss", 1, 0, 2, false);
                Check(recorded.Loot != null && !recorded.Loot.Observed, "Kill captures before loot roll");
                dropper.GenerateDropList();
            };
            boss.OnDeath();
            Check(recorded.Loot.Observed && recorded.Loot.Items[0].Quantity == 3, "Delayed roll attached to exact local death");
            Check(dropper.Rolls == 1 && dropper.Drops[0].Value == 3, "Observer neither rerolls nor changes drops");
            dropper.GenerateDropList();
            Check(observations == 1, "Rolls outside death ignored");
            boss.DeathAction = () => {
                game.RegisterKill(99, "boss", 1, KillModifiers.None, 2, false);
                game.RPC_RegisterKill(42, "boss", 1, 0, 2, false);
                dropper.GenerateDropList();
            };
            boss.OnDeath();
            Check(recorded.Loot.Observed && recorded.Loot.Items[0].Quantity == 3, "Co-op result enriches credited event after death returns");
            boss.DeathAction = () => {
                game.RPC_RegisterKill(0, "boss", 1, 0, 2, false);
                new CharacterDrop(new Character { Boss = true, m_name = "boss" }).GenerateDropList();
            };
            boss.OnDeath();
            Check(!recorded.Loot.Observed, "Same-name other boss cannot supply loot");
            boss.DeathAction = () => { throw new InvalidOperationException("test"); };
            try { boss.OnDeath(); } catch (InvalidOperationException) { }
            Check(BossLootDetector.Take(0, "boss") == null, "Loot context cleared on exception");
        }
        finally { harmony.UnpatchSelf(); BossLootDetector.Clear(); BossKillDetector.OnKill = null; }
        Console.WriteLine("PASS: " + checks + " loot assertions with real Harmony and behavioral game stand-ins.");
    }
}
