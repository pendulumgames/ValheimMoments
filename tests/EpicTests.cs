using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using ValheimMoments;

public class Ragdoll
{
    public Action SpawnAction;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Setup(CharacterDrop characterDrop) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void SpawnLoot() { SpawnAction?.Invoke(); }
}
namespace EpicLoot
{
    public enum ItemRarity { Magic, Rare, Epic, Legendary, Mythic, Ancient }
    public class MagicItemEffect { }
    public class MagicItem
    {
        public List<MagicItemEffect> Effects = new List<MagicItemEffect> { new MagicItemEffect() };
        public int SocketCount = 2;
        public ItemRarity Rarity = ItemRarity.Ancient;
        public static string GetEffectText(MagicItemEffect effect, ItemRarity rarity, bool ranges, string prefix) { return "<color=blue>+25% Slash Damage</color>"; }
    }
    public static class API
    {
        public static bool Hidden;
        public static string GetItemDisplayName(ItemDrop.ItemData item) { return "<color=red>Storm Axe</color>"; }
        public static bool TryGetRarity(ItemDrop.ItemData item, out int rank) { rank = 5; return true; }
        public static string GetRarityDisplayNameByIndex(int rank) { return "Ancient"; }
        public static string GetRarityColorByIndex(int rank) { return "#e53935"; }
        public static bool IsUnidentified(ItemDrop.ItemData item) { return Hidden; }
    }
    public static class ItemDataExtensions { public static MagicItem GetMagicItem(ItemDrop.ItemData item) { return new MagicItem(); } }
    public class LootTable { }
    public static class LootRoller
    {
        public static List<GameObject> Results = new List<GameObject>();
        [MethodImpl(MethodImplOptions.NoInlining)] public static List<GameObject> RollLootTableAndSpawnObjects(LootTable table) { return Results; }
        [MethodImpl(MethodImplOptions.NoInlining)] public static List<GameObject> RollLootTableAndSpawnObjects(List<LootTable> tables) { return Results; }
    }
}
internal static class EpicTests
{
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    internal static void Run()
    {
        var harmony = new Harmony("valheimmoments.epic.tests");
        Check(!EpicLootAdapter.Install(null, harmony), "Optional dependency absent");
        BossLootDetector.Install(harmony); BossKillDetector.Install(harmony);
        Check(EpicLootAdapter.Install(Assembly.GetExecutingAssembly(), harmony), "Verified adapter contracts installed");
        BossKill recorded = null; BossKillDetector.OnKill = k => recorded = k;
        try
        {
            var router = new ZRoutedRpc();
            var boss = new Character { Boss = true, m_name = "boss" };
            var dropper = new CharacterDrop(boss); var body = new Ragdoll(); var game = new Game();
            var item = new GameObject { name = "Axe", Item = new ItemDrop() };
            EpicLoot.LootRoller.Results = new List<GameObject> { item };
            boss.DeathAction = () => {
                game.RegisterKill(99, "boss", 1, KillModifiers.None, 2, false);
                game.RPC_RegisterKill(42, "boss", 1, 0, 2, false);
                body.Setup(dropper); dropper.GenerateDropList();
            };
            boss.OnDeath();
            Check(recorded.Loot.Pending && recorded.Loot.Items.Count == 0, "Co-op death snapshot awaits exact ragdoll");
            EpicLoot.LootRoller.RollLootTableAndSpawnObjects(new EpicLoot.LootTable());
            Check(recorded.Loot.Items.Count == 0, "Unrelated generated items ignored");
            body.SpawnAction = () => {
                EpicLoot.LootRoller.RollLootTableAndSpawnObjects(new EpicLoot.LootTable());
                EpicLoot.LootRoller.RollLootTableAndSpawnObjects(new List<EpicLoot.LootTable>());
            };
            body.SpawnLoot();
            Check(!recorded.Loot.Pending && recorded.Loot.Items.Count == 1, "Late completed items update co-op snapshot once");
            var magic = recorded.Loot.Items[0];
            Check(magic.Name == "Storm Axe" && magic.Rank == 5 && magic.Rarity == "Ancient", "Real API name and enum rank consumed");
            Check(magic.Modifiers == "+25% Slash Damage" && magic.Sockets == 2, "API effect text stripped of Unity markup");
            string message = recorded.Loot.Display(5, true, s => s);
            Check(message.Contains("Ancient Storm Axe") && message.Contains("Sockets: 2"), "Magic details displayed");
            Check(!recorded.Loot.Display(5, true, s => s, false, false, false).Contains("Ancient"), "Detail switches respected");
            recorded.Loot.Add("Vanilla", "Antler", 3);
            Check(recorded.Loot.Display(1, true, s => s).Contains("Storm Axe") && !recorded.Loot.Display(1, true, s => s).Contains("Antler"), "Highest rarity before alphabetic vanilla item");
            var roundTrip = BossLoot.Decode(recorded.Loot.Encode());
            Check(roundTrip.Items[0].Modifiers == magic.Modifiers && roundTrip.Items[0].Rank == 5, "Detailed wire round trip");
            EpicLoot.API.Hidden = true;
            var hidden = EpicLootAdapter.Read(item.Item.m_itemData, "Axe");
            Check(hidden.Unidentified && hidden.Modifiers == "" && hidden.Sockets == 0, "Hidden details never extracted");
            var hiddenLoot = new BossLoot(); hiddenLoot.AddEpic(hidden);
            Check(hiddenLoot.Display(5, true, s => s).Contains("unidentified"), "Unidentified marker");
            var unrelated = new Ragdoll { SpawnAction = body.SpawnAction }; unrelated.SpawnLoot();
            Check(recorded.Loot.Items.Count == 2, "Other ragdoll cannot supply items");
            Check(EpicLootAdapter.Plain(new string('x',255) + "\uD83D\uDE00").Length == 255, "Text limit preserves surrogate pairs");
            EpicLoot.API.Hidden = false;
            BossKillDetector.ObserveOrdinary = () => true;
            BossKill ordinary = null;
            BossKillDetector.OnLootKill = k => ordinary = k;
            var creature = new Character { m_name = "troll" };
            var ordinaryDrops = new CharacterDrop(creature);
            var ordinaryBody = new Ragdoll { SpawnAction = body.SpawnAction };
            creature.DeathAction = () => {
                game.RegisterKill(99, "troll", 0, KillModifiers.None, 1, false);
                game.RPC_RegisterKill(42, "troll", 0, 0, 1, false);
                ordinaryBody.Setup(ordinaryDrops); ordinaryDrops.GenerateDropList();
            };
            var priorBoss = recorded;
            creature.OnDeath();
            Check(ordinary != null && ordinary.BossNumber == 0 && ordinary.Loot.Pending, "Ordinary co-op credit joins exact delayed loot");
            ordinaryBody.SpawnLoot();
            Check(ordinary.Loot.Items.Count == 1 && ordinary.Loot.Items[0].Rank == 5 && !ordinary.Loot.Pending, "Ordinary delayed Epic item observed once");
            Check(ReferenceEquals(priorBoss, recorded), "Ordinary kill does not emit boss event");
            var priorOrdinary = ordinary;
            BossKillDetector.ObserveOrdinary = () => false;
            creature.OnDeath(); ordinaryBody.SpawnLoot();
            Check(ReferenceEquals(priorOrdinary, ordinary), "Disabled ordinary observer emits no loot event");
        }
        finally { EpicLoot.API.Hidden = false; harmony.UnpatchSelf(); BossLootDetector.Clear(); BossKillDetector.OnKill = null; BossKillDetector.OnLootKill = null; BossKillDetector.ObserveOrdinary = null; }
        Console.WriteLine("PASS: " + checks + " Epic Loot adapter and delayed-ragdoll assertions with behavioral API stand-ins.");
    }
}
