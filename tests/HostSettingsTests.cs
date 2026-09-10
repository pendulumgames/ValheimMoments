using System;
using System.IO;
using BepInEx.Configuration;
using ValheimMoments;

internal static class HostSettingsTests
{
    private static int checks;
    private static void Check(bool pass, string label) { if (!pass) throw new Exception(label); checks++; }
    private static ConfigEntry<T> Add<T>(HostConfiguration policy, ConfigFile file, string section, string key, T value, out HostConfiguration.ManagerAttributes tags)
    {
        tags = new HostConfiguration.ManagerAttributes();
        var entry = file.Bind(section, key, value, new ConfigDescription("test", SettingRanges.For(section, key, value), tags));
        policy.Register(entry, tags); return entry;
    }
    internal static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "valheim-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var host = new ZNet { Server = true }; var client = new ZNet();
        var hostRpc = new ZRpc { World = host }; var clientRpc = new ZRpc { World = client };
        hostRpc.Other = clientRpc; clientRpc.Other = hostRpc;
        host.Peers.Add(new ZNetPeer { m_rpc = hostRpc }); client.Peers.Add(new ZNetPeer { m_rpc = clientRpc });
        var hostPolicy = new HostConfiguration(null, null, null, s => { });
        var clientPolicy = new HostConfiguration(e => { }, null, null, s => { });
        var hostFile = new ConfigFile(Path.Combine(folder, "host.cfg"), false);
        var clientFile = new ConfigFile(Path.Combine(folder, "client.cfg"), false);
        HostConfiguration.ManagerAttributes unused, ruleTags, localTags, secretTags;
        var hostRule = Add(hostPolicy, hostFile, "Boss Kill", "FirstKillOnly", true, out unused);
        var localRule = Add(clientPolicy, clientFile, "Boss Kill", "FirstKillOnly", false, out ruleTags);
        Add(hostPolicy, hostFile, "Capture", "FPS", 15, out unused);
        var localFPS = Add(clientPolicy, clientFile, "Capture", "FPS", 30, out localTags);
        Add(hostPolicy, hostFile, "Discord", "WebhookURL", "HOST_SECRET_SENTINEL", out unused);
        Add(clientPolicy, clientFile, "Discord", "WebhookURL", "CLIENT_PRIVATE_VALUE", out secretTags);
        var hostPre = Add(hostPolicy, hostFile, "Capture", "PreEventSeconds", 5.0, out unused);
        var localPre = Add(clientPolicy, clientFile, "Capture", "PreEventSeconds", 2.0, out unused);
        double time = 0;
        Action tick = () => {
            time += 0.3; ZNet.instance = host; hostPolicy.Tick(time); hostRpc.Drain();
            ZNet.instance = client; clientPolicy.Tick(time); clientRpc.Drain();
        };
        try
        {
            string exported = hostPolicy.Export();
            string payloadText = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(exported));
            Check(!payloadText.Contains("SECRET") && !payloadText.Contains("WebhookURL") && !payloadText.Contains("FPS"), "Secret and client-local entries never enter host policy");
            ZNet.instance = client; clientPolicy.Tick(0);
            Check(!clientPolicy.Ready && ruleTags.ReadOnly == true && localTags.ReadOnly == false && secretTags.Browsable == false, "Join waits for host; UI locks rules but keeps personal settings editable");
            for (int i = 0; i < 12; i++) tick();
            Check(hostPolicy.PeerHasPolicy(hostRpc) && !hostPolicy.PeerHasPolicy(new ZRpc()), "Relay eligibility requires a recent settings exchange with the actual peer");
            Check(clientPolicy.Ready && clientPolicy.Get(localRule) && clientPolicy.Get(localPre) == 5, "Connected host policy applied with typed values");
            Check(!localRule.Value && localPre.Value == 2 && clientPolicy.Get(localFPS) == 30, "Overlay preserves client originals and local performance choice");
            localFPS.Value = 500;
            Check(localFPS.Value == 30, "BepInEx clamps excessive FPS on edit");
            clientFile.Save();
            Check(File.ReadAllText(clientFile.ConfigFilePath).Contains("FirstKillOnly = false"), "Saving client file never persists host rule over personal preference");
            ZNet.instance = host; hostRule.Value = false; hostPre.Value = 7;
            for (int i = 0; i < 12; i++) tick();
            Check(!clientPolicy.Get(localRule) && clientPolicy.Get(localPre) == 7, "Host changes propagate during the session");
            // A non-server peer cannot install a different policy on the client.
            var stranger = new ZRpc { World = client }; stranger.Other = new ZRpc { World = host };
            client.Peers.Add(new ZNetPeer { m_rpc = stranger });
            ZNet.instance = host; hostRule.Value = true;
            string forged = "S|" + hostPolicy.Export();
            ZNet.instance = client; time += 0.3; clientPolicy.Tick(time);
            stranger.Handlers["ValheimMoments_HostSettings_v1"](stranger, forged);
            Check(!clientPolicy.Get(localRule), "Non-host peer cannot override rules");
            ZNet.instance = host;
            hostRpc.Handlers["ValheimMoments_HostSettings_v1"](hostRpc, "S|" + clientPolicy.Export());
            Check(hostPolicy.Get(hostRule), "Server ignores client-authored settings packets");
            ZNet.instance = client;
            bool rejected = false; try { clientPolicy.Apply("malformed!"); } catch { rejected = true; }
            Check(rejected && clientPolicy.Get(localPre) == 7, "Invalid snapshot leaves previous policy intact");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1); writer.Write(2);
                writer.Write("Boss Kill\nFirstKillOnly"); writer.Write("true");
                writer.Write("Discord\nWebhookURL"); writer.Write("INJECTED_SECRET"); writer.Flush();
                rejected = false; try { clientPolicy.Apply(Convert.ToBase64String(stream.ToArray())); } catch { rejected = true; }
                Check(rejected && !clientPolicy.Get(localRule), "Snapshot cannot inject private settings or apply a partial update");
            }
            time += 11; clientPolicy.Tick(time);
            Check(!clientPolicy.Ready, "Missing host heartbeats stop policy-dependent capture");
            ZNet.instance = new ZNet { Server = true }; time += 1; clientPolicy.Tick(time);
            Check(clientPolicy.Ready && !clientPolicy.Get(localRule) && clientPolicy.Get(localPre) == 2 && ruleTags.ReadOnly == false, "Leaving for own world restores local settings and editing");
            Check((int)SettingRanges.For("Capture", "Width", 640).Clamp(1) == 16, "One-pixel width clamped");
            Check((int)SettingRanges.For("Capture", "Height", 360).Clamp(10000) == 1080, "Extreme height clamped");
            Check((int)SettingRanges.For("Capture", "WebPQuality", 80).Clamp(500) == 100, "Excessive quality clamped");
            Check((double)SettingRanges.For("Capture", "PreEventSeconds", 5.0).Clamp(double.NaN) == 5, "Nonfinite timing falls back safely");
        }
        finally
        {
            hostPolicy.Dispose(); clientPolicy.Dispose(); ZNet.instance = null;
            File.Delete(hostFile.ConfigFilePath); File.Delete(clientFile.ConfigFilePath); Directory.Delete(folder);
        }
        Console.WriteLine("PASS: " + checks + " host policy and BepInEx configuration assertions.");
    }
}
