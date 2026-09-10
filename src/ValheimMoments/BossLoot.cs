using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValheimMoments
{
    internal sealed class LootItem
    {
        internal string Id, Name;
        internal int Quantity;
        internal int Rank = -1, Sockets;
        internal string Rarity = "", Color = "", Modifiers = "";
        internal bool Unidentified;
    }
    internal sealed class BossLoot
    {
        internal readonly string Id = Guid.NewGuid().ToString("N");
        internal readonly List<LootItem> Items = new List<LootItem>();
        internal bool Observed, Incomplete;
        internal bool Pending;
        internal int Revision;

        internal void AddEpic(LootItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Name) || string.IsNullOrEmpty(item.Id) || item.Quantity <= 0 || Items.Count >= 64) { Incomplete = true; return; }
            // Distinct generated equipment stays distinct even with the same base name.
            Items.Add(item); Observed = true;
        }

        internal void Add(string id, string name, long quantity)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || quantity <= 0) { Incomplete = true; return; }
            if (id.Length > 128 || name.Length > 256 || quantity > int.MaxValue) { Incomplete = true; return; }
            var item = Items.Find(i => i.Id == id && i.Name == name);
            if (item != null)
            {
                if (quantity > int.MaxValue - item.Quantity) { Incomplete = true; return; }
                item.Quantity += (int)quantity;
            }
            else if (Items.Count < 64) Items.Add(new LootItem { Id = id, Name = name, Quantity = (int)quantity });
            else Incomplete = true;
        }

        internal string Display(int maximum, bool quantity, Func<string, string> localize, bool showRarity = true, bool showModifiers = true, bool showSockets = true, bool showUnidentified = true)
        {
            if (!Observed) return "unavailable";
            if (Items.Count == 0) return Incomplete ? "unavailable" : "No items generated.";
            var sorted = new List<LootItem>();
            foreach (var item in Items) sorted.Add(new LootItem { Id = item.Id, Name = localize(item.Name), Quantity = item.Quantity, Rank = item.Rank, Rarity = localize(item.Rarity), Color = item.Color, Modifiers = localize(item.Modifiers), Sockets = item.Sockets, Unidentified = item.Unidentified });
            sorted.Sort((a, b) => { int c = b.Rank.CompareTo(a.Rank); if (c != 0) return c; c = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name); return c != 0 ? c : StringComparer.Ordinal.Compare(a.Id, b.Id); });
            maximum = Math.Max(1, Math.Min(20, maximum));
            var lines = new List<string>();
            for (int i = 0; i < Math.Min(maximum, sorted.Count); i++)
            {
                var item = sorted[i];
                lines.Add("* " + (showRarity && item.Rank >= 0 ? Marker(item.Color) + " " + item.Rarity + " " : "") + item.Name + (quantity ? " \u00D7" + item.Quantity : "") + (showUnidentified && item.Unidentified ? " (unidentified)" : ""));
                if (!item.Unidentified && showModifiers && !string.IsNullOrEmpty(item.Modifiers)) lines.Add("  " + item.Modifiers.Replace("\n", "\n  "));
                if (!item.Unidentified && showSockets && item.Sockets > 0) lines.Add("  Sockets: " + item.Sockets);
            }
            if (sorted.Count > maximum) lines.Add("+" + (sorted.Count - maximum) + " more item types");
            if (Incomplete) lines.Add("Some loot details unavailable.");
            if (Pending) lines.Add("Additional Epic Loot details unavailable before upload.");
            return string.Join("\n", lines);
        }

        private static string Marker(string color)
        {
            int rgb;
            if (color == null || color.Length != 7 || color[0] != '#' || !int.TryParse(color.Substring(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out rgb)) return "\u25C6";
            int[] palette = { 0x4285f4, 0x9b59b6, 0xf39c12, 0xe53935, 0x43a047, 0xfdd835, 0xeeeeee };
            string[] icons = { "🔵", "🟣", "🟠", "🔴", "🟢", "🟡", "⚪" };
            int best = 0, distance = int.MaxValue;
            for (int i = 0; i < palette.Length; i++)
            {
                int r = (rgb >> 16) - (palette[i] >> 16), g = ((rgb >> 8) & 255) - ((palette[i] >> 8) & 255), b = (rgb & 255) - (palette[i] & 255);
                int d = r*r + g*g + b*b; if (d < distance) { distance = d; best = i; }
            }
            return icons[best];
        }

        internal string Encode()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    writer.Write(2); writer.Write(Observed); writer.Write(Incomplete); writer.Write(Pending); writer.Write(Revision); writer.Write(Items.Count);
                    foreach (var item in Items) { writer.Write(item.Id); writer.Write(item.Name); writer.Write(item.Quantity); writer.Write(item.Rank); writer.Write(item.Rarity); writer.Write(item.Color); writer.Write(item.Modifiers); writer.Write(item.Sockets); writer.Write(item.Unidentified); }
                }
                return Convert.ToBase64String(stream.ToArray());
            }
        }
        internal static BossLoot Decode(string encoded)
        {
            if (encoded == null || encoded.Length > 524288) return null;
            try
            {
                using (var stream = new MemoryStream(Convert.FromBase64String(encoded)))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (reader.ReadInt32() != 2) return null;
                    var result = new BossLoot { Observed = reader.ReadBoolean(), Incomplete = reader.ReadBoolean(), Pending = reader.ReadBoolean(), Revision = reader.ReadInt32() };
                    if (result.Revision < 0) return null;
                    int count = reader.ReadInt32(); if (count < 0 || count > 64) return null;
                    for (int i = 0; i < count; i++)
                    {
                        string id = reader.ReadString(), name = reader.ReadString(); int amount = reader.ReadInt32();
                        if (id.Length == 0 || id.Length > 128 || name.Length == 0 || name.Length > 256 || amount <= 0) return null;
                        var item = new LootItem { Id = id, Name = name, Quantity = amount, Rank = reader.ReadInt32(), Rarity = reader.ReadString(), Color = reader.ReadString(), Modifiers = reader.ReadString(), Sockets = reader.ReadInt32(), Unidentified = reader.ReadBoolean() };
                        if (item.Rank < -1 || item.Rank > 100 || item.Rarity.Length > 64 || item.Color.Length > 16 || item.Modifiers.Length > 1024 || item.Sockets < 0 || item.Sockets > 64) return null;
                        result.Items.Add(item);
                    }
                    return stream.Position == stream.Length ? result : null;
                }
            }
            catch { return null; }
        }
    }

    internal sealed class LootInbox
    {
        private sealed class Entry
        {
            internal long Sender; internal string Enemy, Token; internal double Time;
            internal bool Taken, Completed; internal BossLoot Loot = new BossLoot();
        }
        private readonly List<Entry> entries = new List<Entry>();
        private void Prune(double now) { entries.RemoveAll(e => now - e.Time > 30); }
        internal void Announce(long sender, string enemy, string token, double now)
        {
            Guid id;
            if (string.IsNullOrEmpty(enemy) || enemy.Length > 256 || token == null || token.Length != 32 || !Guid.TryParseExact(token, "N", out id)) return;
            Prune(now);
            if (entries.Exists(e => e.Sender == sender && e.Token == token)) return;
            if (entries.Count >= 64) entries.RemoveAt(0);
            entries.Add(new Entry { Sender = sender, Enemy = enemy, Token = token, Time = now });
        }
        internal BossLoot Take(long sender, string enemy, double now)
        {
            Prune(now);
            var entry = entries.FindLast(e => !e.Taken && e.Sender == sender && e.Enemy == enemy && now - e.Time <= 5);
            if (entry == null) return null;
            entry.Taken = true; return entry.Loot;
        }
        internal void Complete(long sender, string token, string payload, double now)
        {
            Prune(now);
            var entry = entries.Find(e => e.Sender == sender && e.Token == token);
            if (entry == null) return;
            var decoded = BossLoot.Decode(payload); if (decoded == null) return;
            if (entry.Completed && decoded.Revision <= entry.Loot.Revision) return;
            entry.Loot.Items.Clear(); entry.Loot.Items.AddRange(decoded.Items);
            entry.Loot.Observed = decoded.Observed; entry.Loot.Incomplete = decoded.Incomplete; entry.Loot.Pending = decoded.Pending; entry.Loot.Revision = decoded.Revision; entry.Completed = true;
        }
        internal void Clear() { entries.Clear(); }
    }
}
