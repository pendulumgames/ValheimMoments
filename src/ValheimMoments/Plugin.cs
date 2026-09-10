using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimMoments.Core;

namespace ValheimMoments
{
    [BepInPlugin("local.valheimmoments", "Valheim Moments", "0.11.1")]
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private sealed class ReadbackSlot
        {
            internal RenderTexture Target;
            internal NativeArray<byte> Pixels;
            internal AsyncGPUReadbackRequest Request;
            internal double Submitted;
            internal long SessionRevision;
        }

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Queue<ReadbackSlot> free = new Queue<ReadbackSlot>(3);
        private readonly Queue<ReadbackSlot> pending = new Queue<ReadbackSlot>(3);
        private readonly List<ReadbackSlot> allSlots = new List<ReadbackSlot>(3);
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private CaptureBuffer history;
        private CaptureSession captureSession;
        private HostConfiguration hostSettings;
        private bool captureSettingsDirty;
        private double captureSettingsChangedAt;
        private ConfigEntry<int> widthSetting, heightSetting, fpsSetting, qualitySetting, budgetSetting;
        private ConfigEntry<double> preSetting, postSetting, bossPostSetting, lootPostSetting;
        private ConfigEntry<bool> trackPeriodicSetting;
        private double appliedPre, appliedPost;
        private CaptureBuffer.Clip encodingClip;
        private Task<string> encoding;
        private Task<UploadResult> upload;
        private ConfigEntry<bool> discordEnabled, uploadClips, saveLocalCopy;
        private ConfigEntry<string> webhookUrl, discordUsername;
        private ConfigEntry<bool> useBossWebhook, useLootWebhook, useDeathWebhook;
        private ConfigEntry<string> bossWebhook, lootWebhook, deathWebhook;
        private ZNet pendingSession, activeSession, uploadSession;
        private bool pendingAsHost, activeAsHost;
        private string activeKind;
        private CancellationTokenSource uploadCancellation;
        private ClipRelay relay;
        private ConfigEntry<bool> relayEnabled;
        private Action<bool> relayCompletion;
        private string relayDirectory;
        private ConfigEntry<int> uploadLimitMiB;
        private ConfigEntry<bool> manualTrigger, deathTrigger, deathEnabled, includePlayerName, includeCause;
        private ConfigEntry<string> deathMessage, playerNameOverride;
        private Harmony deathHarmony;
        private Harmony bossHarmony;
        private ConfigEntry<bool> bossTrigger, bossEnabled, firstBossOnly;
        private ConfigEntry<string> bossMessage;
        private double bossPostSeconds;
        private ConfigEntry<BossNameMode> bossNameMode;
        private Harmony attributionHarmony;
        private Harmony periodicHarmony;
        private Harmony lootHarmony, worldHarmony, worldEpicHarmony;
        private readonly AcquisitionHighlights acquisitions = new AcquisitionHighlights();
        private ConfigEntry<bool> chestPickups, worldPickups;
        private ConfigEntry<string> pickupMessage;
        private ConfigEntry<bool> showBossLoot, showLootQuantity;
        private ConfigEntry<int> maxLootItems;
        private ConfigEntry<string> lootHeader;
        private BossKill pendingBoss, activeBoss;
        private ConfigEntry<bool> showRarity, showModifiers, showSockets, showUnidentified;
        private ConfigEntry<double> lootWaitSeconds;
        private double lootDeadline;
        private Harmony epicHarmony;
        private ConfigEntry<bool> filterBossLoot;
        private ConfigEntry<bool> firstKillBypassesRarity;
        private ConfigEntry<string> minimumBossRarity;
        private CaptureBuffer.Clip waitingForLoot;
        private readonly LootHighlights lootHighlights = new LootHighlights();
        private ConfigEntry<bool> lootTrigger, lootEnabled;
        private ConfigEntry<string> minimumLootRarity, highlightMessage;
        private ConfigEntry<double> highlightWaitSeconds;
        private double lootPostSeconds;
        private bool epicReady;
        private ConfigEntry<bool> highlightQuantity, highlightRarity, highlightModifiers, highlightSockets, highlightUnidentified;
        private ConfigEntry<int> highlightMaxItems;
        private ConfigEntry<string> highlightHeader;

        private void Start()
        {
            if (stopped) return;
            System.Reflection.Assembly epic = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if (assembly.GetName().Name == "EpicLoot") { epic = assembly; break; }
            try
            {
                worldEpicHarmony = new Harmony("local.valheimmoments.world.epic");
                WorldLootDetector.InstallEpic(epic, worldEpicHarmony);
                if (epic != null) Logger.LogInfo("[Loot] Epic Loot delayed chest provenance observer installed.");
            }
            catch (Exception error)
            {
                worldEpicHarmony?.UnpatchSelf();
                Logger.LogWarning("[Loot] Delayed chest provenance unavailable: " + error.GetType().Name);
            }
            if (!initialized) return;
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if (assembly.GetName().Name == "EpicLoot") { epic = assembly; break; }
                epicHarmony = new Harmony("local.valheimmoments.epicloot");
                epicReady = EpicLootAdapter.Install(epic, epicHarmony);
                Logger.LogInfo(epicReady ? "[Loot] Optional Epic Loot adapter installed." : "[Loot] Epic Loot absent; vanilla summaries enabled.");
            }
            catch (Exception error)
            {
                try { epicHarmony?.UnpatchSelf(); } catch { }
                Logger.LogWarning("[Loot] Epic Loot adapter unavailable: " + error.GetType().Name + ". Vanilla summaries remain enabled.");
            }
        }
        private string pendingKind = "manual", pendingMessage = "Valheim moment", activeMessage;
        private string pendingRecorder, activeRecorder;
        private RenderTexture screen;
        private byte[] scratch;
        private ConfigEntry<bool> captureEnabled, timing, flip;
        private ConfigEntry<KeyCode> captureKey, toggleKey;
        private int width, height, fps, quality;
        private string encoderPath, outputDirectory, activeOutput;
        private bool initialized, stopped, paused, historyCleared;
        private double nextCapture, lastReport, lastUpdate;
        private double submitMs, copyMs, latencyMs, maxSubmitMs, maxCopyMs, maxFrameMs, frameMs;
        private int submitted, received, skipped, errors, updateCount, consecutiveErrors;

        private ConfigEntry<T> Bind<T>(string section, string key, T value, string help)
        {
            return Bind(section, key, value, new ConfigDescription(help));
        }
        private ConfigEntry<T> Bind<T>(string section, string key, T value, ConfigDescription description)
        {
            var tags = new HostConfiguration.ManagerAttributes();
            var allTags = new List<object>(description.Tags); allTags.Add(tags);
            var entry = Config.Bind(section, key, value, new ConfigDescription(description.Description,
                SettingRanges.For(section, key, value) ?? description.AcceptableValues, allTags.ToArray()));
            hostSettings.Register(entry, tags);
            return entry;
        }
        private T Value<T>(ConfigEntry<T> entry) { return hostSettings.Get(entry); }
        private void RefreshConfigurationManager()
        {
            foreach (var plugin in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                var instance = plugin.Instance;
                if (instance != null && instance.GetType().FullName == "ConfigurationManager.ConfigurationManager")
                    instance.GetType().GetMethod("BuildSettingList")?.Invoke(instance, null);
            }
        }

        private void Awake()
        {
            try
            {
                hostSettings = new HostConfiguration(entry => GUILayout.Label(hostSettings.Display(entry)),
                    RefreshConfigurationManager, () => { captureSettingsDirty = true; captureSettingsChangedAt = clock.Elapsed.TotalSeconds; }, message => Logger.LogInfo("[Settings] " + message));
                captureEnabled = Bind("Capture", "Enabled", true, "Enable recording. F9 toggles recording for baseline comparison.");
                captureKey = Bind("Capture", "ManualCaptureKey", KeyCode.F10, "Save recent gameplay plus post-event footage locally.");
                toggleKey = Bind("Capture", "ToggleCaptureKey", KeyCode.F9, "Pause/resume recording to compare game performance.");
                timing = Bind("Debug", "LogCaptureTiming", false,
                    new ConfigDescription("Troubleshooting: log aggregate CPU timing, readback latency and frame counts every 10 seconds.", null, "Advanced"));
                flip = Bind("Capture", "FlipVertically", false, "Enable if the test WebP is upside down on your graphics backend.");
                discordEnabled = Bind("Discord", "Enabled", false, "Host/single-player only: enable Discord delivery. Remote clients send clips to the host and never use local webhook settings.");
                relayEnabled = Bind("Discord", "EnableClientRelay", true, "Host: accept clips from connected clients. Client: allow sending clips to the host. Both sides need this version. Host Discord.Enabled and event trigger switches also apply. Successful client uploads follow the recording player's SaveLocalCopy setting.");
                uploadClips = Bind("Discord", "UploadClips", true, "Upload newly completed clips when Discord is enabled.");
                saveLocalCopy = Bind("Discord", "SaveLocalCopy", false, "Keep successfully uploaded clips locally. When false, delete after Discord success or the host's successful relay confirmation. Failed/skipped uploads retain the clip. Applies to this recording player.");
                webhookUrl = Bind("Discord", "WebhookURL", "", "Secret: enter locally, never share this config. HTTPS Discord webhook; optional thread_id query.");
                discordUsername = Bind("Discord", "Username", "Valheim Moments", "Host/single-player only: bot display name, 1–80 characters. Remote client values are ignored.");
                useBossWebhook = Bind("Discord", "UseBossKillWebhook", false, "Host only: route boss clips to BossKillWebhookURL; when off, use WebhookURL.");
                bossWebhook = Bind("Discord", "BossKillWebhookURL", "", "Host-only secret: optional boss destination. An enabled but invalid override keeps the clip locally; it does not silently change channels.");
                useLootWebhook = Bind("Discord", "UseGoodLootWebhook", false, "Host only: route loot clips to GoodLootWebhookURL; when off, use WebhookURL.");
                lootWebhook = Bind("Discord", "GoodLootWebhookURL", "", "Host-only secret: optional loot destination.");
                useDeathWebhook = Bind("Discord", "UsePlayerDeathWebhook", false, "Host only: route death clips to PlayerDeathWebhookURL; when off, use WebhookURL.");
                deathWebhook = Bind("Discord", "PlayerDeathWebhookURL", "", "Host-only secret: optional player-death destination.");
                uploadLimitMiB = Bind("Discord", "MaxUploadMiB", 10, "Per-file upload guard. Discord can impose its own limit. Allowed range 1–100.");
                manualTrigger = Bind("Triggers", "ManualCapture", true, "Enable the manual hotkey independently of automatic events.");
                deathTrigger = Bind("Triggers", "PlayerDeath", true, "Enable local player death captures.");
                deathEnabled = Bind("Player Death", "Enabled", true, "Enable this event. Triggers.PlayerDeath must also be enabled.");
                deathMessage = Bind("Player Death", "Message", "\uD83D\uDC80 {player} died!", "Discord death message. Placeholders: {player}, {cause}. Cause appends automatically when enabled and no placeholder is present.");
                includeCause = Bind("Player Death", "IncludeCause", true, "Include the recorded attacker or environmental cause; unknown when unavailable.");
                includePlayerName = Bind("Player Death", "IncludePlayerName", true, "Replace {player} with the character name/override; otherwise use A player.");
                playerNameOverride = Bind("Player Death", "PlayerNameOverride", "", "Optional display name instead of the character name.");
                bossTrigger = Bind("Triggers", "BossKill", true, "Capture boss kills credited by Valheim to this character.");
                bossEnabled = Bind("Boss Kill", "Enabled", true, "Enable boss capture; Triggers.BossKill must also be enabled.");
                trackPeriodicSetting = Bind("Boss Kill", "TrackPeriodicDamage", true, "Track actual Spirit/fire/poison effects for final-blow attribution. Mixed or unknown sources remain unavailable. Requires the mod on the creature owner. Restart after changing.");
                firstBossOnly = Bind("Boss Kill", "FirstKillOnly", false, "Only capture when this character has no previous kill of this boss in saved game statistics.");
                bossMessage = Bind("Boss Kill", "Message", "\uD83C\uDFC6 {boss} defeated!", "Discord boss message. Supported placeholders: {boss}, {player}. Loot placeholders: {loot}, {item_count}.");
                bossPostSetting = Bind("Boss Kill", "PostEventSeconds", 4.0, "Seconds to record after a credited boss kill, to show loot dropping. Restart after changing. Longer clips must fit Capture.MemoryBudgetMiB and the encoder's 256 MiB raw-frame limit.");
                bossNameMode = Bind("Boss Kill", "PlayerNameMode", BossNameMode.Both, "KillCredit, FinalBlow or Both. Kill credit names this character; final blow names the last-hit player when available. Co-op final-blow sharing requires this version on the client owning the boss. Templates support {credit} and {killer}; selected names append when placeholders are absent.");
                showBossLoot = Bind("Boss Kill", "ShowLoot", true, "Show observed vanilla rolls and completed Epic Loot drops when its optional adapter is available.");
                showLootQuantity = Bind("Boss Kill", "ShowQuantity", true, "Show item quantities in the boss loot summary.");
                maxLootItems = Bind("Boss Kill", "MaxLootItemsShown", 5, "Maximum entries shown, 1-20; highest verified rarity first, then name. Distinct magic items stay separate.");
                lootHeader = Bind("Boss Kill", "LootHeader", "Generated loot:", "Message placeholders {loot} and {item_count}; count is displayed-data entries before the display limit (grouped vanilla types and individual Epic Loot items).");
                showRarity = Bind("Boss Kill", "ShowRarity", true, "Show verified rarity names and approximate color emojis.");
                showModifiers = Bind("Boss Kill", "ShowItemModifiers", true, "Show Epic Loot's formatted modifiers for identified items.");
                showSockets = Bind("Boss Kill", "ShowItemSockets", true, "Show verified socket counts for identified items.");
                showUnidentified = Bind("Boss Kill", "ShowUnidentifiedStatus", true, "Label unidentified items. Hidden modifiers are never exposed.");
                lootWaitSeconds = Bind("Boss Kill", "LootWaitSeconds", 12.0, "Maximum seconds from boss kill to wait for delayed Epic Loot before upload, clamped 0-25. Recording duration remains PostEventSeconds.");
                filterBossLoot = Bind("Boss Kill", "OnlyCaptureIfLootMeetsRarity", false, "Only encode/save/upload a boss clip when at least one observed item meets MinimumLootRarity. Preserve kill footage while awaiting drops. Missing/unknown qualifying data skips the clip at the wait deadline.");
                minimumBossRarity = Bind("Boss Kill", "MinimumLootRarity", "Legendary", "None accepts all; otherwise an actual Epic Loot rarity name (0.14.2: Magic, Rare, Epic, Legendary, Mythic, Ancient). Used only when OnlyCaptureIfLootMeetsRarity=true. Unknown names or absent Epic Loot fail closed.");
                firstKillBypassesRarity = Bind("Boss Kill", "FirstKillBypassesRarity", true, "Always keep this character's first recorded kill of each boss regardless of MinimumLootRarity. Repeat kills still use the rarity filter. FirstKillOnly separately excludes all repeat kills.");
                lootTrigger = Bind("Triggers", "LootDrop", true, "Enable loot highlights. Loot Capture.Enabled must also be enabled; bosses use Boss Kill rules exclusively.");
                lootEnabled = Bind("Loot Capture", "Enabled", true, "Capture qualifying ordinary kill loot and verified natural chest/world pickups. Epic Loot is optional when MinimumRarity=None. No crafting, player storage or gravestone triggers.");
                chestPickups = Bind("Loot Capture", "CaptureChestPickups", true, "Host-controlled: capture first acquisition of tracked, naturally generated chest loot. Player chests, deposits, gravestones and untracked older contents are excluded.");
                worldPickups = Bind("Loot Capture", "CaptureWorldPickups", true, "Host-controlled: capture first acquisition from tracked natural pickables and breakable drops. Player drops, cultivated plants, mixed ground stacks and unknown sources are excluded.");
                pickupMessage = Bind("Loot Capture", "PickupMessage", "Great loot from {source}!", "Natural pickup message: {source}, {player}, {loot}, {item_count}. Collected by and loot append if omitted.");
                minimumLootRarity = Bind("Loot Capture", "MinimumRarity", "Legendary", "Minimum observed rarity: Magic, Rare, Epic, Legendary, Mythic, Ancient. None accepts any observed item. Unknown names skip captures.");
                highlightMessage = Bind("Loot Capture", "Message", "Great loot from {enemy}!", "Placeholders: {enemy}, {player}, {loot}, {item_count}. Loot and kill credit append if omitted. Item display is configured independently in this section.");
                // Seed new independent entries from the existing display preferences on upgrade.
                highlightQuantity = Bind("Loot Capture", "ShowQuantity", Value(showLootQuantity), "Show item quantities in ordinary-loot posts.");
                highlightRarity = Bind("Loot Capture", "ShowRarity", Value(showRarity), "Show rarity labels and colored markers; does not change MinimumRarity filtering.");
                highlightModifiers = Bind("Loot Capture", "ShowItemModifiers", Value(showModifiers), "Show identified items' Epic Loot modifier text in ordinary-loot posts.");
                highlightSockets = Bind("Loot Capture", "ShowItemSockets", Value(showSockets), "Show identified items' socket counts in ordinary-loot posts.");
                highlightUnidentified = Bind("Loot Capture", "ShowUnidentifiedStatus", Value(showUnidentified), "Label unidentified items. Hidden modifiers and sockets are never revealed.");
                highlightMaxItems = Bind("Loot Capture", "MaxLootItemsShown", Value(maxLootItems), "Maximum displayed entries, 1-20; highest rarity first. Display limits do not affect capture eligibility.");
                highlightHeader = Bind("Loot Capture", "LootHeader", Value(lootHeader), "Header above generated loot in ordinary-loot posts.");
                highlightWaitSeconds = Bind("Loot Capture", "LootWaitSeconds", 12.0, "Wait 0-25 seconds after credited kill for drops. Holds metadata only; up to 64 pending kills.");
                lootPostSetting = Bind("Loot Capture", "PostEventSeconds", 4.0, "Seconds after observing qualifying drops or acquisitions. Uses rolling pre-event footage; long ragdoll delays may leave the kill outside the clip. Restart after changing.");
                widthSetting = Bind("Capture", "Width", 640, "Output pixel width, 480-1920. Aspect ratio and memory limits may reduce the effective dimensions.");
                heightSetting = Bind("Capture", "Height", 360, "Output pixel height, 270-1080. Supported aspect ratios: 1:2 through 3:1.");
                fpsSetting = Bind("Capture", "FPS", 15, "Capture sampling rate, 1-30. Missed samples are skipped.");
                qualitySetting = Bind("Capture", "WebPQuality", 80, "Lossy animated WebP quality, 1-100.");
                preSetting = Bind("Capture", "PreEventSeconds", 5.0, "Host-controlled rolling history, 1-30 seconds.");
                postSetting = Bind("Capture", "PostEventSeconds", 2.0, "Host-controlled manual/death post-event duration, 0-30 seconds.");
                budgetSetting = Bind("Capture", "MemoryBudgetMiB", 192, "Managed frame-pool budget, 48-512 MiB. Effective resolution, then FPS, reduces when required to fit; dimensions never fall below 480x270.");
                string pluginDirectory = Path.GetDirectoryName(Info.Location);
                relayDirectory = Path.Combine(pluginDirectory, "RelayTemp");
                relay = new ClipRelay(CanRelay, () => Math.Max(1, Math.Min(10, Value(uploadLimitMiB))) * 1048576,
                    ReceiveRelayedClip, message => Logger.LogInfo("[Relay] " + message), hostSettings.PeerHasPolicy);
                encoderPath = Path.Combine(pluginDirectory, "Encoder", "ValheimMoments.Encoder.exe");
                outputDirectory = Path.Combine(pluginDirectory, "Clips");
                try
                {
                    worldHarmony = new Harmony("local.valheimmoments.world.loot");
                    WorldLootDetector.Enabled = kind => initialized && !stopped && !paused && Value(captureEnabled) && hostSettings.Ready &&
                        !captureSettingsDirty && Value(lootTrigger) && Value(lootEnabled) && (kind == "c" ? Value(chestPickups) : Value(worldPickups));
                    WorldLootDetector.Read = item => epicReady ? EpicLootAdapter.Read(item, item.m_shared.m_name) :
                        new LootItem { Id = EpicLootAdapter.Plain(item.m_shared.m_name, 128), Name = EpicLootAdapter.Plain(item.m_shared.m_name), Quantity = item.m_stack };
                    WorldLootDetector.OnAcquired = (kind, item) => {
                        SynchronizeCaptureSession();
                        if (captureSession.HasSession && Player.m_localPlayer != null)
                            acquisitions.Add(kind, item, Player.m_localPlayer.GetPlayerName(), clock.Elapsed.TotalSeconds);
                    };
                    WorldLootDetector.OnError = () => Logger.LogWarning("[Loot] Natural loot provenance unavailable; uncertain acquisition skipped.");
                    WorldLootDetector.Install(worldHarmony);
                    Logger.LogInfo("[Loot] Natural chest/world provenance observer installed.");
                }
                catch (Exception error)
                {
                    worldHarmony?.UnpatchSelf(); WorldLootDetector.Clear();
                    Logger.LogWarning("[Loot] Natural pickup observer unavailable: " + error.GetType().Name);
                }
                try
                {
                    attributionHarmony = new Harmony("local.valheimmoments.boss.attribution");
                    BossAttribution.OnDiagnostic = reason => Logger.LogInfo("[Boss] Final-blow source: " + reason);
                    BossAttribution.Install(attributionHarmony);
                    Logger.LogInfo("[Boss] Kill-credit roster and final-blow attribution installed.");
                }
                catch (Exception error) { Logger.LogWarning("[Boss] Final-blow attribution unavailable: " + error.GetType().Name); }
                if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                {
                    Logger.LogInfo("[Relay] Host delivery ready; graphics capture disabled on this headless server.");
                    return;
                }
                if (!SystemInfo.supportsAsyncGPUReadback) throw new NotSupportedException("This graphics backend does not support asynchronous GPU readback.");
                if (!File.Exists(encoderPath)) throw new FileNotFoundException("Bundled Encoder/ValheimMoments.Encoder.exe is missing.");
                ApplyCaptureSettings();
                initialized = true;
                try
                {
                    deathHarmony = new Harmony("local.valheimmoments.death");
                    PlayerDeathDetector.OnLocalDeath = OnLocalDeath;
                    PlayerDeathDetector.OnError = () => Logger.LogWarning("[Death] Could not inspect local death; gameplay was left unchanged.");
                    PlayerDeathDetector.Install(deathHarmony);
                    Logger.LogInfo("[Death] Local player death detector installed.");
                }
                catch (Exception error) { Logger.LogWarning("[Death] Detector unavailable: " + error.GetType().Name + ". Manual capture remains available."); }
                try
                {
                    bossHarmony = new Harmony("local.valheimmoments.boss");
                    BossKillDetector.OnKill = OnBossKill;
                    BossKillDetector.ObserveOrdinary = () => Value(lootTrigger) && Value(lootEnabled);
                    BossKillDetector.OnLootKill = kill => {
                        if (Value(captureEnabled) && !paused) lootHighlights.Add(kill, clock.Elapsed.TotalSeconds, Value(highlightWaitSeconds));
                    };
                    BossKillDetector.OnError = () => Logger.LogWarning("[Boss] Unable to verify character kill statistics; boss event skipped.");
                    BossKillDetector.Install(bossHarmony);
                    Logger.LogInfo("[Boss] Local kill-credit detector installed.");
                }
                catch (Exception error) { Logger.LogWarning("[Boss] Detector unavailable: " + error.GetType().Name + ". Other captures remain available."); }
                // Observe status effects; the current host policy controls attribution.
                {
                    try
                    {
                        periodicHarmony = new Harmony("local.valheimmoments.boss.periodic");
                        PeriodicAttribution.Enabled = () => Value(trackPeriodicSetting);
                        PeriodicAttribution.Install(periodicHarmony);
                        Logger.LogInfo("[Boss] Periodic damage source tracking installed.");
                    }
                    catch (Exception error)
                    {
                        periodicHarmony?.UnpatchSelf();
                        PeriodicAttribution.Clear();
                        Logger.LogWarning("[Boss] Periodic attribution unavailable: " + error.GetType().Name);
                    }
                }
                try
                {
                    lootHarmony = new Harmony("local.valheimmoments.boss.loot");
                    BossLootDetector.OnObserved = count => Logger.LogDebug("[Loot] Observed generated loot; item types=" + count);
                    BossLootDetector.OnError = () => Logger.LogWarning("[Loot] Some loot data unavailable; boss capture remains enabled.");
                    BossLootDetector.Install(lootHarmony);
                    Logger.LogInfo("[Loot] Generated boss loot observer installed.");
                }
                catch (Exception error) { Logger.LogWarning("[Loot] Observer unavailable: " + error.GetType().Name); }
                Logger.LogInfo(string.Format("[Capture] Ready: Unity {0}, {1}, {2}x{3} at {4} FPS; pool {5:F1} MiB. F10 saves; F9 pauses. Output: {6}",
                    Application.unityVersion, SystemInfo.graphicsDeviceType, width, height, fps, history.AllocatedPixelBytes / 1048576.0, outputDirectory));
                StartCoroutine(CaptureLoop());
            }
            catch (Exception error)
            {
                Logger.LogError("[Capture] Initialization failed: " + error.Message);
                StopCapture();
            }
        }

        private static RenderTexture MakeTarget(int w, int h)
        {
            var result = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = "ValheimMoments", antiAliasing = 1, useMipMap = false, filterMode = FilterMode.Bilinear };
            if (!result.Create()) { Destroy(result); throw new InvalidOperationException("RenderTexture creation failed"); }
            return result;
        }

        private bool ApplyCaptureSettings()
        {
            double pre = Value(preSetting), post = Value(postSetting);
            double bossPost = Value(bossPostSetting), lootPost = Value(lootPostSetting);
            double maxPost = Math.Max(post, Math.Max(bossPost, lootPost));
            var limits = CaptureLimits.Fit(Value(widthSetting), Value(heightSetting), Value(fpsSetting), Value(qualitySetting), Value(budgetSetting), pre, maxPost);
            if (history != null && width == limits.Width && height == limits.Height && fps == limits.FPS &&
                appliedPre == pre && appliedPost == post && bossPostSeconds == bossPost && lootPostSeconds == lootPost)
            { quality = limits.Quality; captureSettingsDirty = false; return true; }
            // Let GPU requests and an existing encoder finish before replacing storage.
            if (pending.Count != 0 || encoding != null) return false;
            waitingForLoot?.Release(); waitingForLoot = null;
            pendingBoss = null; lootHighlights.Clear(); acquisitions.Clear();
            history?.ClearHistory(); history = null; captureSession = null;
            foreach (var slot in allSlots)
            {
                if (slot.Pixels.IsCreated) slot.Pixels.Dispose();
                ReleaseTarget(slot.Target);
            }
            allSlots.Clear(); free.Clear();
            width = limits.Width; height = limits.Height; fps = limits.FPS; quality = limits.Quality;
            appliedPre = pre; appliedPost = post; bossPostSeconds = bossPost; lootPostSeconds = lootPost;
            history = new CaptureBuffer(width, height, fps, pre, post, limits.BudgetMiB * 1048576L, maxPost);
            captureSession = new CaptureSession(history);
            scratch = new byte[checked(width * height * 4)];
            for (int i = 0; i < 3; i++)
            {
                var slot = new ReadbackSlot(); allSlots.Add(slot);
                slot.Target = MakeTarget(width, height);
                slot.Pixels = new NativeArray<byte>(scratch.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                free.Enqueue(slot);
            }
            nextCapture = 0; captureSettingsDirty = false;
            Logger.LogInfo(string.Format("[Capture] Effective settings: {0}x{1}, {2} FPS, quality {3}; pre={4}s, maximum post={5}s, pool={6:F1} MiB. Resolution and FPS fit aspect and memory limits.",
                width, height, fps, quality, pre, maxPost, limits.PoolBytes / 1048576.0));
            return true;
        }

        private void Update()
        {
            if (stopped) return;
            try
            {
                hostSettings?.Tick(clock.Elapsed.TotalSeconds);
                if (relay != null && !Value(relayEnabled)) relay.StopSending();
                relay?.Tick(clock.Elapsed.TotalSeconds);
                if (uploadCancellation != null && upload != null && !upload.IsCompleted &&
                    (!DiscordRouting.CanSubmit(uploadSession, true, ZNet.instance, ZNet.instance != null && ZNet.instance.IsServer()) ||
                    (relayCompletion != null && (!Value(relayEnabled) || !Value(discordEnabled) || !Value(uploadClips) || !relay.DeliveryPeerConnected))))
                    uploadCancellation.Cancel();
                if (upload != null && upload.IsCompleted)
                {
                    var result = upload.GetAwaiter().GetResult();
                    if (result.Success) Logger.LogInfo("[Discord] " + result.Message);
                    else Logger.LogWarning("[Discord] " + result.Message);
                    upload = null;
                    uploadCancellation?.Dispose(); uploadCancellation = null; uploadSession = null;
                    var completed = relayCompletion; relayCompletion = null;
                    completed?.Invoke(result.Success);
                }
            }
            catch (Exception error) { Logger.LogWarning("[Relay] Delivery update failed: " + error.GetType().Name); }
            if (!initialized) return;
            try
            {
                double now = clock.Elapsed.TotalSeconds;
                if (lastUpdate > 0)
                {
                    double delta = (now - lastUpdate) * 1000;
                    frameMs += delta; maxFrameMs = Math.Max(maxFrameMs, delta); updateCount++;
                }
                lastUpdate = now;
                if (Input.GetKeyDown(Value(toggleKey)))
                {
                    paused = !paused;
                    Logger.LogInfo(paused ? "[Capture] Paused for baseline comparison." : "[Capture] Recording resumed; allow 5 seconds to warm up.");
                }
                SynchronizeCaptureSession();
                DrainReadbacks();
                if (captureSettingsDirty && hostSettings.Ready && now - captureSettingsChangedAt >= 0.5) ApplyCaptureSettings();
                bool active = Value(captureEnabled) && !paused && captureSession.HasSession && hostSettings.Ready && !captureSettingsDirty;
                if (active && Value(lootTrigger) && Value(lootEnabled))
                {
                    lootHighlights.Poll(now, Value(minimumLootRarity), OnLootHighlight);
                    acquisitions.Poll(now, Value(minimumLootRarity), kind => kind == "c" ? Value(chestPickups) : Value(worldPickups), OnLootHighlight);
                }
                else { lootHighlights.Clear(); acquisitions.Clear(); }
                if (!active)
                {
                    if (!historyCleared && pending.Count == 0) { history.ClearHistory(); historyCleared = true; }
                }
                else
                {
                    historyCleared = false;
                    if (Value(manualTrigger) && Application.isFocused && Input.GetKeyDown(Value(captureKey)))
                    {
                        Trigger("manual", "Valheim moment");
                    }
                }
                if (encoding != null && encoding.IsCompleted && (!encoding.IsCompletedSuccessfully || activeBoss?.Loot == null || !activeBoss.Loot.Pending || (activeBoss.BossNumber > 0 && !Value(showBossLoot)) || now >= lootDeadline))
                {
                    try
                    {
                        Logger.LogInfo("[WebP] " + encoding.GetAwaiter().GetResult() + "; saved " + activeOutput);
                        StartUpload(activeOutput);
                    }
                    catch (Exception error) { Logger.LogWarning("[WebP] Clip failed: " + error.Message); }
                    encodingClip.Release(); encodingClip = null; encoding = null;
                }
                // Oldest pending submission is a safe exclusive completion watermark.
                if (active && encoding == null)
                {
                    var clip = history.TryComplete(pending.Count == 0 ? now : pending.Peek().Submitted);
                    if (clip != null)
                    {
                        if (clip.Count == 0) { clip.Release(); Logger.LogWarning("[Capture] No frames available; clip discarded."); }
                        else if (pendingBoss != null && pendingBoss.BossNumber > 0 && Value(filterBossLoot)) waitingForLoot = clip;
                        else StartEncoding(clip);
                    }
                }
                if (waitingForLoot != null)
                {
                    string reason;
                    var decision = BossLootFilter.Evaluate(pendingBoss?.Loot, Value(filterBossLoot), pendingBoss != null && pendingBoss.FirstKill, Value(firstKillBypassesRarity), Value(minimumBossRarity), now >= lootDeadline, out reason);
                    if (decision != LootDecision.Wait)
                    {
                        var clip = waitingForLoot; waitingForLoot = null;
                        if (decision == LootDecision.Accept) { Logger.LogInfo("[Loot] Boss clip accepted: " + reason); StartEncoding(clip); }
                        else
                        {
                            clip.Release(); pendingBoss = null;
                            Logger.LogInfo("[Loot] Boss clip skipped: " + reason);
                        }
                    }
                }
                if (Value(timing) && now - lastReport >= 10)
                {
                    Logger.LogInfo(string.Format("[Capture] {0}: received={1}, submitted={2}, skipped={3}, errors={4}, buffer={5}; submit CPU avg/max={6:F2}/{7:F2}ms, copy CPU avg/max={8:F2}/{9:F2}ms, readback latency avg={10:F2}ms; game Update avg/max={11:F2}/{12:F2}ms; managed={13:F1}MiB; effective capture FPS={14:F2}",
                        active ? "recording" : "paused", received, submitted, skipped, errors, history.BufferedFrames,
                        submitMs / Math.Max(1, submitted), maxSubmitMs, copyMs / Math.Max(1, received), maxCopyMs,
                        latencyMs / Math.Max(1, received + errors), frameMs / Math.Max(1, updateCount), maxFrameMs, GC.GetTotalMemory(false) / 1048576.0, received / (now - lastReport)));
                    submitted = received = skipped = errors = updateCount = 0;
                    submitMs = copyMs = latencyMs = maxSubmitMs = maxCopyMs = frameMs = maxFrameMs = 0;
                    lastReport = now;
                }
            }
            catch (Exception error) { Logger.LogError("[Capture] Stopping after error: " + error.Message); StopCapture(); }
        }

        private IEnumerator CaptureLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (!stopped)
            {
                yield return endOfFrame;
                if (!Value(captureEnabled) || paused || !Application.isFocused) continue;
                try { SubmitCapture(); }
                catch (Exception error) { Logger.LogError("[Capture] Submission failed: " + error.Message); StopCapture(); }
            }
        }

        private void SubmitCapture()
        {
            if (!hostSettings.Ready || captureSettingsDirty) return;
            SynchronizeCaptureSession();
            if (!captureSession.HasSession || Player.m_localPlayer == null) return;
            double now = clock.Elapsed.TotalSeconds;
            if (now < nextCapture) return;
            // Anchor to the sampling grid; adding a period to each actual game-frame
            // time drifts toward 12 FPS on a jittery 60 FPS render loop.
            nextCapture = (Math.Floor(now * fps) + 1) / fps; // No catch-up burst.
            if (free.Count == 0) { skipped++; return; }
            if (Screen.width < 16 || Screen.height < 16) return;
            if (screen == null || screen.width != Screen.width || screen.height != Screen.height)
            {
                if (pending.Count != 0) { skipped++; return; }
                if ((long)Screen.width * Screen.height > 33554432) throw new InvalidOperationException("Screen exceeds prototype capture limit");
                ReleaseTarget(screen);
                screen = MakeTarget(Screen.width, Screen.height);
            }
            ReadbackSlot slot = free.Peek();
            double started = clock.Elapsed.TotalMilliseconds;
            ScreenCapture.CaptureScreenshotIntoRenderTexture(screen);
            Graphics.Blit(screen, slot.Target);
            slot.Submitted = now;
            slot.SessionRevision = captureSession.Revision;
            slot.Request = AsyncGPUReadback.RequestIntoNativeArray(ref slot.Pixels, slot.Target, 0, TextureFormat.RGBA32);
            free.Dequeue(); pending.Enqueue(slot);
            double elapsed = clock.Elapsed.TotalMilliseconds - started;
            submitMs += elapsed; maxSubmitMs = Math.Max(maxSubmitMs, elapsed); submitted++;
        }

        private void DrainReadbacks()
        {
            while (pending.Count > 0 && pending.Peek().Request.done)
            {
                ReadbackSlot slot = pending.Dequeue();
                if (!captureSession.Accepts(slot.SessionRevision))
                {
                    free.Enqueue(slot);
                    continue; // Completed request may be reused, but old pixels never enter new history.
                }
                latencyMs += (clock.Elapsed.TotalSeconds - slot.Submitted) * 1000;
                if (slot.Request.hasError)
                {
                    errors++; consecutiveErrors++;
                    if (consecutiveErrors >= 8) throw new InvalidOperationException("Repeated GPU readback failures; try another graphics backend.");
                }
                else
                {
                    consecutiveErrors = 0;
                    double start = clock.Elapsed.TotalMilliseconds;
                    slot.Pixels.CopyTo(scratch);
                    if (!history.AddFrame(scratch, slot.Submitted)) skipped++;
                    double elapsed = clock.Elapsed.TotalMilliseconds - start;
                    copyMs += elapsed; maxCopyMs = Math.Max(maxCopyMs, elapsed); received++;
                }
                free.Enqueue(slot);
            }
        }

        private void StartEncoding(CaptureBuffer.Clip clip)
        {
            encodingClip = clip;
            activeMessage = pendingMessage;
            activeRecorder = pendingRecorder;
            activeKind = pendingKind; activeSession = pendingSession; activeAsHost = pendingAsHost;
            activeBoss = pendingBoss; pendingBoss = null;
            activeOutput = Path.Combine(outputDirectory, pendingKind + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".webp");
            string destination = activeOutput;
            bool flipImage = Value(flip);
            int encodeWidth = width, encodeHeight = height, encodeQuality = quality;
            int before = 0;
            for (int i = 0; i < clip.Count; i++) if (clip.GetTimestamp(i) < clip.TriggerTime) before++;
            Logger.LogInfo(string.Format("[WebP] Encoding {0} frames in background helper; pre-event={1}, post-event={2}.", clip.Count, before, clip.Count - before));
            encoding = Task.Run(() => EncoderClient.Encode(clip, encoderPath, destination, encodeWidth, encodeHeight, encodeQuality, flipImage, shutdown.Token));
        }

        private static void ReleaseTarget(RenderTexture target)
        {
            if (target == null) return;
            target.Release(); Destroy(target);
        }

        private void StartUpload(string file)
        {
            var session = ZNet.instance;
            if (session != null && !session.IsServer() && !activeAsHost && ReferenceEquals(activeSession, session))
            {
                string message = EventMessages.FormatPost(activeBoss == null ? activeMessage : BossMessage(activeBoss));
                if (!Value(relayEnabled) || !relay.Offer(activeSession, file, activeKind, message, Value(saveLocalCopy)))
                    Logger.LogInfo("[Relay] Clip retained locally: relay disabled, unavailable, busy or clip exceeds 10 MiB.");
                return;
            }
            if (!DiscordRouting.CanSubmit(activeSession, activeAsHost, session, session != null && session.IsServer()))
            {
                Logger.LogInfo("[Discord] Local clip retained: changed or ended sessions cannot submit old clips.");
                return;
            }
            if (!Value(discordEnabled) || !Value(uploadClips)) return;
            if (upload != null)
            {
                Logger.LogWarning("[Discord] Upload busy; new clip retained locally.");
                return;
            }
            // Snapshot config on Unity's thread; perform all HTTP/file work on a worker.
            var options = new DiscordOptions {
                WebhookUrl = DiscordRouting.Destination(activeKind, Value(webhookUrl),
                    Value(useBossWebhook), Value(bossWebhook), Value(useLootWebhook), Value(lootWebhook), Value(useDeathWebhook), Value(deathWebhook)),
                Username = Value(discordUsername),
                Message = EventMessages.RecordedPost(activeBoss == null ? activeMessage : BossMessage(activeBoss), activeRecorder),
                SaveLocalCopy = Value(saveLocalCopy),
                MaxUploadBytes = Math.Max(1, Math.Min(100, Value(uploadLimitMiB))) * 1048576L
            };
            Logger.LogInfo("[Discord] Starting background upload.");
            uploadSession = session;
            uploadCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            var token = uploadCancellation.Token;
            upload = Task.Run(() => DiscordWebhook.UploadAsync(file, options, token));
        }

        private void OnDestroy() { StopCapture(); }
        private string Destination(string kind)
        {
            return DiscordRouting.Destination(kind, Value(webhookUrl), Value(useBossWebhook), Value(bossWebhook),
                Value(useLootWebhook), Value(lootWebhook), Value(useDeathWebhook), Value(deathWebhook));
        }
        private bool CanRelay(string kind)
        {
            if (!Value(relayEnabled) || !Value(discordEnabled) || !Value(uploadClips) || upload != null) return false;
            if (kind == "manual" && !Value(manualTrigger)) return false;
            if (kind == "boss" && (!Value(bossTrigger) || !Value(bossEnabled))) return false;
            if (kind == "loot" && (!Value(lootTrigger) || !Value(lootEnabled))) return false;
            if (kind == "death" && (!Value(deathTrigger) || !Value(deathEnabled))) return false;
            Uri endpoint;
            return RelayProtocol.ValidKind(kind) && DiscordWebhook.TryEndpoint(Destination(kind), out endpoint);
        }
        private void ReceiveRelayedClip(RelayBuffer clip, string recorder, Action<bool> completion)
        {
            var session = ZNet.instance;
            if (session == null || !session.IsServer() || !CanRelay(clip.Kind)) { completion(false); return; }
            var options = new DiscordOptions { WebhookUrl = Destination(clip.Kind), Username = Value(discordUsername),
                Message = EventMessages.RecordedPost(clip.Message, recorder), SaveLocalCopy = true,
                MaxUploadBytes = Math.Max(1, Math.Min(10, Value(uploadLimitMiB))) * 1048576L };
            uploadSession = session; relayCompletion = completion;
            uploadCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            var token = uploadCancellation.Token;
            string file = Path.Combine(relayDirectory, Guid.NewGuid().ToString("N") + ".webp");
            upload = Task.Run(async () => {
                try
                {
                    token.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(relayDirectory);
                    using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, true))
                        await stream.WriteAsync(clip.Bytes, 0, clip.Bytes.Length, token).ConfigureAwait(false);
                    var result = await DiscordWebhook.UploadAsync(file, options, token).ConfigureAwait(false);
                    return new UploadResult { Success = result.Success, Message = result.Success ? "Client clip uploaded; success confirmation sent." : "Client clip delivery failed; client retains original." };
                }
                catch { return new UploadResult { Success = false, Message = "Client clip delivery cancelled or failed; client retains original." }; }
                finally { try { if (File.Exists(file)) File.Delete(file); } catch { } }
            });
        }
        private void OnLootHighlight(BossKill kill)
        {
            if (!Trigger("loot", "", lootPostSeconds)) return;
            pendingBoss = kill;
            lootDeadline = clock.Elapsed.TotalSeconds + Math.Max(0, Math.Min(25, double.IsNaN(Value(highlightWaitSeconds)) ? 12 : Value(highlightWaitSeconds)));
            Logger.LogInfo("[Loot] Qualifying loot captured; minimum=" + Value(minimumLootRarity));
        }
        private void OnBossKill(BossKill kill)
        {
            if (!Value(bossTrigger) || !Value(bossEnabled)) return;
            Logger.LogInfo("[Boss] Kill credited; boss number=" + kill.BossNumber + ", first kill=" + kill.FirstKill);
            Logger.LogInfo("[Boss] Capture rules: FirstKillOnly=" + Value(firstBossOnly) + ", rarity filter=" + Value(filterBossLoot) + ", minimum=" + Value(minimumBossRarity) + ", first-kill bypass=" + Value(firstKillBypassesRarity));
            if (Value(firstBossOnly) && !kill.FirstKill)
            {
                Logger.LogInfo("[Boss] Repeat kill skipped by FirstKillOnly.");
                return;
            }
            if (Trigger("boss", "", bossPostSeconds))
            {
                pendingBoss = kill;
                double wait = Value(lootWaitSeconds);
                lootDeadline = clock.Elapsed.TotalSeconds + (double.IsNaN(wait) ? 12 : Math.Max(0, Math.Min(25, wait)));
            }
        }
        private string BossMessage(BossKill kill)
        {
            try
            {
                bool highlight = kill.BossNumber <= 0;
                string loot = null;
                if (highlight)
                    loot = EventMessages.Heading(Value(highlightHeader), 2) + "\n" + (kill.Loot == null ? "unavailable" : kill.Loot.Display(Value(highlightMaxItems), Value(highlightQuantity), text => Localization.instance.Localize(text), Value(highlightRarity), Value(highlightModifiers), Value(highlightSockets), Value(highlightUnidentified)));
                else if (Value(showBossLoot))
                    loot = EventMessages.Heading(Value(lootHeader), 2) + "\n" + (kill.Loot == null ? "unavailable" : kill.Loot.Display(Value(maxLootItems), Value(showLootQuantity), text => Localization.instance.Localize(text), Value(showRarity), Value(showModifiers), Value(showSockets), Value(showUnidentified)));
                string count = kill.Loot != null && kill.Loot.Observed ? kill.Loot.Items.Count.ToString() : "unknown";
                if (highlight && kill.Acquired) return EventMessages.FoundLoot(Value(pickupMessage), kill.EnemyKey, kill.PlayerName, loot, count);
                if (highlight) return EventMessages.Loot(Value(highlightMessage), Localization.instance.Localize(kill.EnemyKey), kill.PlayerName, loot, count);
                return EventMessages.Boss(Value(bossMessage), Localization.instance.Localize(kill.EnemyKey), BossAttribution.CreditLabel(kill.CreditNames, kill.PlayerName), Value(bossNameMode), kill.FinalBlowName, loot, count);
            }
            catch
            {
                Logger.LogWarning("[Loot] Message enrichment failed; sending boss names only.");
                if (kill.Acquired) return EventMessages.FoundLoot(Value(pickupMessage), kill.EnemyKey, kill.PlayerName, "unavailable");
                return kill.BossNumber <= 0 ? EventMessages.Loot(Value(highlightMessage), kill.EnemyKey, kill.PlayerName, "unavailable") : EventMessages.Boss(Value(bossMessage), kill.EnemyKey, BossAttribution.CreditLabel(kill.CreditNames, kill.PlayerName), Value(bossNameMode), kill.FinalBlowName);
            }
        }
        private void OnLocalDeath(Player player, string cause)
        {
            if (!Value(deathTrigger) || !Value(deathEnabled)) return;
            string message = EventMessages.Death(Value(deathMessage), Value(includePlayerName), Value(playerNameOverride),
                Value(includePlayerName) ? player.GetPlayerName() : "", Value(includeCause), cause);
            Trigger("death", message);
        }

        private bool Trigger(string kind, string message, double? postOverride = null)
        {
            if (!initialized || stopped || !Value(captureEnabled) || paused) return false;
            if (!hostSettings.Ready || captureSettingsDirty) return false;
            SynchronizeCaptureSession();
            if (!captureSession.HasSession || Player.m_localPlayer == null) return false;
            if (!history.TryTrigger(clock.Elapsed.TotalSeconds, postOverride))
            {
                Logger.LogInfo("[Capture] " + kind + " trigger ignored: a clip is collecting or encoding.");
                return false;
            }
            pendingBoss = null;
            pendingSession = ZNet.instance;
            pendingAsHost = pendingSession != null && pendingSession.IsServer();
            pendingKind = kind; pendingMessage = message;
            pendingRecorder = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : null;
            Logger.LogInfo("[Capture] " + kind + " event triggered; buffered frames: " + history.BufferedFrames);
            return true;
        }
        private void OnDisable() { if (initialized || relay != null) StopCapture(); }
        private void SynchronizeCaptureSession()
        {
            if (!captureSession.Observe(ZNet.instance)) return;
            waitingForLoot?.Release(); waitingForLoot = null;
            pendingBoss = null;
            lootHighlights.Clear(); acquisitions.Clear();
            historyCleared = true;
            consecutiveErrors = 0;
            Logger.LogInfo("[Capture] Session changed; buffered footage and pending capture cleared.");
        }
        private void StopCapture()
        {
            if (stopped) return;
            stopped = true;
            hostSettings?.Dispose();
            relay?.Dispose();
            PlayerDeathDetector.OnLocalDeath = null;
            PlayerDeathDetector.OnError = null;
            BossKillDetector.OnKill = null;
            BossKillDetector.OnLootKill = null; BossKillDetector.ObserveOrdinary = null;
            lootHighlights.Clear(); acquisitions.Clear();
            BossKillDetector.OnError = null;
            BossAttribution.Clear();
            PeriodicAttribution.Clear();
            try { periodicHarmony?.UnpatchSelf(); } catch { Logger.LogWarning("[Boss] Could not remove periodic attribution patches."); }
            WorldLootDetector.Clear();
            try { worldEpicHarmony?.UnpatchSelf(); worldHarmony?.UnpatchSelf(); } catch { Logger.LogWarning("[Loot] Could not remove natural pickup patches."); }
            BossLootDetector.Clear(); pendingBoss = activeBoss = null;
            waitingForLoot?.Release(); waitingForLoot = null;
            try { epicHarmony?.UnpatchSelf(); } catch { Logger.LogWarning("[Loot] Could not remove Epic Loot patches."); }
            try { lootHarmony?.UnpatchSelf(); } catch { Logger.LogWarning("[Loot] Could not remove loot patches."); }
            try { attributionHarmony?.UnpatchSelf(); } catch { Logger.LogWarning("[Boss] Could not remove attribution patches."); }
            try { bossHarmony?.UnpatchSelf(); } catch { Logger.LogWarning("[Boss] Could not remove patch during shutdown."); }
            try { deathHarmony?.UnpatchSelf(); } catch { Logger.LogWarning("[Death] Could not remove patch during shutdown."); }
            shutdown.Cancel();
            // Only shutdown may block on the GPU. Never free an in-flight target or
            // NativeArray. Completed clips remain alive until their worker unwinds.
            try { if (pending.Count > 0) AsyncGPUReadback.WaitAllRequests(); }
            catch (Exception error) { Logger.LogWarning("[Capture] Readback teardown: " + error.Message); return; }
            foreach (var slot in allSlots)
            {
                if (slot.Pixels.IsCreated) slot.Pixels.Dispose();
                ReleaseTarget(slot.Target);
            }
            allSlots.Clear(); pending.Clear(); free.Clear();
            ReleaseTarget(screen); screen = null;
            if (history != null) history.ClearHistory();
            scratch = null; history = null;
        }
    }
}
