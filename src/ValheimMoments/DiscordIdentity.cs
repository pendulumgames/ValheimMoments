using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ValheimMoments
{
    internal static class DiscordMentionText
    {
        internal static bool ValidId(string id)
        {
            ulong number;
            return id != null && id.Length >= 17 && id.Length <= 20 && Regex.IsMatch(id, @"^[0-9]+$") &&
                ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number != 0;
        }
        internal static string Name(string name)
        {
            string value = (name ?? "").Replace("\r", " ").Replace("\n", " ").Replace("*", "").Replace("`", "").Trim();
            return value.Length <= 80 ? value : value.Substring(0, char.IsHighSurrogate(value[79]) ? 79 : 80);
        }
        internal static string Format(string message, IDictionary<string, string> identities, out string[] allowed)
        {
            var users = new HashSet<string>(StringComparer.Ordinal);
            // Ignore hand-written mentions in templates/remote captions. Only the
            // session's explicitly configured identities can mint pingable mentions.
            string text = Regex.Replace(message ?? "", @"<@!?([0-9]+)>", "@$1");
            text = Regex.Replace(text, @"(?m)(\*\*Recorded by:\*\*|\*\*Kill credit:\*\*|^Kill credit:)[ \t]*([^\r\n]*)", match => {
                bool credits = match.Groups[1].Value.IndexOf("Kill credit", StringComparison.Ordinal) >= 0;
                string[] names = credits ? match.Groups[2].Value.Split(new[] { ", " }, StringSplitOptions.None) : new[] { match.Groups[2].Value };
                for (int i = 0; i < names.Length; i++)
                {
                    string suffix = "", name = names[i];
                    foreach (string ending in new[] { " (full list unavailable)", " (first kill for this character)" })
                        if (name.EndsWith(ending, StringComparison.Ordinal)) { suffix = ending; name = name.Substring(0, name.Length - ending.Length); break; }
                    string id;
                    if (identities.TryGetValue(name, out id) && ValidId(id))
                    { names[i] = name + " (<@" + id + ">)" + suffix; users.Add(id); }
                }
                return match.Groups[1].Value + " " + string.Join(", ", names);
            });
            if (text.Length > 2000) text = text.Substring(0, char.IsHighSurrogate(text[1999]) ? 1999 : 2000);
            var present = new List<string>();
            foreach (string id in users) if (text.Contains("<@" + id + ">")) present.Add(id);
            allowed = present.ToArray(); return text;
        }
    }

    // Main-thread direct-peer registry. A client supplies only its own Discord ID;
    // its character name comes from the authenticated game connection.
    internal sealed class DiscordIdentity
    {
        private const string Rpc = "ValheimMoments_DiscordIdentity_v1";
        private readonly HashSet<ZRpc> registered = new HashSet<ZRpc>();
        private readonly Dictionary<ZRpc, string> identities = new Dictionary<ZRpc, string>();
        private ZNet session;
        private double nextSend;
        private string localId, localName;
        internal void Tick(double now, string id, string name)
        {
            if (!ReferenceEquals(session, ZNet.instance)) { session = ZNet.instance; registered.Clear(); identities.Clear(); nextSend = 0; }
            localId = DiscordMentionText.ValidId(id) ? id : ""; localName = name;
            if (session == null) return;
            var live = new HashSet<ZRpc>();
            foreach (var peer in session.GetPeers())
            {
                if (!peer.IsReady() || !peer.m_rpc.IsConnected()) continue;
                live.Add(peer.m_rpc);
                if (registered.Add(peer.m_rpc)) peer.m_rpc.Register<string>(Rpc, Receive);
            }
            registered.RemoveWhere(peer => !live.Contains(peer));
            var stale = new List<ZRpc>(); foreach (var peer in identities.Keys) if (!live.Contains(peer)) stale.Add(peer);
            foreach (var peer in stale) identities.Remove(peer);
            if (!session.IsServer() && now >= nextSend)
            {
                var host = session.GetServerPeer();
                if (host != null && live.Contains(host.m_rpc)) { host.m_rpc.Invoke(Rpc, new object[] { localId }); nextSend = now + 2; }
            }
        }
        private void Receive(ZRpc rpc, string id)
        {
            if (session == null || !ReferenceEquals(session, ZNet.instance) || !session.IsServer() || !registered.Contains(rpc) || !rpc.IsConnected()) return;
            if (string.IsNullOrEmpty(id)) identities.Remove(rpc);
            else if (DiscordMentionText.ValidId(id)) identities[rpc] = id;
        }
        internal void Apply(DiscordOptions options)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            Action<string, string> add = (name, id) => {
                name = DiscordMentionText.Name(name);
                if (name.Length == 0 || name.Contains(",") || name.Contains("<") || name.Contains(">")) return;
                if (map.ContainsKey(name)) duplicates.Add(name); else map.Add(name, id ?? "");
            };
            add(localName, localId);
            if (session != null && ReferenceEquals(session, ZNet.instance) && session.IsServer())
                foreach (var peer in session.GetPeers())
                {
                    if (!peer.IsReady() || !peer.m_rpc.IsConnected()) continue;
                    string id; identities.TryGetValue(peer.m_rpc, out id); add(peer.m_playerName, id);
                }
            foreach (string name in duplicates) map.Remove(name);
            options.Message = DiscordMentionText.Format(options.Message, map, out options.MentionUsers);
        }
    }
}
