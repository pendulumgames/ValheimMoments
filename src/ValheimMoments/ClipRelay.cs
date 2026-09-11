using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ValheimMoments
{
    // Direct ZRpc binds the sender to the connected transport; no routed sender ID
    // or client-provided destination/username is accepted by this protocol.
    internal sealed class ClipRelay : IDisposable
    {
        private const string Rpc = "ValheimMoments_ClipRelay_v1";
        private readonly Func<string, bool> allow;
        private readonly Func<int> limit;
        private readonly Action<RelayBuffer, string, Action<bool>> deliver;
        private readonly Action<string> log;
        private readonly Func<ZRpc, bool> allowPeer;
        private readonly Func<ZRpc, string, double, bool> acceptEvent;
        private Action<bool> outgoingCompletion;
        private readonly HashSet<ZRpc> registered = new HashSet<ZRpc>();
        private readonly Dictionary<ZRpc, double> nextOffer = new Dictionary<ZRpc, double>();
        private ZNet session;
        private ZRpc source, target, deliveryPeer;
        private RelayBuffer incoming;
        private byte[] outgoing;
        private Task<byte[]> preparation;
        private Task<string> cleanup;
        private string outgoingFile;
        private bool keepOutgoing;
        private string offeredKind, offeredMessage;
        private string outgoingId;
        private int sent, acknowledged;
        private double now, incomingDeadline, outgoingDeadline, nextSend, nextTick;
        private bool delivering, awaitingOffer, waitingResult, disposed;

        internal ClipRelay(Func<string, bool> allow, Func<int> limit, Action<RelayBuffer, string, Action<bool>> deliver, Action<string> log, Func<ZRpc, bool> allowPeer = null, Func<ZRpc, string, double, bool> acceptEvent = null)
        { this.allow = allow; this.limit = limit; this.deliver = deliver; this.log = log; this.allowPeer = allowPeer ?? (rpc => true); this.acceptEvent = acceptEvent ?? ((rpc, kind, now) => true); }

        internal void Tick(double time)
        {
            if (disposed) return;
            now = time;
            if (cleanup != null && cleanup.IsCompleted) { log(cleanup.GetAwaiter().GetResult()); cleanup = null; }
            if (now < nextTick) return;
            nextTick = now + 0.05;
            var current = ZNet.instance;
            if (!ReferenceEquals(session, current)) { Reset(); session = current; }
            if (session == null) return;
            var live = new HashSet<ZRpc>();
            foreach (var peer in session.GetPeers())
            {
                if (!peer.IsReady() || !peer.m_rpc.IsConnected()) continue;
                live.Add(peer.m_rpc);
                if (registered.Add(peer.m_rpc)) peer.m_rpc.Register<string>(Rpc, Receive);
            }
            registered.RemoveWhere(rpc => !live.Contains(rpc));
            var stale = new List<ZRpc>();
            foreach (var entry in nextOffer) if (!live.Contains(entry.Key)) stale.Add(entry.Key);
            foreach (var rpc in stale) nextOffer.Remove(rpc);
            if (source != null && (!live.Contains(source) || now > incomingDeadline)) { incoming = null; source = null; }
            if (target != null && (!live.Contains(target) || now > outgoingDeadline)) EndOutgoing("Transfer ended or host unavailable; local clip retained.");
            if (preparation != null && preparation.IsCompleted)
            {
                var ready = preparation; preparation = null;
                try
                {
                    var bytes = ready.GetAwaiter().GetResult();
                    if (target != null)
                    {
                        outgoing = bytes; outgoingDeadline = now + 10;
                        Send(target, "B|" + outgoingId + "|" + offeredKind + "|" + bytes.Length + "|" + RelayProtocol.Text(offeredMessage));
                    }
                }
                catch { EndOutgoing("Clip could not be read or exceeds 10 MiB; local copy retained."); }
            }
            if (outgoing != null && !awaitingOffer && !waitingResult && sent == acknowledged && now >= nextSend)
            {
                try
                {
                    int remaining = (int)(outgoing.Length - sent);
                    var bytes = new byte[Math.Min(RelayProtocol.ChunkBytes, remaining)];
                    int read = bytes.Length;
                    if (read == 0) { EndOutgoing("Clip could not be read; local copy retained."); return; }
                    Buffer.BlockCopy(outgoing, sent, bytes, 0, read);
                    int offset = sent; sent += read; nextSend = now + 0.05;
                    Send(target, "C|" + outgoingId + "|" + offset + "|" + Convert.ToBase64String(bytes));
                }
                catch { EndOutgoing("Clip transfer failed; local copy retained."); }
            }
        }

        internal bool Offer(ZNet capturedSession, string file, string kind, string message, bool saveLocalCopy = true, Action<bool> completed = null)
        {
            if (disposed || session == null || !ReferenceEquals(session, capturedSession) || session.IsServer() || outgoing != null || preparation != null || cleanup != null || target != null) return false;
            var peer = session.GetServerPeer();
            if (peer == null || !registered.Contains(peer.m_rpc)) return false;
            try
            {
                target = peer.m_rpc; outgoingId = Guid.NewGuid().ToString("N");
                outgoingCompletion = completed;
                outgoingFile = file; keepOutgoing = saveLocalCopy;
                offeredKind = kind; offeredMessage = message;
                sent = acknowledged = 0; awaitingOffer = true; waitingResult = false; outgoingDeadline = now + 10;
                preparation = Task.Run(() => {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (stream.Length < 20 || stream.Length > RelayProtocol.MaxBytes) throw new IOException("Clip size outside relay limit");
                        var bytes = new byte[(int)stream.Length]; int offset = 0;
                        while (offset < bytes.Length) { int count = stream.Read(bytes, offset, bytes.Length - offset); if (count == 0) throw new EndOfStreamException(); offset += count; }
                        return bytes;
                    }
                });
                log("Offered clip to host; client webhook and bot-name settings are ignored.");
                return true;
            }
            catch { EndOutgoing("Could not offer clip to host; local copy retained."); return false; }
        }

        private void Receive(ZRpc rpc, string packet)
        {
            if (disposed || session == null || !ReferenceEquals(session, ZNet.instance) || !registered.Contains(rpc) || !rpc.IsConnected() || packet == null || packet.Length > RelayProtocol.MaxPacketChars) return;
            try
            {
                string[] p = packet.Split('|');
                if (p.Length < 2 || !RelayProtocol.ValidId(p[1])) return;
                if (session.IsServer()) ReceiveHost(rpc, p);
                else if (ReferenceEquals(target, rpc) && p[1] == outgoingId) ReceiveClient(p);
            }
            catch { log("Invalid relay data ignored."); }
        }
        private void ReceiveHost(ZRpc rpc, string[] p)
        {
            if (p[0] == "B" && p.Length == 5)
            {
                double next;
                if (nextOffer.TryGetValue(rpc, out next) && now < next) { Send(rpc, "R|" + p[1] + "|0"); return; }
                nextOffer[rpc] = now + 15;
                int size;
                if (source != null || delivering || !allowPeer(rpc) || !RelayProtocol.ValidKind(p[2]) || !allow(p[2]) || !int.TryParse(p[3], out size)) { Send(rpc, "R|" + p[1] + "|0"); return; }
                string message = RelayProtocol.ReadText(p[4]);
                try { incoming = new RelayBuffer(p[1], p[2], message, size, limit()); }
                catch { Send(rpc, "R|" + p[1] + "|0"); return; }
                if (!acceptEvent(rpc, p[2], now)) { incoming = null; Send(rpc, "R|" + p[1] + "|0"); return; }
                source = rpc; incomingDeadline = now + 120;
                Send(rpc, "A|" + p[1] + "|0");
            }
            else if (p[0] == "C" && p.Length == 4 && ReferenceEquals(source, rpc) && incoming != null && incoming.Id == p[1])
            {
                int offset;
                if (!allowPeer(rpc) || !allow(incoming.Kind) || !int.TryParse(p[2], out offset) || !incoming.Add(offset, Convert.FromBase64String(p[3])))
                { Send(rpc, "R|" + p[1] + "|0"); incoming = null; source = null; return; }
                if (!incoming.Complete) { Send(rpc, "A|" + p[1] + "|" + incoming.Received); return; }
                var complete = incoming; incoming = null; source = null;
                if (!complete.ValidWebP()) { Send(rpc, "R|" + p[1] + "|0"); return; }
                delivering = true; deliveryPeer = rpc;
                ZNet origin = session;
                string recorder = "Connected player";
                foreach (var peer in session.GetPeers()) if (ReferenceEquals(peer.m_rpc, rpc)) { recorder = peer.m_playerName; break; }
                Send(rpc, "A|" + p[1] + "|" + complete.Received);
                try { deliver(complete, recorder, success => {
                    delivering = false; deliveryPeer = null;
                    if (!disposed && ReferenceEquals(origin, session) && registered.Contains(rpc)) Send(rpc, "R|" + complete.Id + "|" + (success ? "1" : "0"));
                }); }
                catch { delivering = false; deliveryPeer = null; Send(rpc, "R|" + complete.Id + "|0"); }
            }
        }
        private void ReceiveClient(string[] p)
        {
            if (outgoing == null || p.Length != 3) return;
            if (p[0] == "R")
            {
                if (p[2] == "1")
                {
                    // Only a matching host result AFTER the complete transfer can remove our file.
                    if (!waitingResult || acknowledged != outgoing.Length) return;
                    if (!keepOutgoing)
                    {
                        string file = outgoingFile;
                        cleanup = Task.Run(() => {
                            try { File.Delete(file); return "Host uploaded clip to Discord; local copy removed."; }
                            catch { return "Host uploaded clip to Discord; local copy could not be removed."; }
                        });
                        EndOutgoing("Host uploaded clip to Discord; removing local copy.", true);
                    }
                    else EndOutgoing("Host uploaded clip to Discord; local copy retained.", true);
                }
                else if (p[2] == "0") EndOutgoing("Host declined or could not deliver clip; local copy retained.");
                return;
            }
            int offset;
            if (p[0] != "A" || !int.TryParse(p[2], out offset) || offset != sent) return;
            acknowledged = offset; awaitingOffer = false;
            if (sent == outgoing.Length) { waitingResult = true; outgoingDeadline = now + 90; }
            else outgoingDeadline = now + 15;
        }
        private static void Send(ZRpc rpc, string packet) { rpc.Invoke(Rpc, new object[] { packet }); }
        internal bool DeliveryPeerConnected { get { return deliveryPeer != null && registered.Contains(deliveryPeer) && deliveryPeer.IsConnected(); } }
        internal void StopSending() { if (target != null) EndOutgoing("Client relay disabled; local clip retained."); }
        private void EndOutgoing(string reason, bool success = false)
        {
            var completed = outgoingCompletion; outgoingCompletion = null;
            outgoing = null; target = null; outgoingId = null; outgoingFile = null; log(reason);
            try { completed?.Invoke(success); } catch { log("Clip completion observer unavailable."); }
        }
        private void Reset()
        {
            if (target != null) EndOutgoing("Session changed; local clip retained.");
            incoming = null; source = null; registered.Clear(); nextOffer.Clear();
        }
        public void Dispose() { disposed = true; Reset(); session = null; }
    }
}
