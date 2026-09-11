using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;

namespace ValheimMoments
{
    // The remote policy is an in-memory overlay. Never write host values into a
    // client's ConfigFile: saving an unrelated hotkey must preserve local preferences.
    internal sealed class HostConfiguration : IDisposable
    {
        private const string RpcName = "ValheimMoments_HostSettings_v2";
        internal sealed class ConfigurationManagerAttributes
        {
            public bool? ReadOnly;
            public bool? Browsable;
            public Action<ConfigEntryBase> CustomDrawer;
            public int? Order = 0;
            public string Category;
        }
        private sealed class Entry
        {
            internal ConfigEntryBase Config;
            internal ConfigurationManagerAttributes Tags;
            internal bool Local, Shared;
            internal Action Unsubscribe;
            internal string Key;
            internal Action<ConfigEntryBase> LocalDrawer;
        }
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        private readonly Dictionary<ConfigEntryBase, Entry> byConfig = new Dictionary<ConfigEntryBase, Entry>();
        private readonly HashSet<ZRpc> registered = new HashSet<ZRpc>();
        private readonly Dictionary<ZRpc, double> nextReply = new Dictionary<ZRpc, double>();
        private Dictionary<string, object> remote;
        private readonly Action<ConfigEntryBase> drawRemote;
        private readonly Action refresh, changed;
        private readonly Action<string> log;
        private ZNet session;
        private ZRpc server;
        private double now, nextTick, nextRequest, lastReceived;
        private string lastPayload;
        private bool disposed, lastLocked;
        internal bool Ready { get { return ZNet.instance == null || ZNet.instance.IsServer() || (ReferenceEquals(session, ZNet.instance) && remote != null && server != null && server.IsConnected() && now - lastReceived <= 10); } }

        internal HostConfiguration(Action<ConfigEntryBase> drawRemote, Action refresh, Action changed, Action<string> log)
        { this.drawRemote = drawRemote; this.refresh = refresh; this.changed = changed; this.log = log; }

        internal static bool IsLocal(string section, string key)
        {
            if (section == "Debug") return true;
            if (section == "Notifications") return true;
            if (section == "Discord") return false;
            if (section != "Capture") return false;
            return key == "Enabled" || key == "ManualCaptureKey" || key == "ToggleCaptureKey" ||
                key == "Width" || key == "Height" || key == "SizePreset" || key == "SaveLocalCopy" || key == "FPS" || key == "WebPQuality" || key == "MemoryBudgetMiB" || key == "FlipVertically";
        }
        private static string Key(ConfigEntryBase entry) { return entry.Definition.Section + "\n" + entry.Definition.Key; }
        internal void Register<T>(ConfigEntry<T> entry, ConfigurationManagerAttributes tags)
        {
            bool local = IsLocal(entry.Definition.Section, entry.Definition.Key);
            var state = new Entry { Config = entry, Tags = tags, Local = local, Key = Key(entry),
                LocalDrawer = tags.CustomDrawer,
                Shared = !local && (entry.Definition.Section != "Discord" || entry.Definition.Key == "Enabled"), Unsubscribe = () => entry.SettingChanged -= OnLocalChanged };
            entries.Add(state.Key, state); byConfig.Add(entry, state);
            entry.SettingChanged += OnLocalChanged;
        }
        private void OnLocalChanged(object sender, EventArgs args)
        {
            if (sender is ConfigEntryBase entry && entry.Definition.Section == "Notifications") return;
            changed?.Invoke();
        }
        internal bool PeerHasPolicy(ZRpc rpc)
        {
            double next;
            return session != null && session.IsServer() && registered.Contains(rpc) &&
                nextReply.TryGetValue(rpc, out next) && now - next <= 10;
        }
        internal T Get<T>(ConfigEntry<T> entry)
        {
            object value;
            var state = byConfig[entry];
            if (!state.Shared) return entry.Value;
            if (ZNet.instance != null && !ZNet.instance.IsServer() && ReferenceEquals(session, ZNet.instance) &&
                remote != null && remote.TryGetValue(state.Key, out value)) return (T)value;
            return entry.Value;
        }
        internal string Display(ConfigEntryBase entry)
        {
            object value;
            if (!byConfig[entry].Shared) return "Host controlled; not shared with clients";
            return remote != null && remote.TryGetValue(Key(entry), out value)
                ? TomlTypeConverter.ConvertToString(value, entry.SettingType) : "Waiting for host settings";
        }
        internal void RefreshPresentation() { UpdateManager(true); }
        private void UpdateManager(bool force)
        {
            bool locked = ZNet.instance != null && !ZNet.instance.IsServer();
            if (!force && locked == lastLocked) return;
            lastLocked = locked;
            foreach (var entry in entries.Values)
            {
                entry.Tags.ReadOnly = locked && !entry.Local;
                entry.Tags.Browsable = !(locked && entry.Config.Definition.Section == "Discord" && !entry.Shared);
                entry.Tags.CustomDrawer = locked && !entry.Local ? drawRemote : entry.LocalDrawer;
                string section = entry.Config.Definition.Section;
                entry.Tags.Category = section == "Notifications" ? "02 - Your Notifications" : entry.Local ? "01 - Your Capture" : section == "Discord" ? "03 - Discord (Host)" :
                    section == "Capture" ? "04 - Capture Timing (Host)" : "05 - " + section + " (Host)";
                if (section == "Debug") entry.Tags.Category = "99 - Advanced";
            }
            try { refresh?.Invoke(); } catch { }
        }
        internal string Export()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                var keys = new List<string>();
                foreach (var pair in entries) if (pair.Value.Shared) keys.Add(pair.Key);
                keys.Sort(StringComparer.Ordinal);
                writer.Write(1); writer.Write(keys.Count);
                foreach (string key in keys) { writer.Write(key); writer.Write(entries[key].Config.GetSerializedValue()); }
                writer.Flush();
                if (stream.Length > 48000) throw new InvalidDataException("Host settings exceed transfer limit");
                return Convert.ToBase64String(stream.ToArray());
            }
        }
        internal void Apply(string payload)
        {
            if (payload == null || payload.Length > 64000) throw new InvalidDataException();
            var values = new Dictionary<string, object>();
            using (var stream = new MemoryStream(Convert.FromBase64String(payload)))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadInt32() != 1) throw new InvalidDataException();
                int count = reader.ReadInt32(), expected = 0;
                foreach (var entry in entries.Values) if (entry.Shared) expected++;
                if (count != expected || count > 128) throw new InvalidDataException();
                for (int i = 0; i < count; i++)
                {
                    string key = reader.ReadString(), text = reader.ReadString();
                    Entry entry;
                    if (key.Length > 256 || text.Length > 4096 || !entries.TryGetValue(key, out entry) || !entry.Shared) throw new InvalidDataException();
                    object value = TomlTypeConverter.ConvertToValue(text, entry.Config.SettingType);
                    if (value is double && (double.IsNaN((double)value) || double.IsInfinity((double)value))) throw new InvalidDataException();
                    if (entry.Config.Description.AcceptableValues != null && !entry.Config.Description.AcceptableValues.IsValid(value)) throw new InvalidDataException();
                    if (entry.Config.SettingType.IsEnum && !Enum.IsDefined(entry.Config.SettingType, value)) throw new InvalidDataException();
                    values.Add(key, value);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException();
            }
            remote = values;
        }
        internal void Tick(double time)
        {
            if (disposed) return;
            now = time;
            if (now < nextTick) return;
            nextTick = now + 0.25;
            var current = ZNet.instance;
            if (!ReferenceEquals(session, current))
            {
                session = current; remote = null; server = null; lastPayload = null;
                registered.Clear(); nextReply.Clear(); nextRequest = 0;
                UpdateManager(true); changed?.Invoke();
                if (session != null && !session.IsServer()) log("Waiting for host settings; host and clients need matching 0.13.0 settings schema.");
            }
            UpdateManager(false);
            if (session == null) return;
            var live = new HashSet<ZRpc>();
            foreach (var peer in session.GetPeers())
            {
                if (!peer.IsReady() || !peer.m_rpc.IsConnected()) continue;
                live.Add(peer.m_rpc);
                if (registered.Add(peer.m_rpc)) peer.m_rpc.Register<string>(RpcName, Receive);
            }
            registered.RemoveWhere(rpc => !live.Contains(rpc));
            var stale = new List<ZRpc>();
            foreach (var pair in nextReply) if (!live.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (var rpc in stale) nextReply.Remove(rpc);
            if (session.IsServer()) return;
            if (remote != null && now - lastReceived > 10)
            {
                remote = null; lastPayload = null; changed?.Invoke(); UpdateManager(true);
                log("Host settings timed out; recording paused until settings are received again.");
            }
            var host = session.GetServerPeer();
            var connection = host == null ? null : host.m_rpc;
            if (!ReferenceEquals(server, connection) || (server != null && !server.IsConnected()))
            { server = connection; remote = null; lastPayload = null; nextRequest = 0; changed?.Invoke(); UpdateManager(true); }
            if (server != null && registered.Contains(server) && now >= nextRequest)
            { nextRequest = now + 2; server.Invoke(RpcName, new object[] { "Q" }); }
        }
        private void Receive(ZRpc rpc, string packet)
        {
            if (disposed || session == null || !ReferenceEquals(session, ZNet.instance) || !registered.Contains(rpc) || !rpc.IsConnected() || packet == null || packet.Length > 64002) return;
            try
            {
                if (session.IsServer())
                {
                    double next;
                    if (packet != "Q" || (nextReply.TryGetValue(rpc, out next) && now < next)) return;
                    string payload = Export();
                    rpc.Invoke(RpcName, new object[] { "S|" + payload });
                    nextReply[rpc] = now + 1;
                }
                else if (ReferenceEquals(server, rpc) && ReferenceEquals(session.GetServerPeer()?.m_rpc, rpc) && packet.StartsWith("S|", StringComparison.Ordinal))
                {
                    string payload = packet.Substring(2);
                    if (payload == lastPayload) { lastReceived = now; return; }
                    Apply(payload); lastPayload = payload; lastReceived = now;
                    UpdateManager(true); changed?.Invoke(); log("Host event settings applied; local recording preferences preserved.");
                }
            }
            catch { log("Host settings unavailable or incompatible; no client policy override accepted."); }
        }
        public void Dispose()
        {
            disposed = true;
            foreach (var entry in entries.Values) entry.Unsubscribe();
            remote = null; registered.Clear(); nextReply.Clear();
        }
    }
}
