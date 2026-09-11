using System;
using System.IO;
using BepInEx.Configuration;
using ValheimMoments;

internal static class HostSettingsTests
{
    private static int checks;
    private static void Check(bool pass, string label) { if (!pass) throw new Exception(label); checks++; }
    private static ConfigEntry<T> Add<T>(HostConfiguration policy, ConfigFile file, string section, string key, T value, out HostConfiguration.ConfigurationManagerAttributes tags)
    {
        tags = new HostConfiguration.ConfigurationManagerAttributes();
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
        HostConfiguration.ConfigurationManagerAttributes unused, ruleTags, localTags, secretTags;
        var hostRule = Add(hostPolicy, hostFile, "Boss Kill", "FirstKillOnly", true, out unused);
        var localRule = Add(clientPolicy, clientFile, "Boss Kill", "FirstKillOnly", false, out ruleTags);
        Add(hostPolicy, hostFile, "Capture", "FPS", 15, out unused);
        var localFPS = Add(clientPolicy, clientFile, "Capture", "FPS", 30, out localTags);
        Add(hostPolicy, hostFile, "Discord", "WebhookURL", "HOST_SECRET_SENTINEL", out unused);
        Add(clientPolicy, clientFile, "Discord", "WebhookURL", "CLIENT_PRIVATE_VALUE", out secretTags);
        var hostPre = Add(hostPolicy, hostFile, "Capture", "PreEventSeconds", 5.0, out unused);
        var localPre = Add(clientPolicy, clientFile, "Capture", "PreEventSeconds", 2.0, out unused);
        var hostDiscord = Add(hostPolicy, hostFile, "Discord", "Enabled", false, out unused);
        var clientDiscord = Add(clientPolicy, clientFile, "Discord", "Enabled", true, out unused);
        Add(hostPolicy, hostFile, "Capture", "SaveLocalCopy", false, out unused);
        var clientSave = Add(clientPolicy, clientFile, "Capture", "SaveLocalCopy", true, out unused);
        Add(hostPolicy, hostFile, "Discord", "DiscoveryWebhookURL", "DISCOVERY_SECRET_SENTINEL", out unused);
        Add(clientPolicy, clientFile, "Discord", "DiscoveryWebhookURL", "LOCAL_DISCOVERY_SECRET", out unused);
        Add(hostPolicy, hostFile, "Discoveries", "Enabled", true, out unused);
        var clientDiscoveries = Add(clientPolicy, clientFile, "Discoveries", "Enabled", false, out unused);
        Add(hostPolicy, hostFile, "Special Enemies", "EnemyKeys", "$enemy_test", out unused);
        var clientEnemies = Add(clientPolicy, clientFile, "Special Enemies", "EnemyKeys", "local_choice", out unused);
        double time = 0;
        Action tick = () => {
            time += 0.3; ZNet.instance = host; hostPolicy.Tick(time); hostRpc.Drain();
            ZNet.instance = client; clientPolicy.Tick(time); clientRpc.Drain();
        };
        try
        {
            MigrationChecks(folder);
            string exported = hostPolicy.Export();
            string payloadText = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(exported));
            Check(!payloadText.Contains("SECRET") && !payloadText.Contains("WebhookURL") && !payloadText.Contains("FPS"), "Secret and client-local entries never enter host policy");
            ZNet.instance = client; clientPolicy.Tick(0);
            Check(!clientPolicy.Ready && ruleTags.ReadOnly == true && localTags.ReadOnly == false && secretTags.Browsable == false, "Join waits for host; UI locks rules but keeps personal settings editable");
            for (int i = 0; i < 12; i++) tick();
            Check(hostPolicy.PeerHasPolicy(hostRpc) && !hostPolicy.PeerHasPolicy(new ZRpc()), "Relay eligibility requires a recent settings exchange with the actual peer");
            Check(clientPolicy.Ready && clientPolicy.Get(localRule) && clientPolicy.Get(localPre) == 5, "Connected host policy applied with typed values");
            Check(clientPolicy.Get(clientDiscoveries) && clientPolicy.Get(clientEnemies) == "$enemy_test", "Discovery and enemy rules use host policy");
            Check(!HostConfiguration.IsLocal("Special Enemies", "LogEnemyKeys") && !HostConfiguration.IsLocal("Discoveries", "Enabled"), "New category controls and diagnostic remain host-owned");
            Check(!clientPolicy.Get(clientDiscord) && clientDiscord.Value && clientPolicy.Get(clientSave), "Host delivery off overrides client true; local saving remains independent");
            Check(string.CompareOrdinal(localTags.Category, ruleTags.Category) < 0, "Player category sorts ahead of host settings");
            Check(!localRule.Value && localPre.Value == 2 && clientPolicy.Get(localFPS) == 30, "Overlay preserves client originals and local performance choice");
            localFPS.Value = 500;
            Check(localFPS.Value == 30, "BepInEx clamps excessive FPS on edit");
            clientFile.Save();
            Check(File.ReadAllText(clientFile.ConfigFilePath).Contains("FirstKillOnly = false"), "Saving client file never persists host rule over personal preference");
            ZNet.instance = host; hostRule.Value = false; hostPre.Value = 7; hostDiscord.Value = true;
            for (int i = 0; i < 12; i++) tick();
            Check(!clientPolicy.Get(localRule) && clientPolicy.Get(localPre) == 7, "Host changes propagate during the session");
            Check(clientPolicy.Get(clientDiscord), "Host delivery enablement propagates");
            // A non-server peer cannot install a different policy on the client.
            var stranger = new ZRpc { World = client }; stranger.Other = new ZRpc { World = host };
            client.Peers.Add(new ZNetPeer { m_rpc = stranger });
            ZNet.instance = host; hostRule.Value = true;
            string forged = "S|" + hostPolicy.Export();
            ZNet.instance = client; time += 0.3; clientPolicy.Tick(time);
            stranger.Handlers["ValheimMoments_HostSettings_v2"](stranger, forged);
            Check(!clientPolicy.Get(localRule), "Non-host peer cannot override rules");
            ZNet.instance = host;
            hostRpc.Handlers["ValheimMoments_HostSettings_v2"](hostRpc, "S|" + clientPolicy.Export());
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
            Check((int)SettingRanges.For("Capture", "Width", 640).Clamp(1) == 480, "One-pixel width clamped");
            Check((int)SettingRanges.For("Capture", "Height", 360).Clamp(10000) == 1080, "Extreme height clamped");
            Check((int)SettingRanges.For("Capture", "WebPQuality", 80).Clamp(500) == 100, "Excessive quality clamped");
            Check((double)SettingRanges.For("Capture", "PreEventSeconds", 5.0).Clamp(double.NaN) == 5, "Nonfinite timing falls back safely");
            Check(HostConfiguration.IsLocal("Notifications", "SoundMode") && !HostConfiguration.IsLocal("Player Death", "CaptureLimit"), "Notification presentation local; death policy host-owned");
            Check((int)SettingRanges.For("Player Death", "CaptureLimit", 1).Clamp(int.MaxValue) == 20, "Death quota bounded");
            Check((double)SettingRanges.For("Player Death", "WindowSeconds", 60.0).Clamp(double.NaN) == 60, "Invalid death window falls back safely");
            Check((double)SettingRanges.For("Notifications", "Volume", 0.35).Clamp(500.0) == 1, "Notification volume bounded");
            int changed = 0;
            using (var notificationPolicy = new HostConfiguration(null, null, () => changed++, s => { }))
            {
                var volume = Add(notificationPolicy, clientFile, "Notifications", "Volume", 0.35, out unused);
                volume.Value = 0.5;
                Check(changed == 0, "Changing local notification volume does not invalidate capture buffers");
            }
        }
        finally
        {
            hostPolicy.Dispose(); clientPolicy.Dispose(); ZNet.instance = null;
            File.Delete(hostFile.ConfigFilePath); File.Delete(clientFile.ConfigFilePath); Directory.Delete(folder);
        }
        Console.WriteLine("PASS: " + checks + " host policy and BepInEx configuration assertions.");
    }
    private static void MigrationChecks(string folder)
    {
        string path = Path.Combine(folder, "migration.cfg");
        try
        {
            Check(new ConfigurationMigration(path).BossMode == BossCaptureMode.FirstKillThenRarity, "New install defaults to first kill then rarity");
            for (int flags = 0; flags < 8; flags++)
            {
                bool first = (flags & 1) != 0, filter = (flags & 2) != 0, bypass = (flags & 4) != 0;
                File.WriteAllText(path, "[Boss Kill]\nFirstKillOnly = " + first + "\nOnlyCaptureIfLootMeetsRarity = " + filter + "\nFirstKillBypassesRarity = " + bypass);
                var mode = new ConfigurationMigration(path).BossMode;
                foreach (bool isFirst in new[] { false, true })
                    foreach (bool qualifies in new[] { false, true })
                    {
                        bool oldAccept = (!first || isFirst) && (!filter || (isFirst && bypass) || qualifies);
                        bool newAccept = (!BossCaptureRules.FirstOnly(mode) || isFirst) &&
                            (!BossCaptureRules.UsesRarity(mode) || (isFirst && BossCaptureRules.FirstBypasses(mode)) || qualifies);
                        Check(oldAccept == newAccept, "Every old filter combination preserves eligibility");
                    }
            }
            File.WriteAllText(path, "[Discord]\nSaveLocalCopy = true\nUploadClips = true\nEnableClientRelay = true\n[Capture]\nWidth = 1280\n");
            var migration = new ConfigurationMigration(path);
            Check(migration.SaveLocalCopy && migration.CustomSize, "Existing save choice and custom dimensions detected");
            var file = new ConfigFile(path, false);
            var save = file.Bind("Capture", "SaveLocalCopy", migration.SaveLocalCopy);
            ConfigurationMigration.Retire(file);
            Check(save.Value && !File.ReadAllText(path).Contains("UploadClips") && !File.ReadAllText(path).Contains("EnableClientRelay"), "Retired bindings removed without losing retention choice");
            save.Value = false; file.Save();
            migration = new ConfigurationMigration(path);
            var reloaded = new ConfigFile(path, false);
            Check(!reloaded.Bind("Capture", "SaveLocalCopy", migration.SaveLocalCopy).Value, "New explicit value survives later migrations");
            ConfigurationMigration.Retire(reloaded);
            Check(!new ConfigFile(path, false).Bind("Capture", "SaveLocalCopy", true).Value, "Migration idempotent");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
