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
        private const string Rpc = "ValheimMoments_ClipRelay_v2";
        private readonly Func<string, bool> allow;
        private readonly Func<int> limit;
        private readonly Action<RelayFile, string, Action<RelayOutcome>> deliver;
        private readonly string directory;
        private readonly Action<string> log;
        private readonly Func<ZRpc, bool> allowPeer;
        private readonly Func<ZRpc, string, double, bool> acceptEvent;
        private Action<RelayOutcome> outgoingCompletion;
        private readonly HashSet<ZRpc> registered = new HashSet<ZRpc>();
        private readonly Dictionary<ZRpc, double> nextOffer = new Dictionary<ZRpc, double>();
        private ZNet session;
        private ZRpc source, target, deliveryPeer;
        private RelayFile incoming;
        private RelayFile delivery;
        private int outgoingSize;
        private Task<int> preparation;
        private Task<byte[]> reading;
        private Task<string> cleanup;
        private string outgoingFile;
        private bool keepOutgoing;
        private string offeredKind, offeredMessage;
        private string offeredEventId;
        private bool offeredFirst;
        private string outgoingId;
        private int sent, acknowledged;
        private double now, incomingDeadline, incomingEnd, outgoingDeadline, nextSend, nextTick;
        private bool delivering, awaitingOffer, waitingResult, disposed;

        internal ClipRelay(Func<string, bool> allow, Func<int> limit, Action<RelayFile, string, Action<RelayOutcome>> deliver, Action<string> log, Func<ZRpc, bool> allowPeer = null, Func<ZRpc, string, double, bool> acceptEvent = null, string directory = null)
        { this.allow = allow; this.limit = limit; this.deliver = deliver; this.log = log; this.allowPeer = allowPeer ?? (rpc => true); this.acceptEvent = acceptEvent ?? ((rpc, kind, now) => true); this.directory = directory ?? Path.Combine(Path.GetTempPath(), "ValheimMoments-Relay"); }

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
            if (source != null && (!live.Contains(source) || now > incomingDeadline || now > incomingEnd))
            {
                if (live.Contains(source) && incoming != null) Send(source, "R|" + incoming.Id + "|0");
                incoming?.Dispose(); incoming = null; source = null;
            }
            PollIncoming();
            if (target != null && (!live.Contains(target) || now > outgoingDeadline)) EndInterrupted("Transfer ended or host unavailable; local clip retained.");
            if (preparation != null && preparation.IsCompleted)
            {
                var ready = preparation; preparation = null;
                try
                {
                    int size = ready.GetAwaiter().GetResult();
                    if (target != null)
                    {
                        if (size == 0) { EndOutgoing("Clip could not be read or exceeds 10 MiB; local copy retained."); return; }
                        outgoingSize = size; outgoingDeadline = now + 10;
                        Send(target, "B|" + outgoingId + "|" + offeredKind + "|" + size + "|" + RelayProtocol.Text(offeredMessage) + "|" + (offeredEventId ?? "-") + "|" + (offeredFirst ? "1" : "0"));
                    }
                }
                catch { EndOutgoing("Clip could not be read or exceeds 10 MiB; local copy retained."); }
            }
            if (outgoingSize > 0 && !awaitingOffer && !waitingResult && sent == acknowledged && now >= nextSend)
            {
                try
                {
                    if (reading == null) { reading = RelayFile.ReadChunk(outgoingFile, sent, outgoingSize); return; }
                    if (!reading.IsCompleted) return;
                    var bytes = reading.GetAwaiter().GetResult(); reading = null;
                    if (bytes == null || bytes.Length == 0) { EndOutgoing("Clip could not be read; local copy retained."); return; }
                    int read = bytes.Length;
                    int offset = sent; sent += read; nextSend = now + 0.05;
                    Send(target, "C|" + outgoingId + "|" + offset + "|" + Convert.ToBase64String(bytes));
                }
                catch { EndInterrupted("Clip transfer failed; local copy retained."); }
            }
        }

        internal bool Offer(ZNet capturedSession, string file, string kind, string message, bool saveLocalCopy = true, Action<RelayOutcome> completed = null, string eventId = null, bool personalFirst = false)
        {
            if (disposed || session == null || !ReferenceEquals(session, capturedSession) || session.IsServer() || outgoingSize > 0 || preparation != null || cleanup != null || target != null) return false;
            if (!RelayProtocol.ValidKind(kind) || message == null || message.Length > 2000 || (eventId != null && !HighlightDirector.ValidId(eventId))) return false;
            var peer = session.GetServerPeer();
            if (peer == null || !registered.Contains(peer.m_rpc)) return false;
            try
            {
                target = peer.m_rpc; outgoingId = Guid.NewGuid().ToString("N");
                outgoingCompletion = completed;
                outgoingFile = file; keepOutgoing = saveLocalCopy;
                offeredKind = kind; offeredMessage = message;
                offeredEventId = eventId; offeredFirst = personalFirst;
                sent = acknowledged = 0; awaitingOffer = true; waitingResult = false; outgoingDeadline = now + 10;
                preparation = RelayFile.Inspect(file);
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
            if (p[0] == "B" && p.Length == 7)
            {
                double next;
                if (nextOffer.TryGetValue(rpc, out next) && now < next) { Send(rpc, "R|" + p[1] + "|0"); return; }
                nextOffer[rpc] = now + 15;
                int size;
                if (source != null || delivering || !allowPeer(rpc) || !RelayProtocol.ValidKind(p[2]) || !allow(p[2]) || !int.TryParse(p[3], out size)) { Send(rpc, "R|" + p[1] + "|0"); return; }
                string message = RelayProtocol.ReadText(p[4]);
                if (p[6] != "0" && p[6] != "1") { Send(rpc, "R|" + p[1] + "|0"); return; }
                try { incoming = new RelayFile(directory, p[1], p[2], message, size, limit(), p[5] == "-" ? null : p[5], p[6] == "1"); }
                catch { Send(rpc, "R|" + p[1] + "|0"); return; }
                if (!acceptEvent(rpc, p[2], now)) { incoming.Dispose(); incoming = null; Send(rpc, "R|" + p[1] + "|0"); return; }
                source = rpc; incomingDeadline = now + 30;
                incomingEnd = now + 30 + Math.Ceiling(size / (double)RelayProtocol.ChunkBytes) * 0.5;
                Send(rpc, "A|" + p[1] + "|0");
            }
            else if (p[0] == "C" && p.Length == 4 && ReferenceEquals(source, rpc) && incoming != null && incoming.Id == p[1])
            {
                int offset;
                if (!allowPeer(rpc) || !allow(incoming.Kind) || !int.TryParse(p[2], out offset) || !incoming.BeginAdd(offset, Convert.FromBase64String(p[3])))
                { Send(rpc, "R|" + p[1] + "|0"); incoming.Dispose(); incoming = null; source = null; }
            }
        }
        private void PollIncoming()
        {
            if (incoming == null || source == null) return;
            int state = incoming.Poll();
            if (state == 0) return;
            var rpc = source;
            if (state < 0 || !allowPeer(rpc) || !allow(incoming.Kind))
            { Send(rpc, "R|" + incoming.Id + "|0"); incoming.Dispose(); incoming = null; source = null; return; }
            incomingDeadline = now + 30;
            if (state == 1) { Send(rpc, "A|" + incoming.Id + "|" + incoming.Received); return; }
            var complete = incoming; incoming = null; source = null;
            delivering = true; deliveryPeer = rpc; delivery = complete;
            ZNet origin = session;
            string recorder = "Connected player";
            foreach (var peer in session.GetPeers()) if (ReferenceEquals(peer.m_rpc, rpc)) { recorder = peer.m_playerName; break; }
            Send(rpc, "A|" + complete.Id + "|" + complete.Received);
            try { deliver(complete, recorder, outcome => {
                complete.Dispose();
                if (!disposed && ReferenceEquals(origin, session) && ReferenceEquals(delivery, complete))
                { delivering = false; deliveryPeer = null; delivery = null; if (registered.Contains(rpc)) Send(rpc, "R|" + complete.Id + "|" + ((int)outcome).ToString()); }
            }); }
            catch { complete.Dispose(); delivering = false; deliveryPeer = null; delivery = null; Send(rpc, "R|" + complete.Id + "|0"); }
        }
        private void ReceiveClient(string[] p)
        {
            if (outgoingSize == 0 || p.Length != 3) return;
            if (p[0] == "R")
            {
                if (p[2] == "1")
                {
                    // Only a matching host result AFTER the complete transfer can remove our file.
                    if (!waitingResult || acknowledged != outgoingSize) return;
                    if (!keepOutgoing)
                    {
                        string file = outgoingFile;
                        cleanup = Task.Run(() => {
                            try { File.Delete(file); return "Host uploaded clip to Discord; local copy removed."; }
                            catch { return "Host uploaded clip to Discord; local copy could not be removed."; }
                        });
                        EndOutgoing("Host uploaded clip to Discord; removing local copy.", RelayOutcome.Uploaded);
                    }
                    else EndOutgoing("Host uploaded clip to Discord; local copy retained.", RelayOutcome.Uploaded);
                }
                else if (p[2] == "0") EndOutgoing("Host declined or could not deliver clip; local copy retained.");
                else if (p[2] == "2") EndOutgoing("Host omitted this perspective; local copy retained.", RelayOutcome.Omitted);
                else if (p[2] == "3" && waitingResult) EndOutgoing("Delivery unknown; check Discord before retrying. Local clip retained.", RelayOutcome.Unknown);
                return;
            }
            int offset;
            if (p[0] != "A" || !int.TryParse(p[2], out offset) || offset != sent) return;
            acknowledged = offset; awaitingOffer = false;
            if (sent == outgoingSize) { waitingResult = true; outgoingDeadline = now + 90; }
            else outgoingDeadline = now + 30;
        }
        private static void Send(ZRpc rpc, string packet) { rpc.Invoke(Rpc, new object[] { packet }); }
        internal bool DeliveryPeerConnected { get { return deliveryPeer != null && registered.Contains(deliveryPeer) && deliveryPeer.IsConnected(); } }
        internal void StopSending() { if (target != null) EndInterrupted("Client relay disabled; local clip retained."); }
        private void EndInterrupted(string reason)
        {
            bool uncertain = outgoingSize > 0 && sent == outgoingSize;
            EndOutgoing(reason + (uncertain ? " Delivery unknown; check Discord before retrying." : ""), uncertain ? RelayOutcome.Unknown : RelayOutcome.Failed);
        }
        private void EndOutgoing(string reason, RelayOutcome outcome = RelayOutcome.Failed)
        {
            var completed = outgoingCompletion; outgoingCompletion = null;
            outgoingSize = 0; reading = null; target = null; outgoingId = null; outgoingFile = null; log(reason);
            try { completed?.Invoke(outcome); } catch { log("Clip completion observer unavailable."); }
        }
        private void Reset()
        {
            if (target != null) EndInterrupted("Session changed; local clip retained.");
            incoming?.Dispose(); incoming = null; source = null; delivering = false; deliveryPeer = null; delivery = null; registered.Clear(); nextOffer.Clear();
        }
        public void Dispose() { disposed = true; Reset(); session = null; }
    }
}
