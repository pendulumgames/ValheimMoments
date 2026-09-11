using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using ValheimMoments;

namespace UnityEngine
{
    public partial class GameObject
    {
        public readonly Dictionary<Type, object> Components = new Dictionary<Type, object>();
        public T Attach<T>(T component) where T : Component { Components[typeof(T)] = component; component.gameObject = this; return component; }
    }
    public partial class Component
    {
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>() where T : class { return GetComponent<T>(); }
    }
}
public class ZNetView : Component { public bool Owner = true, Valid = true; public bool IsOwner() { return Owner; } public bool IsValid() { return Valid; } public ZDO Data = new ZDO(); public ZDO GetZDO() { return Data; } }
public class ZDO { public readonly HashSet<string> Flags = new HashSet<string>(); public bool GetBool(string key, bool fallback) { return Flags.Contains(key) || fallback; } }
public static class ZDOVars { public static int s_attackers = 1234; }
public class Piece : Component { public bool Built; public bool IsPlacedByPlayer() { return Built; } }
public class TombStone : Component { }
public class Plant : Component { }
public partial class ItemDrop
{
    public int Saved;
    public Action StackAction;
    public Dictionary<string, string> SavedData;
    public void Save() { Saved++; SavedData = new Dictionary<string, string>(m_itemData.m_customData); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnPlayerDrop() { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void AutoStackItems() { StackAction?.Invoke(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void OnCreateNew(ItemDrop item, bool cheated) { }
    public partial class ItemData
    {
        public Dictionary<string, string> m_customData = new Dictionary<string, string>();
        public int m_quality = 1, m_worldLevel;
        public long m_crafterID;
        public bool m_pickedUp;
        public ItemData Clone() { var item = (ItemData)MemberwiseClone(); item.m_customData = new Dictionary<string, string>(m_customData); return item; }
    }
}
public class Humanoid : Character
{
    public readonly Inventory Inventory = new Inventory();
    public Inventory GetInventory() { return Inventory; }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool Pickup(GameObject obj, bool auto, bool delay) { return Inventory.AddItem(obj.Item.m_itemData); }
}
public class Inventory
{
    public readonly List<ItemDrop.ItemData> Items = new List<ItemDrop.ItemData>();
    public int Capacity = int.MaxValue;
    public bool Merge, Fail, Throw;
    public List<ItemDrop.ItemData> GetAllItems() { return Items; }
    public bool ContainsItem(ItemDrop.ItemData item) { return Items.Contains(item); }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool AddItem(ItemDrop.ItemData item)
    {
        if (Throw) throw new InvalidOperationException("simulated inventory error");
        if (Fail || Capacity == 0) return false;
        int amount = Math.Min(Capacity, item.m_stack);
        var existing = Merge ? Items.Find(i => i.m_shared.m_name == item.m_shared.m_name) : null;
        if (existing != null) existing.m_stack += amount;
        else { var clone = item.Clone(); clone.m_stack = amount; Items.Add(clone); }
        item.m_stack -= amount;
        return item.m_stack == 0;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool AddItem(ItemDrop.ItemData item, int amount, int x, int y, bool skip)
    { int old = Capacity; Capacity = Math.Min(Capacity, amount); try { return AddItem(item); } finally { Capacity = old; } }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool AddItem(int hash, ItemDrop.ItemData item, bool skip) { return AddItem(item); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void MoveItemToThis(Inventory from, ItemDrop.ItemData item)
    { if (AddItem(item)) from.Items.Remove(item); }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool MoveItemToThis(Inventory from, ItemDrop.ItemData item, int amount, int x, int y)
    { bool ok = AddItem(item, amount, x, y, false); if (item.m_stack == 0) from.Items.Remove(item); return ok; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void MoveAll(Inventory from)
    { foreach (var item in new List<ItemDrop.ItemData>(from.Items)) MoveItemToThis(from, item); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void Load(List<ItemDrop.ItemData> saved)
    { Items.Clear(); foreach (var item in saved) AddItem(1, item.Clone(), true); }
}
public class Container : Component
{
    private Inventory m_inventory = new Inventory();
    public Inventory Inventory { get { return m_inventory; } }
    public readonly List<ItemDrop.ItemData> Generated = new List<ItemDrop.ItemData>();
    public List<ItemDrop.ItemData> Saved = new List<ItemDrop.ItemData>();
    public Action During;
    [MethodImpl(MethodImplOptions.NoInlining)] public void AddDefaultItems() { foreach (var item in Generated) m_inventory.AddItem(item); During?.Invoke(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool Load() { m_inventory.Load(Saved); return true; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnDestroyed() { During?.Invoke(); }
}
public class Pickable : Component
{
    public Action Drop;
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_Pick(long sender, int amount) { Drop?.Invoke(); }
}
public class PickableItem : Component
{
    public Action Drop;
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_Pick(long sender) { Drop?.Invoke(); }
}
public class DropOnDestroyed : Component
{
    public Action Drop;
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnDestroyed() { Drop?.Invoke(); }
}
namespace EpicLoot
{
    public static class PendingChestLoot
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static void RollInternal(Container c, string name, List<LootTable> tables)
        { c.Inventory.AddItem(WorldLootTests.Item("Delayed Legendary")); }
    }
}
internal static class WorldLootTests
{
    private static int checks;
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    internal static ItemDrop.ItemData Item(string name = "Sword", int count = 1)
    { var item = new ItemDrop.ItemData { m_stack = count }; item.m_shared.m_name = name; return item; }
    private static bool Tagged(ItemDrop.ItemData item) { return item.m_customData.ContainsKey(WorldLootDetector.OriginKey); }
    private static Container Chest(bool player = false, bool grave = false)
    {
        var c = new Container(); c.gameObject.Attach(c); c.gameObject.Attach(new Piece { Built = player });
        c.gameObject.Attach(new ZNetView()); if (grave) c.gameObject.Attach(new TombStone()); return c;
    }
    private static ItemDrop Ground(ItemDrop.ItemData item)
    {
        var drop = new ItemDrop { m_itemData = item }; drop.gameObject.Item = drop;
        drop.gameObject.Attach(new ZNetView()); return drop;
    }
    private static ItemDrop.ItemData Generated(Container c, string name = "Sword", int count = 1)
    { c.Generated.Clear(); c.Generated.Add(Item(name, count)); c.AddDefaultItems(); return c.Inventory.Items[c.Inventory.Items.Count - 1]; }
    internal static void Run()
    {
        var harmony = new Harmony("valheimmoments.world.tests");
        var events = new List<LootItem>(); var kinds = new List<string>(); int errors = 0;
        WorldLootDetector.Enabled = kind => true;
        WorldLootDetector.Read = item => new LootItem { Id = item.m_shared.m_name, Name = item.m_shared.m_name, Rank = 3, Quantity = item.m_stack };
        WorldLootDetector.OnAcquired = (kind, item) => { kinds.Add(kind); events.Add(item); };
        WorldLootDetector.OnError = () => errors++;
        Player.m_localPlayer = new Player { Name = "Ragnar" };
        var inv = Player.m_localPlayer.Inventory;
        try
        {
            WorldLootDetector.Install(harmony); WorldLootDetector.InstallEpic(Assembly.GetExecutingAssembly(), harmony);
            var chest = Chest(); var item = Generated(chest);
            Check(Tagged(item) && events.Count == 0, "Generation tags without recording or rerolling");
            inv.MoveItemToThis(chest.Inventory, item);
            Check(events.Count == 1 && kinds[0] == "c" && events[0].Quantity == 1, "Natural chest transfer records collector");
            Check(!Tagged(item) && !Tagged(inv.Items[0]), "Both source and cloned destination consumed before save");
            var storage = Chest(true); storage.Inventory.MoveItemToThis(inv, inv.Items[0]);
            inv.MoveAll(storage.Inventory); Check(events.Count == 1, "Player storage does not retrigger");
            Check(!Tagged(Generated(storage)), "Player built generation never tagged");
            Check(!Tagged(Generated(Chest(false, true))), "Gravestone generation never tagged");
            var remote = Chest(); remote.gameObject.GetComponent<ZNetView>().Owner = false;
            Check(!Tagged(Generated(remote)), "Non-owner cannot mint chest provenance");
            var older = Item("Old chest contents"); chest.Inventory.Items.Add(older);
            inv.MoveItemToThis(chest.Inventory, older); Check(events.Count == 1, "Untracked older chest contents excluded");
            item = Generated(chest, "Full inventory"); inv.Fail = true;
            inv.MoveItemToThis(chest.Inventory, item); Check(Tagged(item) && events.Count == 1, "Failed transfer retains opportunity");
            inv.Fail = false; inv.MoveItemToThis(chest.Inventory, item); Check(events.Count == 2, "Retry after full inventory succeeds");
            item = Generated(chest, "Partial", 5); inv.Capacity = 2;
            inv.MoveItemToThis(chest.Inventory, item, 5, 0, 0);
            Check(events.Count == 3 && events[2].Quantity == 2 && !Tagged(item) && item.m_stack == 3, "Partial addition reports actual count once despite false result");
            inv.Capacity = int.MaxValue; inv.MoveItemToThis(chest.Inventory, item);
            Check(events.Count == 3, "Remainder of a consumed stack does not retrigger");
            var deposit = Chest(); item = Generated(deposit, "Mixed", 3); deposit.Inventory.Merge = true;
            deposit.Inventory.AddItem(Item("Mixed", 2)); Check(!Tagged(item), "Player deposit invalidates mixed natural stack");
            inv.MoveAll(deposit.Inventory); Check(events.Count == 3, "Mixed chest stack excluded");
            item = Generated(chest, "Moved chest"); var another = Chest(); another.AddDefaultItems();
            another.Inventory.MoveItemToThis(chest.Inventory, item); inv.MoveAll(another.Inventory);
            Check(events.Count == 3, "Moving loot into another natural chest is a deposit, not fresh treasure");
            item = Generated(chest, "Saved loot"); var saved = item.Clone(); var reloaded = Chest();
            reloaded.Saved.Add(saved); reloaded.Load();
            Check(Tagged(reloaded.Inventory.Items[0]) && events.Count == 3, "Natural chest load preserves origin without capture");
            inv.MoveAll(reloaded.Inventory); Check(events.Count == 4, "Reloaded proven treasure still qualifies");
            inv.Load(new List<ItemDrop.ItemData> { saved }); Check(events.Count == 4 && !Tagged(inv.Items[0]), "Player inventory load is never acquisition");
            var grave = Chest(false, true); grave.Saved.Add(saved); grave.Load();
            Check(!Tagged(grave.Inventory.Items[0]), "Tombstone load strips even inherited provenance");
            inv.MoveAll(grave.Inventory); Check(events.Count == 4, "Gravestone recovery excluded");
            var ground = Ground(Item("World treasure")); var pickable = new Pickable();
            pickable.gameObject.Attach(new ZNetView()); pickable.Drop = () => ItemDrop.OnCreateNew(ground, false);
            pickable.RPC_Pick(1, 1);
            Check(Tagged(ground.m_itemData) && ground.SavedData.ContainsKey(WorldLootDetector.OriginKey), "Natural pickable origin persisted to ground item");
            Player.m_localPlayer.Pickup(ground.gameObject, true, false);
            Check(events.Count == 5 && kinds[4] == "w", "Auto pickup of world treasure qualifies");
            ground = Ground(Item("Player drop")); pickable.Drop = () => ItemDrop.OnCreateNew(ground, false); pickable.RPC_Pick(1, 1);
            ground.OnPlayerDrop(); Check(!Tagged(ground.m_itemData), "Player drop explicitly invalidates origin");
            Player.m_localPlayer.Pickup(ground.gameObject, false, false); Check(events.Count == 5, "Player drop pickup excluded");
            ground = Ground(Item("Merged ground", 2)); pickable.RPC_Pick(1, 1); ground.StackAction = () => ground.m_itemData.m_stack++;
            ground.AutoStackItems(); Check(!Tagged(ground.m_itemData), "Ground auto-stack invalidates ambiguous origin");
            ground = Ground(Item("Plant")); pickable.gameObject.Attach(new Plant()); pickable.RPC_Pick(1, 1);
            Check(!Tagged(ground.m_itemData), "Cultivated plants excluded");
            var breakable = new DropOnDestroyed(); breakable.gameObject.Attach(new ZNetView()); ground = Ground(Item("Barrel loot"));
            breakable.Drop = () => ItemDrop.OnCreateNew(ground, false); breakable.OnDestroyed();
            Check(Tagged(ground.m_itemData), "Natural breakable generation supported");
            breakable.gameObject.Attach(new Piece { Built = true }); ground = Ground(Item("Refund")); breakable.OnDestroyed();
            Check(!Tagged(ground.m_itemData), "Player building refunds excluded");
            var partialGround = Ground(Item("Ground partial", 5)); var source = new PickableItem(); source.gameObject.Attach(new ZNetView());
            source.Drop = () => ItemDrop.OnCreateNew(partialGround, false); source.RPC_Pick(1);
            inv.Capacity = 2; Player.m_localPlayer.Pickup(partialGround.gameObject, true, false); inv.Capacity = int.MaxValue;
            Check(events.Count == 6 && events[5].Quantity == 2 && !partialGround.SavedData.ContainsKey(WorldLootDetector.OriginKey), "Partial failed pickup persists consumption before a reload");
            var nested = new PickableItem(); nested.gameObject.Attach(new ZNetView());
            var mobDrop = Ground(Item("Mob loot"));
            nested.Drop = () => new Character { DeathAction = () => ItemDrop.OnCreateNew(mobDrop, false) }.OnDeath();
            nested.RPC_Pick(1); Check(!Tagged(mobDrop.m_itemData), "Nested mob death does not become world loot");
            var reclaimed = Chest(); var reclaimedItem = Generated(reclaimed, "Old generated storage");
            reclaimed.gameObject.GetComponent<Piece>().Built = true;
            reclaimed.OnDestroyed(); Check(!Tagged(reclaimedItem), "Destroying a player-owned container strips inherited tags");
            var delayed = Chest(); EpicLoot.PendingChestLoot.RollInternal(delayed, "Chest", null);
            Check(Tagged(delayed.Inventory.Items[0]), "Epic Loot deferred chest roll independently tagged");
            inv.MoveAll(delayed.Inventory); Check(events.Count == 7, "Deferred Epic Loot chest acquisition captured");
            WorldLootDetector.Enabled = kind => false; item = Generated(chest, "Disabled"); inv.MoveItemToThis(chest.Inventory, item);
            Check(!Tagged(item) && events.Count == 7, "Disabled capture still consumes provenance");
            WorldLootDetector.Enabled = kind => true; item = Generated(chest, "Exception"); inv.Throw = true;
            try { inv.MoveItemToThis(chest.Inventory, item); } catch (InvalidOperationException) { }
            inv.Throw = false; Check(!Tagged(item) && events.Count == 7, "Exception is not hidden and cannot create highlight");
            chest.During = () => { throw new InvalidOperationException("generation failure"); };
            try { chest.AddDefaultItems(); } catch (InvalidOperationException) { }
            var outside = Item("Unscoped"); chest.Inventory.AddItem(outside);
            Check(!Tagged(chest.Inventory.Items[chest.Inventory.Items.Count - 1]), "Generation scope restored after exception");
            Check(errors == 0, "Observers completed without internal errors");
            var batches = new AcquisitionHighlights(); var accepted = new List<BossKill>();
            batches.Add("c", new LootItem { Id = "a", Name = "Magic", Rank = 0, Quantity = 1 }, "Ragnar", 0);
            batches.Add("c", new LootItem { Id = "b", Name = "Legendary", Rank = 3, Quantity = 1 }, "Ragnar", 0.1);
            batches.Poll(0.2, "Legendary", kind => true, accepted.Add); Check(accepted.Count == 0, "Take-all batch waits briefly");
            batches.Poll(0.3, "Legendary", kind => true, accepted.Add);
            Check(accepted.Count == 1 && accepted[0].Acquired && accepted[0].Loot.Items.Count == 2, "Take-all aggregates before applying highest rarity");
            batches.Poll(1, "Legendary", kind => true, accepted.Add); Check(accepted.Count == 1, "Batch consumed once");
            batches.Add("w", new LootItem { Id = "a", Name = "Epic", Rank = 2, Quantity = 1 }, "Ragnar", 1);
            batches.Poll(2, "Legendary", kind => true, accepted.Add); Check(accepted.Count == 1, "Minimum rarity still filters pickups");
            batches.Add("c", new LootItem { Id = "a", Name = "Legendary", Rank = 3, Quantity = 1 }, "Ragnar", 2);
            batches.Clear(); batches.Poll(3, "None", kind => true, accepted.Add); Check(accepted.Count == 1, "Session reset cancels batches");
            string message = EventMessages.FoundLoot(null, "a treasure chest", "Ragnar", "## Generated loot:\n* Sword", "1");
            Check(message == "Great loot from a treasure chest!\n**Collected by:** Ragnar\n## Generated loot:\n* Sword", "Pickup formatting has collector without kill credit or blank lines");
        }
        finally { harmony.UnpatchSelf(); WorldLootDetector.Clear(); Player.m_localPlayer = null; }
        Console.WriteLine("PASS: " + checks + " natural loot provenance and acquisition assertions with behavioral game stand-ins.");
    }
}
