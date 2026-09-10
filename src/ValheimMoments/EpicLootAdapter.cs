using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace ValheimMoments
{
    // No Epic Loot type appears in a compile-time reference or plugin signature.
    internal static class EpicLootAdapter
    {
        private static MethodInfo display, rarity, rarityName, rarityColor, unidentified, getMagic, effectText;
        private static FieldInfo effects, sockets, magicRarity;
        private static readonly HashSet<int> ranks = new HashSet<int>();
        private static readonly Regex tags = new Regex("<[^>]*>", RegexOptions.Compiled);
        internal static bool Install(Assembly assembly, Harmony harmony)
        {
            if (assembly == null) return false;
            Type api = assembly.GetType("EpicLoot.API", true), magic = assembly.GetType("EpicLoot.MagicItem", true);
            Type rank = assembly.GetType("EpicLoot.ItemRarity", true), effect = assembly.GetType("EpicLoot.MagicItemEffect", true);
            display = Required(api, "GetItemDisplayName", typeof(ItemDrop.ItemData));
            rarity = Required(api, "TryGetRarity", typeof(ItemDrop.ItemData), typeof(int).MakeByRefType());
            rarityName = Required(api, "GetRarityDisplayNameByIndex", typeof(int));
            rarityColor = Required(api, "GetRarityColorByIndex", typeof(int));
            unidentified = Required(api, "IsUnidentified", typeof(ItemDrop.ItemData));
            getMagic = Required(assembly.GetType("EpicLoot.ItemDataExtensions", true), "GetMagicItem", typeof(ItemDrop.ItemData));
            effectText = Required(magic, "GetEffectText", effect, rank, typeof(bool), typeof(string));
            effects = magic.GetField("Effects"); sockets = magic.GetField("SocketCount"); magicRarity = magic.GetField("Rarity");
            if (effects == null || sockets == null || magicRarity == null) throw new MissingFieldException("Epic Loot magic item layout changed");
            ranks.Clear(); foreach (var value in Enum.GetValues(rank)) ranks.Add(Convert.ToInt32(value));
            BossLootFilter.SetRarities(rank);
            int patched = 0;
            foreach (var method in assembly.GetType("EpicLoot.LootRoller", true).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (method.Name == "RollLootTableAndSpawnObjects" && method.ReturnType == typeof(List<GameObject>))
                {
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(EpicLootAdapter), nameof(AfterSpawn)));
                    patched++;
                }
            if (patched != 2) throw new MissingMethodException("Expected two Epic Loot spawn-list overloads");
            BossLootDetector.EnableEpic(harmony);
            return true;
        }
        private static MethodInfo Required(Type type, string name, params Type[] args)
        {
            return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, args, null)
                ?? throw new MissingMethodException(type.FullName, name);
        }
        private static void AfterSpawn(List<GameObject> __result) { BossLootDetector.RecordEpic(__result); }
        internal static string Plain(string value, int max = 256)
        {
            string text = tags.Replace(value ?? "", "").Trim();
            if (text.Length <= max) return text;
            return text.Substring(0, char.IsHighSurrogate(text[max - 1]) ? max - 1 : max);
        }
        internal static LootItem Read(ItemDrop.ItemData item, string id)
        {
            var result = new LootItem { Id = Plain(id, 128), Name = Plain((string)display.Invoke(null, new object[] { item })), Quantity = item.m_stack };
            var args = new object[] { item, 0 };
            if ((bool)rarity.Invoke(null, args) && ranks.Contains((int)args[1]))
            {
                result.Rank = (int)args[1];
                result.Rarity = Plain((string)rarityName.Invoke(null, new object[] { result.Rank }), 64);
                result.Color = Plain((string)rarityColor.Invoke(null, new object[] { result.Rank }), 16);
            }
            result.Unidentified = (bool)unidentified.Invoke(null, new object[] { item });
            // Do not expose hidden modifiers or sockets of unidentified drops.
            if (!result.Unidentified)
            {
                object magic = getMagic.Invoke(null, new object[] { item });
                if (magic != null)
                {
                    result.Sockets = Math.Max(0, Math.Min(64, (int)sockets.GetValue(magic)));
                    var lines = new List<string>();
                    foreach (object effect in (IEnumerable)effects.GetValue(magic))
                    {
                        if (lines.Count >= 12) break;
                        lines.Add(Plain((string)effectText.Invoke(null, new[] { effect, magicRarity.GetValue(magic), (object)false, "" }), 160));
                    }
                    result.Modifiers = Plain(string.Join("\n", lines), 1024);
                }
            }
            return result;
        }
    }
}
