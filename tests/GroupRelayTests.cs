using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ValheimMoments;

internal static class GroupRelayTests
{
    private static int checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    private static byte[] Container()
    {
        var bytes = new byte[40000];
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length - 8), 0, bytes, 4, 4);
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("WEBPJUNK"), 0, bytes, 8, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length - 20), 0, bytes, 16, 4); return bytes;
    }
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "ValheimMoments-GroupTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); string occurrence = Guid.NewGuid().ToString("N");
        var host = new ZNet { Server = true }; var worlds = new[] { new ZNet(), new ZNet() };
        var hostRpcs = new ZRpc[2]; var clientRpcs = new ZRpc[2]; var clients = new ClipRelay[2];
        var outcomes = new List<RelayOutcome>[] { new List<RelayOutcome>(), new List<RelayOutcome>() };
        var files = new[] { Path.Combine(root, "host.webp"), Path.Combine(root, "one.webp"), Path.Combine(root, "two.webp") };
        foreach (string file in files) File.WriteAllBytes(file, Container());
        var receipt = new TaskCompletionSource<UploadResult>(); QueuedPerspective[] sent = null; int sends = 0, active = 0, maximumActive = 0, admitted = 0;
        double now = 0;
        var queue = new HostHighlightQueue(new HighlightDirector(3, 50000, 120000, 30), 120000,
            (batch, stop) => { sent = batch; sends++; return receipt.Task; }); queue.Reset(host);
        var server = new ClipRelay(_ => true, () => 50000, (file, name, done) => { throw new Exception("Queue bypassed"); }, _ => { }, directory: root);
        server.Collect = (file, name, peer, live, transfer, complete) => {
            var item = new QueuedPerspective { Offer = new HighlightOffer(file.Id, file.EventId, file.Kind, peer, name, file.Size, file.PersonalFirst),
                Message = file.Message, Keep = true, Eligible = live, Complete = complete, Release = file.Dispose,
                Transfer = done => transfer(path => { active--; done(path); }) };
            var actual = item.Transfer; item.Transfer = done => { bool started = actual(done); if (started) { active++; maximumActive = Math.Max(maximumActive, active); } return started; };
            bool accepted = queue.Offer(host, item, now) == HighlightAdmission.Accepted; if (accepted) admitted++; return accepted;
        };
        for (int i = 0; i < 2; i++)
        {
            hostRpcs[i] = new ZRpc { World = host }; clientRpcs[i] = new ZRpc { World = worlds[i] };
            hostRpcs[i].Other = clientRpcs[i]; clientRpcs[i].Other = hostRpcs[i];
            host.Peers.Add(new ZNetPeer { m_rpc = hostRpcs[i], m_playerName = "Authenticated " + i });
            worlds[i].Peers.Add(new ZNetPeer { m_rpc = clientRpcs[i] });
            clients[i] = new ClipRelay(_ => false, () => 0, (file, name, done) => { throw new Exception("Client upload"); }, _ => { }, directory: root);
        }
        Action tick = () => {
            Thread.Sleep(1); now += 0.05;
            ZNet.instance = host; server.Tick(now); foreach (var rpc in hostRpcs) rpc.Drain(); queue.Tick(host, now);
            for (int i = 0; i < 2; i++) { ZNet.instance = worlds[i]; clients[i].Tick(now); clientRpcs[i].Drain(); }
        };
        var localResult = new List<RelayOutcome>();
        try
        {
            queue.Offer(host, new QueuedPerspective { Offer = new HighlightOffer(Guid.NewGuid().ToString("N"), occurrence, "boss", long.MinValue, "Host", 40000, false),
                File = files[0], Keep = true, Message = "Boss", Eligible = () => true, Complete = localResult.Add }, 0);
            tick();
            for (int i = 0; i < 2; i++) { ZNet.instance = worlds[i]; Check(clients[i].Offer(worlds[i], files[i + 1], "boss", "Boss", false, outcomes[i].Add, occurrence, i == 1), "Client offers same shared boss occurrence"); }
            for (int i = 0; i < 10; i++) tick();
            Check(sends == 0 && active == 0, "Collection phase transfers no footage");
            for (int i = 0; i < 2000 && sends == 0; i++) tick();
            Check(admitted == 2 && sends == 1 && sent.Length == 3, "One selected group includes host and two clients");
            Check(maximumActive == 1, "Selected client transfers are serialized");
            Check(sent[1].Offer.Recorder.StartsWith("Authenticated ") && sent[2].Offer.Recorder.StartsWith("Authenticated "), "Recorder labels come from actual host peer records");
            Check(localResult.Count == 0 && outcomes[0].Count == 0 && outcomes[1].Count == 0, "Complete files still await Discord acknowledgement");
            receipt.SetResult(new UploadResult { Success = true });
            for (int i = 0; i < 200 && (outcomes[0].Count == 0 || outcomes[1].Count == 0 || File.Exists(files[1]) || File.Exists(files[2])); i++) tick();
            Check(localResult.Count == 1 && localResult[0] == RelayOutcome.Uploaded && outcomes[0][0] == RelayOutcome.Uploaded && outcomes[1][0] == RelayOutcome.Uploaded, "Every included recorder receives final uploaded result");
            Check(File.Exists(files[0]) && !File.Exists(files[1]) && !File.Exists(files[2]), "Each recording honors its own local retention choice");
            Check(sends == 1 && !queue.Busy, "No duplicate post or lingering queue reservation");
        }
        finally
        {
            queue.Stop(); server.Dispose(); foreach (var client in clients) client.Dispose(); ZNet.instance = null;
            foreach (string file in files) File.Delete(file);
            SpinWait.SpinUntil(() => Directory.GetFiles(root).Length == 0, 5000); Directory.Delete(root);
        }
        Console.WriteLine("PASS: " + checks + " three-player grouped relay integration assertions.");
    }
}
