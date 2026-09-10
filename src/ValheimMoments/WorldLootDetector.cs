using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMoments
{
    // Only positively identified generation paths mint provenance. Inventory additions
    // alone never qualify. Custom data survives the game's existing save/ZDO/clone paths.
    internal static class WorldLootDetector
    {
        internal const string OriginKey = "local.valheimmoments.origin.v1";
        private static readonly FieldInfo InventoryField = AccessTools.Field(typeof(Container), "m_inventory");
        private static readonly MethodInfo SaveItem = AccessTools.Method(typeof(ItemDrop), "Save", Type.EmptyTypes);
        private static ConditionalWeakTable<Inventory, Container> containers = new ConditionalWeakTable<Inventory, Container>();
        private sealed class Scope
        {
            internal Inventory Generating, From, Loading;
            internal ItemDrop Pickup;
            internal bool World;
        }
        private sealed class Addition
        {
            internal ItemDrop.ItemData Item;
            internal string Token;
            internal LootItem Snapshot;
            internal long Before;
            internal bool Eligible;
        }
        [ThreadStatic] private static Scope scope;
        private static bool installed;
        internal static Func<string, bool> Enabled;
        internal static Func<ItemDrop.ItemData, LootItem> Read;
        internal static Action<string, LootItem> OnAcquired;
        internal static Action OnError;

        internal static void Install(Harmony harmony)
        {
            if (InventoryField == null || SaveItem == null) throw new MissingMemberException("Container inventory / ItemDrop.Save");
            Patch(harmony, AccessTools.Method(typeof(Container), "AddDefaultItems"), nameof(BeforeChest), nameof(EndScope));
            Patch(harmony, AccessTools.Method(typeof(Container), "Load"), nameof(RegisterContainer));
            Patch(harmony, AccessTools.Method(typeof(Container), "OnDestroyed"), nameof(BeforeContainerDestroyed), nameof(EndScope));
            Patch(harmony, AccessTools.Method(typeof(Character), "OnDeath", Type.EmptyTypes), nameof(Suppress), nameof(EndScope));
            Patch(harmony, AccessTools.Method(typeof(Ragdoll), "SpawnLoot"), nameof(Suppress), nameof(EndScope));
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(Inventory)))
            {
                if (method.Name == "Load") Patch(harmony, method, nameof(BeforeLoad), nameof(EndScope));
                if (method.Name == "MoveAll" || method.Name == "MoveItemToThis") Patch(harmony, method, nameof(BeforeMove), nameof(EndScope));
                if (method.Name == "AddItem" && Array.Exists(method.GetParameters(), p => p.ParameterType == typeof(ItemDrop.ItemData)))
                    Patch(harmony, method, nameof(BeforeAdd), nameof(AfterAdd));
            }
            Patch(harmony, AccessTools.Method(typeof(Humanoid), "Pickup", new[] { typeof(GameObject), typeof(bool), typeof(bool) }), nameof(BeforePickup), nameof(EndScope));
            Patch(harmony, AccessTools.Method(typeof(ItemDrop), "OnPlayerDrop"), nameof(PlayerDrop));
            Patch(harmony, AccessTools.Method(typeof(ItemDrop), "AutoStackItems"), nameof(BeforeStack), nameof(AfterStack));
            foreach (var type in new[] { typeof(Pickable), typeof(PickableItem) })
                Patch(harmony, AccessTools.Method(type, "RPC_Pick"), nameof(BeforeWorld), nameof(EndScope));
            Patch(harmony, AccessTools.Method(typeof(DropOnDestroyed), "OnDestroyed"), nameof(BeforeWorld), nameof(EndScope));
            harmony.Patch(AccessTools.Method(typeof(ItemDrop), "OnCreateNew", new[] { typeof(ItemDrop), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(WorldLootDetector), nameof(Created)) { priority = Priority.Last });
            installed = true;
        }
        internal static void InstallEpic(Assembly assembly, Harmony harmony)
        {
            if (assembly == null || !installed) return;
            // 0.14.2 defers chest rolls, independently of AddDefaultItems.
            var method = AccessTools.Method(assembly.GetType("EpicLoot.PendingChestLoot", true), "RollInternal");
            if (method == null || method.GetParameters().Length != 3 || method.GetParameters()[0].ParameterType != typeof(Container))
                throw new MissingMethodException("EpicLoot.PendingChestLoot.RollInternal");
            Patch(harmony, method, nameof(BeforeEpicChest), nameof(EndScope));
        }
        private static void Patch(Harmony harmony, MethodInfo method, string before, string final = null)
        {
            if (method == null) throw new MissingMethodException("World loot observer target missing");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(WorldLootDetector), before) { priority = Priority.First },
                finalizer: final == null ? null : new HarmonyMethod(typeof(WorldLootDetector), final) { priority = Priority.Last });
        }
        private static void Error() { try { OnError?.Invoke(); } catch { } }
        private static Inventory InventoryOf(Container c) { return c == null ? null : (Inventory)InventoryField.GetValue(c); }
        private static bool Natural(Component component, bool chest)
        {
            if (component == null || component.GetComponentInParent<TombStone>() != null || component.GetComponentInParent<Character>() != null) return false;
            var piece = component.GetComponentInParent<Piece>();
            if (piece != null && piece.IsPlacedByPlayer()) return false;
            if (chest) return piece != null;
            return component.GetComponentInParent<Container>() == null && component.GetComponentInParent<Plant>() == null;
        }
        private static bool Owner(Component c)
        { var view = c == null ? null : c.GetComponent<ZNetView>(); return view != null && view.IsValid() && view.IsOwner(); }
        private static bool NaturalInventory(Inventory inventory)
        { Container c; return inventory != null && containers.TryGetValue(inventory, out c) && Natural(c, true); }
        private static void RegisterContainer(Container __instance)
        {
            try { var inv = InventoryOf(__instance); if (inv != null) { containers.Remove(inv); containers.Add(inv, __instance); } }
            catch { Error(); }
        }
        private static void BeforeChest(Container __instance, out Scope __state) { BeginChest(__instance, out __state); }
        private static void BeforeEpicChest(Container __0, out Scope __state) { BeginChest(__0, out __state); }
        private static void BeginChest(Container c, out Scope previous)
        {
            previous = scope; scope = new Scope();
            try { RegisterContainer(c); if (Natural(c, true) && Owner(c)) scope.Generating = InventoryOf(c); }
            catch { Error(); }
        }
        private static void BeforeWorld(Component __instance, out Scope __state)
        {
            __state = scope; scope = new Scope();
            try { scope.World = Natural(__instance, false) && Owner(__instance); } catch { Error(); }
        }
        private static void BeforeLoad(Inventory __instance, out Scope __state)
        { __state = scope; scope = new Scope { Loading = __instance }; }
        private static void Suppress(out Scope __state) { __state = scope; scope = new Scope(); }
        private static void BeforeContainerDestroyed(Container __instance, out Scope __state)
        {
            Suppress(out __state);
            try
            {
                if (!Natural(__instance, true))
                    foreach (var item in InventoryOf(__instance).GetAllItems()) Forget(item);
            }
            catch { Error(); }
        }
        private static void BeforeMove(Inventory __0, out Scope __state)
        { __state = scope; scope = new Scope { From = __0 }; }
        private static void BeforePickup(GameObject __0, out Scope __state)
        {
            __state = scope; scope = new Scope();
            try { scope.Pickup = __0 == null ? null : __0.GetComponent<ItemDrop>(); } catch { Error(); }
        }
        private static void EndScope(Scope __state) { scope = __state; }
        private static string Token(ItemDrop.ItemData item)
        {
            string value; Guid id;
            return item?.m_customData != null && item.m_customData.TryGetValue(OriginKey, out value) && value != null && value.Length == 34 &&
                (value[0] == 'c' || value[0] == 'w') && value[1] == ':' && Guid.TryParseExact(value.Substring(2), "N", out id) ? value : null;
        }
        private static void Forget(ItemDrop.ItemData item) { item?.m_customData?.Remove(OriginKey); }
        private static void Mark(ItemDrop.ItemData item, string kind)
        {
            if (item == null || item.m_pickedUp || item.m_crafterID != 0) return;
            if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
            if (Token(item) == null) item.m_customData[OriginKey] = kind + ":" + Guid.NewGuid().ToString("N");
        }
        private static void Created(ItemDrop __0)
        {
            try { if (scope?.World == true && __0 != null && Owner(__0)) { Mark(__0.m_itemData, "w"); SaveItem.Invoke(__0, null); } }
            catch { Error(); }
        }
        private static void PlayerDrop(ItemDrop __instance)
        { try { Forget(__instance.m_itemData); SaveItem.Invoke(__instance, null); } catch { Error(); } }
        private static void BeforeStack(ItemDrop __instance, out int __state) { __state = __instance.m_itemData.m_stack; }
        private static void AfterStack(ItemDrop __instance, int __state, Exception __exception)
        {
            try
            {
                // A merged ground stack no longer has a provable single origin.
                if ((__exception != null || __instance.m_itemData.m_stack != __state) && Token(__instance.m_itemData) != null)
                { Forget(__instance.m_itemData); SaveItem.Invoke(__instance, null); }
            }
            catch { Error(); }
        }
        private static long Count(Inventory inventory, ItemDrop.ItemData item)
        {
            long count = 0;
            foreach (var other in inventory.GetAllItems())
                if (other.m_shared.m_name == item.m_shared.m_name && other.m_quality == item.m_quality && other.m_worldLevel == item.m_worldLevel)
                    count += Math.Max(0, other.m_stack);
            return count;
        }
        private static void BeforeAdd(Inventory __instance, object[] __args, out Addition __state)
        {
            __state = null;
            try
            {
                ItemDrop.ItemData item = null;
                foreach (var arg in __args) if (arg is ItemDrop.ItemData) { item = (ItemDrop.ItemData)arg; break; }
                if (item?.m_shared == null) return;
                if (ReferenceEquals(scope?.Generating, __instance)) { Mark(item, "c"); return; }
                if (ReferenceEquals(scope?.Loading, __instance))
                { if (!NaturalInventory(__instance)) Forget(item); return; }
                // Deposits can merge into an existing natural stack. Remove that stack's
                // provenance conservatively, without changing vanilla stacking behavior.
                if (NaturalInventory(__instance))
                    foreach (var existing in __instance.GetAllItems())
                        if (existing.m_shared.m_name == item.m_shared.m_name && existing.m_quality == item.m_quality) Forget(existing);
                string token = Token(item);
                if (token == null) return;
                bool source = (scope?.Pickup != null && ReferenceEquals(scope.Pickup.m_itemData, item)) ||
                    (NaturalInventory(scope?.From) && scope.From.ContainsItem(item));
                bool local = Player.m_localPlayer != null && ReferenceEquals(Player.m_localPlayer.GetInventory(), __instance);
                __state = new Addition { Item = item, Token = token, Before = Count(__instance, item),
                    Eligible = source && local && !item.m_pickedUp && item.m_crafterID == 0 };
                // Consume before cloning or saving, including transfers into other storage.
                Forget(item);
                if (__state.Eligible && Enabled?.Invoke(token.Substring(0, 1)) == true) __state.Snapshot = Read?.Invoke(item);
            }
            catch { if (__state != null) { Forget(__state.Item); __state.Eligible = false; } Error(); }
        }
        private static void AfterAdd(Inventory __instance, Addition __state, Exception __exception)
        {
            if (__state == null) return;
            try
            {
                long added = Count(__instance, __state.Item) - __state.Before;
                if (added <= 0 && __exception == null)
                { __state.Item.m_customData[OriginKey] = __state.Token; return; }
                // Vanilla can partly add a stack and return false before saving the
                // ground object. Persist consumption even in that case.
                if (scope?.Pickup != null && ReferenceEquals(scope.Pickup.m_itemData, __state.Item) && Owner(scope.Pickup))
                    SaveItem.Invoke(scope.Pickup, null);
                if (__exception != null || !__state.Eligible || __state.Snapshot == null || added <= 0) return;
                __state.Snapshot.Quantity = (int)Math.Min(int.MaxValue, added);
                OnAcquired?.Invoke(__state.Token.Substring(0, 1), __state.Snapshot);
            }
            catch { Error(); }
        }
        internal static void Clear()
        {
            installed = false; scope = null; containers = new ConditionalWeakTable<Inventory, Container>();
            Enabled = null; Read = null; OnAcquired = null; OnError = null;
        }
    }
}
