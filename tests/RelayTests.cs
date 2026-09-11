using System;
using System.Collections.Generic;
using System.IO;
using ValheimMoments;

public class ZNetPeer
{
    public ZRpc m_rpc;
    public string m_playerName = "Ragnar";
    public bool IsReady() { return m_rpc.Connected; }
}
public class ZRpc
{
    public bool Connected = true;
    public ZRpc Other;
    public ZNet World;
    public readonly Dictionary<string, Action<ZRpc, string>> Handlers = new Dictionary<string, Action<ZRpc, string>>();
    public readonly Queue<Action> Queue = new Queue<Action>();
    public bool IsConnected() { return Connected; }
    public void Register<T>(string name, Action<ZRpc, T> method) { Handlers[name] = (rpc, text) => method(rpc, (T)(object)text); }
    public void Invoke(string name, object[] args)
    {
        Other.Queue.Enqueue(() => { ZNet.instance = Other.World; Action<ZRpc, string> method;
            if (Other.Connected && Other.Handlers.TryGetValue(name, out method)) method(Other, (string)args[0]); });
    }
    public void Drain() { while (Queue.Count > 0) Queue.Dequeue()(); }
}
internal static class RelayTests
{
    private static int checks;
    private static void Check(bool pass, string label) { if (!pass) throw new Exception(label); checks++; }
    private static byte[] Container(int size)
    {
        var bytes = new byte[size];
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(size - 8), 0, bytes, 4, 4);
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("WEBPJUNK"), 0, bytes, 8, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(size - 20), 0, bytes, 16, 4);
        return bytes;
    }
    internal static void Run()
    {
        string id = Guid.NewGuid().ToString("N");
        Check(!RelayProtocol.ValidId("../../clip") && RelayProtocol.ValidId(id), "Untrusted IDs cannot supply paths");
        Check(!RelayProtocol.ValidKind("webhook") && RelayProtocol.ValidKind("loot"), "Only fixed event routes accepted");
        Check(RelayProtocol.ReadText(RelayProtocol.Text("# Loot\nSword")) == "# Loot\nSword", "Bounded caption round trip");
        Check(RelayProtocol.ReadText("!") == null && RelayProtocol.ReadText(RelayProtocol.Text(new string('x', 2001))) == null, "Malformed and oversized caption rejected");
        bool threw = false;
        try { new RelayBuffer(id, "loot", "x", RelayProtocol.MaxBytes + 1, int.MaxValue); } catch (ArgumentException) { threw = true; }
        Check(threw, "Hard maximum enforced independently of host limit");
        var receiver = new RelayBuffer(id, "loot", "x", 20, 20);
        Check(!receiver.Add(1, new byte[1]) && receiver.Received == 0, "Out-of-order chunk cannot advance receive state");
        Check(!receiver.Add(0, new byte[21]) && !receiver.Complete, "Oversized chunk cannot overflow reservation");
        Check(receiver.Add(0, Container(20)) && receiver.ValidWebP(), "Complete structurally valid container accepted");
        Check(!receiver.Add(0, Container(20)), "Duplicate chunk cannot double count");
        receiver.Bytes[4]++;
        Check(!receiver.ValidWebP(), "RIFF length mismatch rejected");
        receiver.Bytes[4]--; receiver.Bytes[16] = 255;
        Check(!receiver.ValidWebP(), "Truncated RIFF chunk rejected");
        var host = new ZNet { Server = true }; var client = new ZNet();
        var hostRpc = new ZRpc { World = host }; var clientRpc = new ZRpc { World = client };
        hostRpc.Other = clientRpc; clientRpc.Other = hostRpc;
        host.Peers.Add(new ZNetPeer { m_rpc = hostRpc }); client.Peers.Add(new ZNetPeer { m_rpc = clientRpc });
        bool enabled = true, admitDeath = true; int delivered = 0; var logs = new List<string>(); Action<RelayOutcome> finish = null;
        var completions = new List<RelayOutcome>();
        RelayFile received = null;
        var serverRelay = new ClipRelay(kind => enabled, () => RelayProtocol.MaxBytes, (clip, recorder, done) => { delivered++; received = clip; finish = done; }, logs.Add,
            acceptEvent: (peer, kind, now) => kind != "death" || admitDeath);
        var clientRelay = new ClipRelay(kind => false, () => 0, (clip, recorder, done) => { throw new Exception("Client must never receive clips for upload"); }, logs.Add);
        string path = Path.Combine(Path.GetTempPath(), "valheim-relay-test-" + id + ".webp");
        byte[] content = Container(40000); File.WriteAllBytes(path, content);
        double time = 0;
        Action tick = () => {
            System.Threading.Thread.Sleep(1); // Allow the real background file reader to complete.
            time += 0.1; ZNet.instance = host; serverRelay.Tick(time); hostRpc.Drain();
            ZNet.instance = client; clientRelay.Tick(time); clientRpc.Drain();
        };
        try
        {
            tick(); ZNet.instance = client;
            Check(clientRelay.Offer(client, path, "loot", "# Great loot!", completed: completions.Add, eventId: id, personalFirst: true), "Connected client offers file without webhook metadata");
            string offeredId = (string)typeof(ClipRelay).GetField("outgoingId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(clientRelay);
            clientRpc.Handlers["ValheimMoments_ClipRelay_v2"](clientRpc, "R|" + offeredId + "|1");
            Check(completions.Count == 0, "Early host success cannot announce upload before transfer completes");
            for (int i = 0; i < 100 && delivered == 0; i++) tick();
            Check(delivered == 1 && received.Complete && new FileInfo(received.FilePath).Length == 40000 && received.Kind == "loot", "Paced multi-chunk disk transfer delivers exactly once");
            Check(received.Message == "# Great loot!" && serverRelay.DeliveryPeerConnected, "Caption retained and delivery bound to peer");
            Check(received.EventId == id && received.PersonalFirst, "Shared occurrence and personal-first metadata reach host unchanged");
            Check(completions.Count == 0, "Complete transfer does not announce upload while Discord is pending");
            ZNet.instance = host; finish(RelayOutcome.Uploaded); clientRpc.Drain();
            Check(logs.Exists(s => s.StartsWith("Host uploaded")) && File.Exists(path), "Success acknowledgment retains client original");
            Check(completions.Count == 1 && completions[0] == RelayOutcome.Uploaded, "Final host delivery invokes structured success exactly once");
            ZNet.instance = host; finish(RelayOutcome.Uploaded); clientRpc.Drain();
            Check(completions.Count == 1, "Duplicate host result cannot duplicate completion");
            var previousFinish = finish;
            for (int i = 0; i < 160; i++) tick();
            ZNet.instance = client;
            Check(clientRelay.Offer(client, path, "loot", "# Loot", false), "Client can request deletion after confirmed delivery");
            for (int i = 0; i < 20; i++) tick();
            Check(File.Exists(path) && delivered == 2, "Original retained while host upload is pending");
            previousFinish(RelayOutcome.Uploaded);
            Check(serverRelay.DeliveryPeerConnected, "Stale callback cannot clear a newer upload from the same peer");
            ZNet.instance = host; finish(RelayOutcome.Uploaded); clientRpc.Drain();
            for (int i = 0; i < 20; i++) tick();
            Check(!File.Exists(path), "Confirmed successful relay removes original when keep option is off");
            File.WriteAllBytes(path, content);
            for (int i = 0; i < 160; i++) tick();
            enabled = false; ZNet.instance = client;
            Check(clientRelay.Offer(client, path, "manual", "x", false, completions.Add), "Client can ask host without consulting local Discord settings");
            tick(); tick();
            Check(delivered == 2 && File.Exists(path) && logs.Exists(s => s.StartsWith("Host declined")), "Host refusal does not delete the original");
            Check(completions.Count == 2 && completions[1] == RelayOutcome.Failed, "Host rejection reports failure, never an upload notification");
            enabled = true;
            for (int i = 0; i < 160; i++) tick();
            admitDeath = false; ZNet.instance = client;
            Check(clientRelay.Offer(client, path, "death", "death", false, completions.Add), "Client may offer death metadata");
            for (int i = 0; i < 20; i++) tick();
            Check(delivered == 2 && File.Exists(path) && completions.Count == 3 && completions[2] == RelayOutcome.Failed, "Host death quota rejects before any delivery and preserves client original");
            foreach (var outcome in new[] { RelayOutcome.Unknown, RelayOutcome.Omitted })
            {
                for (int i = 0; i < 160; i++) tick();
                var reported = new List<RelayOutcome>(); int before = delivered;
                ZNet.instance = client;
                Check(clientRelay.Offer(client, path, "manual", "Memory", false, reported.Add), "Client offers clip for structured non-success result");
                for (int i = 0; i < 100 && delivered == before; i++) tick();
                Check(delivered == before + 1 && reported.Count == 0, "Delivery waits for final service outcome");
                ZNet.instance = host; finish(outcome); clientRpc.Drain();
                Check(reported.Count == 1 && reported[0] == outcome && File.Exists(path), "Unknown/omitted results stay distinct and preserve original");
            }
            for (int i = 0; i < 160; i++) tick();
            var interrupted = new List<RelayOutcome>(); int pendingDelivery = delivered;
            ZNet.instance = client; clientRelay.Offer(client, path, "manual", "Memory", false, interrupted.Add);
            for (int i = 0; i < 100 && delivered == pendingDelivery; i++) tick();
            Check(delivered == pendingDelivery + 1, "Complete transfer awaits service result before disconnect");
            clientRpc.Connected = false; tick();
            Check(interrupted.Count == 1 && interrupted[0] == RelayOutcome.Unknown && File.Exists(path), "Lost final result reports unknown after all bytes were sent");
            ZNet.instance = host; finish(RelayOutcome.Unknown); clientRpc.Drain(); clientRpc.Connected = true;
            for (int i = 0; i < 160; i++) tick();
            ZNet.instance = client; clientRelay.Offer(client, path, "manual", "x", completed: completions.Add);
            clientRpc.Connected = false; tick();
            Check(File.Exists(path) && logs.Exists(s => s.StartsWith("Transfer ended")), "Disconnected transfer retains local clip");
            Check(completions.Count == 4 && completions[3] == RelayOutcome.Failed, "Disconnect completes pending observer with failure");
            clientRpc.Connected = true;
            ZNet.instance = new ZNet(); clientRelay.Tick(time + 1);
            Check(!clientRelay.Offer(client, path, "loot", "x"), "Old-world capture cannot be relayed in a new session");
        }
        finally { serverRelay.Dispose(); clientRelay.Dispose(); ZNet.instance = null; File.Delete(path); }
        Console.WriteLine("PASS: " + checks + " relay protocol and simulated direct-peer transfer assertions.");
    }
}
