using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ValheimMoments
{
    // All file ownership decisions share one lock. HTTP/encoding hold Busy until their
    // completion callback; cleanup and pin moves can never invalidate an open transfer.
    internal sealed class MomentGallery
    {
        internal sealed class Entry
        {
            internal string Id, Kind, Caption, Recorder, Status, Link = "";
            internal DateTime Created, Completed;
            internal bool Pinned, Busy, Saved;
            internal long Bytes;
            internal int Attempts;
            internal object Origin;
            internal object Acknowledgement;
            internal bool AsHost;
            internal Entry Copy() { return (Entry)MemberwiseClone(); }
        }
        private readonly object gate = new object();
        private readonly List<Entry> entries = new List<Entry>();
        private readonly string root;
        private Task worker;
        private bool dirty;
        private bool indexUnavailable;
        private int recoveryMaximum = 20, recoveryHours = 24, indexMaximum = 200;
        private long recoveryBudget = 250L * 1048576;
        private string indexError;
        internal string Error { get; private set; }
        internal MomentGallery(string root)
        {
            this.root = Path.GetFullPath(root);
            Directory.CreateDirectory(Path.Combine(root, "Recovery"));
            Directory.CreateDirectory(Path.Combine(root, "Saved"));
            CheckDirectories();
            Load();
        }
        internal Entry Add(string kind, string caption, string recorder, object origin, bool host, bool keep)
        {
            lock (gate)
            {
                var e = new Entry { Id = Guid.NewGuid().ToString("N"), Kind = kind, Caption = Clean(caption, 2000),
                    Recorder = Clean(recorder, 80), Origin = origin, AsHost = host, Created = DateTime.UtcNow,
                    Status = "Encoding", Busy = true, Pinned = keep };
                entries.Insert(0, e); dirty = true; return e;
            }
        }
        internal string PathFor(Entry e) { lock (gate) return Media(e); }
        internal string PreviewPath(string id)
        {
            Guid parsed; if (!Guid.TryParseExact(id, "N", out parsed)) throw new ArgumentException("id");
            return Path.Combine(root, id + ".rgba");
        }
        internal static byte[] Preview(byte[] source, int width, int height, bool flip)
        {
            if (source == null || source.Length != checked(width * height * 4) || width < 1 || height < 1) throw new ArgumentException("pixels");
            var pixels = new byte[256 * 144 * 4];
            double scale = Math.Min(256.0 / width, 144.0 / height);
            int w = Math.Max(1, (int)(width * scale)), h = Math.Max(1, (int)(height * scale));
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                // Texture2D raw rows start at the bottom; WebP rows start at the top.
                int sy = y * height / h; if (!flip) sy = height - 1 - sy;
                int from = (sy * width + x * width / w) * 4, to = (((144 - h) / 2 + y) * 256 + (256 - w) / 2 + x) * 4;
                Buffer.BlockCopy(source, from, pixels, to, 4);
            }
            return pixels;
        }
        internal void WritePreview(Entry e, byte[] pixels)
        {
            if (pixels.Length != 256 * 144 * 4) throw new ArgumentException("pixels");
            lock (gate) { CheckDirectories(); File.WriteAllBytes(PreviewPath(e.Id), pixels); }
        }
        private string Media(Entry e) { return Path.Combine(root, e.Saved ? "Saved" : "Recovery", e.Id + ".webp"); }
        internal Entry[] Snapshot() { lock (gate) return entries.Select(e => e.Copy()).ToArray(); }
        internal bool Pin(string id)
        {
            lock (gate)
            {
                var e = entries.Find(x => x.Id == id);
                if (e == null || (!e.Busy && !File.Exists(Media(e)))) return false;
                e.Pinned = true; dirty = true; return true;
            }
        }
        internal void Complete(Entry e, string status, string link = "")
        {
            if (e == null) return;
            lock (gate)
            {
                e.Busy = false; e.Status = status; e.Completed = DateTime.UtcNow;
                if (ValidLink(link)) e.Link = link;
                try { e.Bytes = File.Exists(Media(e)) ? new FileInfo(Media(e)).Length : 0; } catch { }
                dirty = true;
            }
        }
        internal void Uploading(Entry e)
        {
            if (e == null) return;
            lock (gate) { e.Status = "Uploading"; try { e.Bytes = new FileInfo(Media(e)).Length; } catch { } dirty = true; }
        }
        internal Entry BeginRetry(string id, object origin, bool host, bool confirmUnknown)
        {
            lock (gate)
            {
                var e = entries.Find(x => x.Id == id);
                if (e == null || e.Busy || origin == null || !ReferenceEquals(origin, e.Origin) || host != e.AsHost ||
                    e.Attempts >= 3 || (e.Status != "Failed" && e.Status != "Omitted" && e.Status != "Unknown") ||
                    (e.Status == "Unknown" && !confirmUnknown) || !File.Exists(Media(e)) ||
                    DateTime.UtcNow < e.Completed.AddSeconds(30 * Math.Pow(2, e.Attempts))) return null;
                e.Attempts++; e.Busy = true; e.Status = "Uploading"; dirty = true; return e;
            }
        }
        internal void Tick(int maximum = 20, long bytes = 250L * 1048576, int hours = 24, int index = 200)
        {
            lock (gate)
            {
                recoveryMaximum = Math.Max(1, Math.Min(100, maximum));
                recoveryBudget = Math.Max(10L * 1048576, Math.Min(1024L * 1048576, bytes));
                recoveryHours = Math.Max(1, Math.Min(168, hours));
                indexMaximum = Math.Max(20, Math.Min(1000, index));
                if (worker != null && !worker.IsCompleted) return;
                worker = Task.Run(SweepConfigured);
            }
        }
        internal Task Flush()
        {
            lock (gate)
            {
                // Queue a final pass even if a previous worker is just finishing.
                // Keep the user's last policy instead of silently restoring defaults.
                worker = (worker ?? Task.CompletedTask).ContinueWith(previous => {
                    var ignored = previous.Exception;
                    SweepConfigured();
                }, TaskScheduler.Default);
                return worker;
            }
        }
        private void SweepConfigured()
        {
            lock (gate) Sweep(recoveryMaximum, recoveryBudget, recoveryHours, indexMaximum);
        }
        private void Sweep(int maximum, long budget, int hours, int index)
        {
            lock (gate)
            {
                try
                {
                    CheckDirectories();
                    DateTime now = DateTime.UtcNow;
                    foreach (var e in entries.Where(x => !x.Busy))
                    {
                        string path = Media(e);
                        if (!File.Exists(path)) { if (e.Bytes != 0) { e.Bytes = 0; dirty = true; } continue; }
                        e.Bytes = new FileInfo(path).Length;
                        if (e.Pinned && !e.Saved)
                        { File.Move(path, Path.Combine(root, "Saved", e.Id + ".webp")); e.Saved = true; dirty = true; }
                        else if (!e.Pinned && now >= e.Completed.AddSeconds(30) &&
                            (e.Status == "Uploaded" || e.Status == "Not shared" || now >= e.Created.AddHours(hours)))
                        { File.Delete(path); e.Bytes = 0; dirty = true; }
                    }
                    var recovery = entries.Where(x => !x.Busy && !x.Pinned && x.Bytes > 0).OrderByDescending(x => x.Created).ToArray();
                    long total = 0; int count = 0;
                    foreach (var e in recovery)
                    {
                        total += e.Bytes; count++;
                        // A recently completed clip always gets its advertised Keep grace.
                        if ((count > maximum || total > budget) && now >= e.Completed.AddSeconds(30))
                        { File.Delete(Media(e)); e.Bytes = 0; dirty = true; }
                    }
                    while (entries.Count > index)
                    {
                        var e = entries.LastOrDefault(x => !x.Busy && (x.Pinned || x.Bytes == 0));
                        if (e == null) break;
                        entries.Remove(e); dirty = true; // Saved media is never quota-deleted.
                        File.Delete(PreviewPath(e.Id));
                    }
                    if (dirty && !indexUnavailable) { Persist(); dirty = false; }
                    // An unreadable index cannot prove that old files were unpinned.
                    // Preserve both the index and unrecognized media for recovery.
                    if (indexUnavailable) { Error = indexError; return; }
                    var known = new HashSet<string>(entries.Select(x => x.Id + ".webp"), StringComparer.OrdinalIgnoreCase);
                    foreach (string orphan in Directory.EnumerateFiles(Path.Combine(root, "Recovery")))
                    {
                        string name = Path.GetFileName(orphan); Guid id;
                        if (name.EndsWith(".webp.partial", StringComparison.Ordinal) && name.Length == 45 &&
                            Guid.TryParseExact(name.Substring(0, 32), "N", out id) && !entries.Any(x => x.Id == name.Substring(0, 32) && x.Busy) &&
                            (File.GetAttributes(orphan) & FileAttributes.ReparsePoint) == 0 && File.GetLastWriteTimeUtc(orphan) < now.AddHours(-24))
                        { File.Delete(orphan); continue; }
                        if (known.Contains(name) || (File.GetAttributes(orphan) & FileAttributes.ReparsePoint) != 0) continue;
                        if (name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out id) &&
                            File.GetLastWriteTimeUtc(orphan) < now.AddHours(-hours)) File.Delete(orphan);
                    }
                    var knownIds = new HashSet<string>(entries.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
                    foreach (string preview in Directory.EnumerateFiles(root, "*.rgba"))
                    {
                        string idText = Path.GetFileNameWithoutExtension(preview); Guid id;
                        if (Guid.TryParseExact(idText, "N", out id) && !knownIds.Contains(idText) &&
                            (File.GetAttributes(preview) & FileAttributes.ReparsePoint) == 0 && File.GetLastWriteTimeUtc(preview) < now.AddHours(-24)) File.Delete(preview);
                    }
                    Error = null;
                }
                catch (Exception e) { Error = "Gallery storage: " + e.GetType().Name; }
            }
        }
        private void Persist()
        {
            string path = Path.Combine(root, "index.bin"), temporary = path + ".partial";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1); writer.Write(entries.Count);
                foreach (var e in entries)
                {
                    writer.Write(e.Id); writer.Write(e.Kind); writer.Write(e.Caption); writer.Write(e.Recorder);
                    writer.Write(e.Created.ToBinary()); writer.Write(e.Completed.ToBinary()); writer.Write(e.Pinned);
                    writer.Write(e.Saved); writer.Write(e.Status); writer.Write(e.Link); writer.Write(e.Attempts);
                }
                writer.Flush(); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        private void Load()
        {
            string path = Path.Combine(root, "index.bin");
            try
            {
                if (!File.Exists(path)) return;
                if (new FileInfo(path).Length > 4 * 1048576) throw new InvalidDataException();
                var loaded = new List<Entry>(); var ids = new HashSet<string>();
                using (var stream = File.OpenRead(path)) using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != 1) throw new InvalidDataException();
                    int count = reader.ReadInt32(); if (count < 0 || count > 2000) throw new InvalidDataException();
                    for (int i = 0; i < count; i++)
                    {
                        var e = new Entry { Id = reader.ReadString(), Kind = reader.ReadString(), Caption = reader.ReadString(), Recorder = reader.ReadString(),
                            Created = DateTime.FromBinary(reader.ReadInt64()), Completed = DateTime.FromBinary(reader.ReadInt64()),
                            Pinned = reader.ReadBoolean(), Saved = reader.ReadBoolean(), Status = reader.ReadString(), Link = reader.ReadString(), Attempts = reader.ReadInt32() };
                        Guid id;
                        if (!Guid.TryParseExact(e.Id, "N", out id) || !ids.Add(e.Id) || e.Kind.Length > 32 || e.Caption.Length > 2000 || e.Recorder.Length > 80 ||
                            e.Status.Length > 80 || e.Link.Length > 200 || e.Attempts < 0 || e.Attempts > 3 || (e.Saved && !e.Pinned)) throw new InvalidDataException();
                        if (!ValidLink(e.Link)) e.Link = "";
                        if (e.Status == "Uploading" || e.Status == "Encoding") e.Status = "Unknown";
                        // Recover a crash between the atomic pin move and index replacement.
                        if (e.Pinned && File.Exists(Path.Combine(root, "Saved", e.Id + ".webp"))) e.Saved = true;
                        e.Bytes = File.Exists(Media(e)) ? new FileInfo(Media(e)).Length : 0;
                        loaded.Add(e);
                    }
                    if (stream.Position != stream.Length) throw new InvalidDataException();
                }
                entries.AddRange(loaded);
            }
            catch (Exception e)
            {
                indexUnavailable = true;
                indexError = "Gallery index unavailable (" + e.GetType().Name + "). Existing files and index.bin are preserved; new history cannot persist until the index is repaired.";
                Error = indexError;
            }
        }
        internal static bool ValidLink(string link)
        {
            if (string.IsNullOrEmpty(link)) return false;
            Uri uri;
            return Uri.TryCreate(link, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Host == "discord.com" &&
                uri.AbsolutePath.StartsWith("/channels/", StringComparison.Ordinal) && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
        }
        private void CheckDirectories()
        {
            foreach (string path in new[] { root, Path.Combine(root, "Recovery"), Path.Combine(root, "Saved") })
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Gallery cannot use redirected directories");
        }
        private static string Clean(string value, int length) { value = value ?? ""; return value.Length <= length ? value : value.Substring(0, length); }
    }
}
