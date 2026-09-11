using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;

namespace ValheimMoments
{
    // Pair an authenticated direct-peer token with the EXACT vanilla SetEvent
    // snapshot sent immediately after it on the same ordered connection.
    // No proximity/timestamp-window grouping and no change to vanilla event data.
    internal static class RaidIdentity
    {
        private const string Rpc = "ValheimMoments_RaidIdentity_v1";
        private static readonly Dictionary<RandomEvent, string> ids = new Dictionary<RandomEvent, string>();
        private static readonly HashSet<ZRpc> peers = new HashSet<ZRpc>();
        private static ZNet session;
        private static string pending;
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.DeclaredMethod(typeof(RandEventSystem), "SendCurrentRandomEvent"),
                new HarmonyMethod(typeof(RaidIdentity), nameof(Sending)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(RandEventSystem), "RPC_SetEvent"),
                postfix: new HarmonyMethod(typeof(RaidIdentity), nameof(Received)));
        }
        internal static void Tick()
        {
            if (!ReferenceEquals(session, ZNet.instance)) { Clear(); session = ZNet.instance; }
            if (session == null) return;
            var live = new HashSet<ZRpc>();
            foreach (var peer in session.GetPeers())
            {
                if (!peer.IsReady() || !peer.m_rpc.IsConnected()) continue;
                live.Add(peer.m_rpc);
                if (peers.Add(peer.m_rpc)) peer.m_rpc.Register<string>(Rpc, Receive);
            }
            peers.RemoveWhere(x => !live.Contains(x));
        }
        internal static string For(RandomEvent raid)
        {
            if (raid == null) return null;
            if (session != null && session.IsServer() && !ids.ContainsKey(raid))
            { if (ids.Count >= 16) ids.Clear(); ids.Add(raid, Guid.NewGuid().ToString("N")); }
            string id; return ids.TryGetValue(raid, out id) ? id : null;
        }
        private static string Snapshot(RandomEvent raid)
        {
            if (raid == null) return "-";
            var c = CultureInfo.InvariantCulture;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(raid.m_name)) + "|" + raid.m_time.ToString("R", c) + "|" +
                raid.m_pos.x.ToString("R", c) + "|" + raid.m_pos.y.ToString("R", c) + "|" + raid.m_pos.z.ToString("R", c);
        }
        private static void Sending(RandEventSystem __instance)
        {
            try
            {
                Tick(); if (session == null || !session.IsServer()) return;
                var raid = __instance.GetCurrentRandomEvent();
                string payload = (For(raid) ?? "-") + "|" + Snapshot(raid);
                foreach (var peer in peers) peer.Invoke(Rpc, new object[] { payload });
            }
            catch { }
        }
        private static void Receive(ZRpc rpc, string value)
        {
            if (session == null || session.IsServer() || !ReferenceEquals(session.GetServerPeer()?.m_rpc, rpc) || value == null || value.Length > 1024) return;
            pending = value;
        }
        private static void Received(RandEventSystem __instance)
        {
            try
            {
                if (session == null || session.IsServer()) return;
                string value = pending; pending = null;
                var raid = __instance.GetCurrentRandomEvent();
                if (raid == null || value == null || value.Length < 34 || value[32] != '|') return;
                string id = value.Substring(0, 32);
                if (!HighlightDirector.ValidId(id) || value.Substring(33) != Snapshot(raid)) return;
                if (ids.Count >= 16 && !ids.ContainsKey(raid)) ids.Clear();
                ids[raid] = id;
            }
            catch { pending = null; }
        }
        internal static void Clear() { ids.Clear(); peers.Clear(); pending = null; session = null; }
    }
}
