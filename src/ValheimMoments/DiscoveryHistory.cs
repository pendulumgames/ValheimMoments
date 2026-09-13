using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ValheimMoments
{
    internal sealed class DiscoveryJournal
    {
        internal const int Maximum = 256;
        internal readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
        internal bool Dirty;
        internal bool Visit(string key, string legacyKey = null)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 192 || Seen.Count >= Maximum) return false;
            bool previouslySeen = legacyKey != null && Seen.Contains(legacyKey);
            // Any previously recorded variant also proves the main biome was visited.
            if (key.StartsWith("biome:", StringComparison.Ordinal) && key.IndexOf('/') < 0)
                foreach (string old in Seen)
                    if (old.StartsWith(key + "/", StringComparison.Ordinal)) { previouslySeen = true; break; }
            if (!Seen.Add(key)) return false;
            Dirty = true;
            return !previouslySeen;
        }
        internal static DiscoveryJournal Load(string path)
        {
            var journal = new DiscoveryJournal();
            if (!File.Exists(path))
            {
                string folder = Path.GetDirectoryName(path);
                if (Directory.Exists(folder))
                {
                    int count = 0;
                    foreach (string file in Directory.EnumerateFiles(folder)) if (++count >= 128) throw new IOException("Discovery history capacity reached");
                }
                return journal;
            }
            if (new FileInfo(path).Length > 262144) throw new InvalidDataException("Oversized discovery history");
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.ReadInt32() != 0x31444d56) throw new InvalidDataException("Unknown discovery history format");
                int count = reader.ReadUInt16();
                if (count > Maximum) throw new InvalidDataException();
                var utf8 = new UTF8Encoding(false, true);
                for (int i = 0; i < count; i++)
                {
                    int length = reader.ReadUInt16();
                    if (length < 1 || length > 768) throw new InvalidDataException();
                    byte[] bytes = reader.ReadBytes(length);
                    if (bytes.Length != length) throw new EndOfStreamException();
                    string key = utf8.GetString(bytes);
                    if (string.IsNullOrWhiteSpace(key) || key.Length > 192 || !journal.Seen.Add(key)) throw new InvalidDataException();
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException();
            }
            return journal;
        }
        internal byte[] Snapshot()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x31444d56); writer.Write((ushort)Seen.Count);
                var sorted = new List<string>(Seen); sorted.Sort(StringComparer.Ordinal);
                foreach (string key in sorted)
                { byte[] bytes = Encoding.UTF8.GetBytes(key); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
                return stream.ToArray();
            }
        }
        internal static void Save(string path, byte[] data)
        {
            if (data.Length > 262144) throw new InvalidDataException();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".pending";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(data, 0, data.Length); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    // Main-thread state; disk work is serialized on workers. Only one I/O operation
    // and one bounded set of visits received during loading are retained.
    internal sealed class DiscoveryHistory
    {
        private readonly string folder;
        private readonly string legacyFolder;
        private readonly Action<string> log;
        private string desired, current, failed;
        private DiscoveryJournal journal;
        private readonly HashSet<string> duringLoad = new HashSet<string>(StringComparer.Ordinal);
        private Task<DiscoveryJournal> loading;
        private string loadingKey;
        private Task saving;
        internal DiscoveryHistory(string folder, Action<string> log, string legacyFolder = null)
        { this.folder = folder; this.log = log; this.legacyFolder = legacyFolder; }
        internal bool Ready { get { return loading == null && desired != null && current == desired && journal != null && failed != desired; } }
        internal bool Use(long player, long world)
        {
            string key = player == 0 || world == 0 ? null : player.ToString("X16") + "-" + world.ToString("X16");
            if (key == desired) return false;
            desired = key; duringLoad.Clear(); return true;
        }
        internal bool Visit(string key, string legacyKey = null)
        {
            if (desired == null || string.IsNullOrWhiteSpace(key) || key.Length > 192) return false;
            if (!Ready) { if (duringLoad.Count < DiscoveryJournal.Maximum) duringLoad.Add(key); return false; }
            return journal.Visit(key, legacyKey);
        }
        internal void Tick()
        {
            if (saving != null)
            {
                if (!saving.IsCompleted) return;
                try { saving.GetAwaiter().GetResult(); }
                catch { failed = current; log("Discovery history could not be saved; this context will skip further discoveries."); }
                saving = null;
            }
            if (loading != null)
            {
                if (!loading.IsCompleted) return;
                try
                {
                    var loaded = loading.GetAwaiter().GetResult();
                    current = loadingKey; journal = loaded;
                    if (current == desired) foreach (string key in duringLoad) journal.Visit(key);
                }
                catch { failed = loadingKey; current = loadingKey; journal = null; log("Discovery history unavailable; skipping rather than replaying discoveries."); }
                loading = null;
                if (current == desired) duringLoad.Clear();
            }
            if (journal != null && journal.Dirty && failed != current)
            {
                byte[] snapshot = journal.Snapshot(); journal.Dirty = false;
                string path = Path.Combine(folder, current + ".bin");
                saving = Task.Run(() => DiscoveryJournal.Save(path, snapshot)); return;
            }
            if (current != desired && desired != null && failed != desired)
            {
                loadingKey = desired;
                string path = Path.Combine(folder, desired + ".bin");
                string legacyPath = legacyFolder == null ? null : Path.Combine(legacyFolder, desired + ".bin");
                loading = Task.Run(() => {
                    // Keep player state outside the replaceable plugin installation.
                    // A corrupt current journal must fail closed, never fall back to older data.
                    bool exists = File.Exists(path);
                    var loaded = DiscoveryJournal.Load(path); // Also enforces destination capacity before migration.
                    if (exists || legacyPath == null || !File.Exists(legacyPath)) return loaded;
                    var migrated = DiscoveryJournal.Load(legacyPath);
                    migrated.Dirty = true;
                    return migrated;
                });
            }
        }
        internal Task Flush()
        {
            // Used on shutdown only; continue the last immutable save off-thread.
            if (journal == null || !journal.Dirty || failed == current) return saving ?? Task.CompletedTask;
            byte[] snapshot = journal.Snapshot(); journal.Dirty = false;
            string path = Path.Combine(folder, current + ".bin"); var previous = saving;
            saving = Task.Run(async () => { if (previous != null) await previous.ConfigureAwait(false); DiscoveryJournal.Save(path, snapshot); });
            return saving;
        }
    }
}
