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
using ValheimEventClips.Core;

namespace ValheimEventClips
{
    [BepInPlugin("local.valheimeventclips", "Valheim Moments", "0.8.1")]
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private sealed class ReadbackSlot
        {
            internal RenderTexture Target;
            internal NativeArray<byte> Pixels;
            internal AsyncGPUReadbackRequest Request;
            internal double Submitted;
        }

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Queue<ReadbackSlot> free = new Queue<ReadbackSlot>(3);
        private readonly Queue<ReadbackSlot> pending = new Queue<ReadbackSlot>(3);
        private readonly List<ReadbackSlot> allSlots = new List<ReadbackSlot>(3);
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private CaptureBuffer history;
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
        private Harmony lootHarmony;
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
            if (!initialized || stopped) return;
            try
            {
                System.Reflection.Assembly epic = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if (assembly.GetName().Name == "EpicLoot") { epic = assembly; break; }
                epicHarmony = new Harmony("local.valheimeventclips.epicloot");
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

        private T Setting<T>(string key, T value, string help)
        {
            return Config.Bind("Capture", key, value, help + " Restart Valheim after changing.").Value;
        }

        private void Awake()
        {
            try
            {
                captureEnabled = Config.Bind("Capture", "Enabled", true, "Enable recording. F9 toggles recording for baseline comparison.");
                captureKey = Config.Bind("Capture", "ManualCaptureKey", KeyCode.F10, "Save recent gameplay plus post-event footage locally.");
                toggleKey = Config.Bind("Capture", "ToggleCaptureKey", KeyCode.F9, "Pause/resume recording to compare game performance.");
                timing = Config.Bind("Debug", "LogCaptureTiming", true, "Log aggregate CPU timing, readback latency and frame counts every 10 seconds.");
                flip = Config.Bind("Capture", "FlipVertically", false, "Enable if the test WebP is upside down on your graphics backend.");
                discordEnabled = Config.Bind("Discord", "Enabled", false, "Host/single-player only: enable Discord delivery. Remote clients send clips to the host and never use local webhook settings.");
                relayEnabled = Config.Bind("Discord", "EnableClientRelay", true, "Host: accept clips from connected clients. Client: allow sending clips to the host. Both sides need this version. Host Discord.Enabled and event trigger switches also apply. Client copies are always retained.");
                uploadClips = Config.Bind("Discord", "UploadClips", true, "Upload newly completed clips when Discord is enabled.");
                saveLocalCopy = Config.Bind("Discord", "SaveLocalCopy", true, "Keep uploaded clips locally. Failed/skipped uploads always retain the clip.");
                webhookUrl = Config.Bind("Discord", "WebhookURL", "", "Secret: enter locally, never share this config. HTTPS Discord webhook; optional thread_id query.");
                discordUsername = Config.Bind("Discord", "Username", "Valheim Moments", "Host/single-player only: bot display name, 1–80 characters. Remote client values are ignored.");
                useBossWebhook = Config.Bind("Discord", "UseBossKillWebhook", false, "Host only: route boss clips to BossKillWebhookURL; when off, use WebhookURL.");
                bossWebhook = Config.Bind("Discord", "BossKillWebhookURL", "", "Host-only secret: optional boss destination. An enabled but invalid override keeps the clip locally; it does not silently change channels.");
                useLootWebhook = Config.Bind("Discord", "UseGoodLootWebhook", false, "Host only: route ordinary-loot clips to GoodLootWebhookURL; when off, use WebhookURL.");
                lootWebhook = Config.Bind("Discord", "GoodLootWebhookURL", "", "Host-only secret: optional ordinary-loot destination.");
                useDeathWebhook = Config.Bind("Discord", "UsePlayerDeathWebhook", false, "Host only: route death clips to PlayerDeathWebhookURL; when off, use WebhookURL.");
                deathWebhook = Config.Bind("Discord", "PlayerDeathWebhookURL", "", "Host-only secret: optional player-death destination.");
                uploadLimitMiB = Config.Bind("Discord", "MaxUploadMiB", 10, "Per-file upload guard. Discord can impose its own limit. Allowed range 1–100.");
                manualTrigger = Config.Bind("Triggers", "ManualCapture", true, "Enable the manual hotkey independently of automatic events.");
                deathTrigger = Config.Bind("Triggers", "PlayerDeath", true, "Enable local player death captures.");
                deathEnabled = Config.Bind("Player Death", "Enabled", true, "Enable this event. Triggers.PlayerDeath must also be enabled.");
                deathMessage = Config.Bind("Player Death", "Message", "\uD83D\uDC80 {player} died!", "Discord death message. Placeholders: {player}, {cause}. Cause appends automatically when enabled and no placeholder is present.");
                includeCause = Config.Bind("Player Death", "IncludeCause", true, "Include the recorded attacker or environmental cause; unknown when unavailable.");
                includePlayerName = Config.Bind("Player Death", "IncludePlayerName", true, "Replace {player} with the character name/override; otherwise use A player.");
                playerNameOverride = Config.Bind("Player Death", "PlayerNameOverride", "", "Optional display name instead of the character name.");
                bossTrigger = Config.Bind("Triggers", "BossKill", true, "Capture boss kills credited by Valheim to this character.");
                bossEnabled = Config.Bind("Boss Kill", "Enabled", true, "Enable boss capture; Triggers.BossKill must also be enabled.");
                firstBossOnly = Config.Bind("Boss Kill", "FirstKillOnly", false, "Only capture when this character has no previous kill of this boss in saved game statistics.");
                bossMessage = Config.Bind("Boss Kill", "Message", "\uD83C\uDFC6 {boss} defeated!", "Discord boss message. Supported placeholders: {boss}, {player}. Loot placeholders: {loot}, {item_count}.");
                bossPostSeconds = Config.Bind("Boss Kill", "PostEventSeconds", 4.0, "Seconds to record after a credited boss kill, to show loot dropping. Restart after changing. Longer clips must fit Capture.MemoryBudgetMiB and the encoder's 256 MiB raw-frame limit.").Value;
                bossNameMode = Config.Bind("Boss Kill", "PlayerNameMode", BossNameMode.Both, "KillCredit, FinalBlow or Both. Kill credit names this character; final blow names the last-hit player when available. Co-op final-blow sharing requires this version on the client owning the boss. Templates support {credit} and {killer}; selected names append when placeholders are absent.");
                showBossLoot = Config.Bind("Boss Kill", "ShowLoot", true, "Show observed vanilla rolls and completed Epic Loot drops when its optional adapter is available.");
                showLootQuantity = Config.Bind("Boss Kill", "ShowQuantity", true, "Show item quantities in the boss loot summary.");
                maxLootItems = Config.Bind("Boss Kill", "MaxLootItemsShown", 5, "Maximum entries shown, 1-20; highest verified rarity first, then name. Distinct magic items stay separate.");
                lootHeader = Config.Bind("Boss Kill", "LootHeader", "Generated loot:", "Message placeholders {loot} and {item_count}; count is displayed-data entries before the display limit (grouped vanilla types and individual Epic Loot items).");
                showRarity = Config.Bind("Boss Kill", "ShowRarity", true, "Show verified rarity names and approximate color emojis.");
                showModifiers = Config.Bind("Boss Kill", "ShowItemModifiers", true, "Show Epic Loot's formatted modifiers for identified items.");
                showSockets = Config.Bind("Boss Kill", "ShowItemSockets", true, "Show verified socket counts for identified items.");
                showUnidentified = Config.Bind("Boss Kill", "ShowUnidentifiedStatus", true, "Label unidentified items. Hidden modifiers are never exposed.");
                lootWaitSeconds = Config.Bind("Boss Kill", "LootWaitSeconds", 12.0, "Maximum seconds from boss kill to wait for delayed Epic Loot before upload, clamped 0-25. Recording duration remains PostEventSeconds.");
                filterBossLoot = Config.Bind("Boss Kill", "OnlyCaptureIfLootMeetsRarity", false, "Only encode/save/upload a boss clip when at least one observed item meets MinimumLootRarity. Preserve kill footage while awaiting drops. Missing/unknown qualifying data skips the clip at the wait deadline.");
                minimumBossRarity = Config.Bind("Boss Kill", "MinimumLootRarity", "Legendary", "None accepts all; otherwise an actual Epic Loot rarity name (0.14.2: Magic, Rare, Epic, Legendary, Mythic, Ancient). Used only when OnlyCaptureIfLootMeetsRarity=true. Unknown names or absent Epic Loot fail closed.");
                firstKillBypassesRarity = Config.Bind("Boss Kill", "FirstKillBypassesRarity", true, "Always keep this character's first recorded kill of each boss regardless of MinimumLootRarity. Repeat kills still use the rarity filter. FirstKillOnly separately excludes all repeat kills.");
                lootTrigger = Config.Bind("Triggers", "LootDrop", true, "Enable ordinary-creature loot highlights. Loot Capture.Enabled must also be enabled; bosses use Boss Kill rules exclusively.");
                lootEnabled = Config.Bind("Loot Capture", "Enabled", true, "Capture qualifying Epic Loot drops from ordinary kills credited to this character. Requires the optional Epic Loot adapter. No pickup/crafting triggers.");
                minimumLootRarity = Config.Bind("Loot Capture", "MinimumRarity", "Legendary", "Minimum observed rarity: Magic, Rare, Epic, Legendary, Mythic, Ancient. None accepts any observed item. Unknown names skip captures.");
                highlightMessage = Config.Bind("Loot Capture", "Message", "Great loot from {enemy}!", "Placeholders: {enemy}, {player}, {loot}, {item_count}. Loot and kill credit append if omitted. Item display is configured independently in this section.");
                // Seed new independent entries from the existing display preferences on upgrade.
                highlightQuantity = Config.Bind("Loot Capture", "ShowQuantity", showLootQuantity.Value, "Show item quantities in ordinary-loot posts.");
                highlightRarity = Config.Bind("Loot Capture", "ShowRarity", showRarity.Value, "Show rarity labels and colored markers; does not change MinimumRarity filtering.");
                highlightModifiers = Config.Bind("Loot Capture", "ShowItemModifiers", showModifiers.Value, "Show identified items' Epic Loot modifier text in ordinary-loot posts.");
                highlightSockets = Config.Bind("Loot Capture", "ShowItemSockets", showSockets.Value, "Show identified items' socket counts in ordinary-loot posts.");
                highlightUnidentified = Config.Bind("Loot Capture", "ShowUnidentifiedStatus", showUnidentified.Value, "Label unidentified items. Hidden modifiers and sockets are never revealed.");
                highlightMaxItems = Config.Bind("Loot Capture", "MaxLootItemsShown", maxLootItems.Value, "Maximum displayed entries, 1-20; highest rarity first. Display limits do not affect capture eligibility.");
                highlightHeader = Config.Bind("Loot Capture", "LootHeader", lootHeader.Value, "Header above generated loot in ordinary-loot posts.");
                highlightWaitSeconds = Config.Bind("Loot Capture", "LootWaitSeconds", 12.0, "Wait 0-25 seconds after credited kill for drops. Holds metadata only; up to 64 pending kills.");
                lootPostSeconds = Config.Bind("Loot Capture", "PostEventSeconds", 4.0, "Seconds after observing qualifying loot. Uses rolling pre-event footage before the drop; long ragdoll delays may leave the kill outside the clip. Restart after changing.").Value;
                width = Setting("Width", 640, "Output pixel width, 16–1920.");
                height = Setting("Height", 360, "Output pixel height, 16–1080. Screen is stretched to this aspect ratio.");
                fps = Setting("FPS", 15, "Capture sampling rate, 1–30. Missed captures are skipped.");
                quality = Setting("WebPQuality", 80, "Lossy animated WebP quality, 1–100.");
                double pre = Setting("PreEventSeconds", 5.0, "History duration.");
                double post = Setting("PostEventSeconds", 2.0, "Post-trigger duration.");
                int budget = Setting("MemoryBudgetMiB", 192, "Maximum preallocated managed frame pool; excludes GPU and helper memory.");
                string pluginDirectory = Path.GetDirectoryName(Info.Location);
                relayDirectory = Path.Combine(pluginDirectory, "RelayTemp");
                relay = new ClipRelay(CanRelay, () => Math.Max(1, Math.Min(10, uploadLimitMiB.Value)) * 1048576,
                    ReceiveRelayedClip, message => Logger.LogInfo("[Relay] " + message));
                encoderPath = Path.Combine(pluginDirectory, "Encoder", "ValheimEventClips.Encoder.exe");
                outputDirectory = Path.Combine(pluginDirectory, "Clips");
                if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                {
                    Logger.LogInfo("[Relay] Host delivery ready; graphics capture disabled on this headless server.");
                    return;
                }
                if (!SystemInfo.supportsAsyncGPUReadback) throw new NotSupportedException("This graphics backend does not support asynchronous GPU readback.");
                if (!File.Exists(encoderPath)) throw new FileNotFoundException("Bundled Encoder/ValheimEventClips.Encoder.exe is missing.");
                if (width < 16 || width > 1920 || height < 16 || height > 1080 || fps < 1 || fps > 30 || quality < 1 || quality > 100 || budget < 16 || budget > 512)
                    throw new ArgumentOutOfRangeException("Capture configuration is outside prototype limits.");
                double maxPost = Math.Max(post, Math.Max(bossPostSeconds, lootPostSeconds));
                if (double.IsNaN(lootPostSeconds) || double.IsInfinity(lootPostSeconds) || lootPostSeconds < 0)
                    throw new ArgumentOutOfRangeException("Loot Capture.PostEventSeconds");
                if (double.IsNaN(bossPostSeconds) || double.IsInfinity(bossPostSeconds) || bossPostSeconds < 0)
                    throw new ArgumentOutOfRangeException("Boss Kill.PostEventSeconds");
                if ((long)width * height * 4 * (Math.Ceiling(pre * fps) + Math.Ceiling(maxPost * fps)) > 256L * 1024 * 1024)
                    throw new ArgumentOutOfRangeException("Clip exceeds the encoder's 256 MiB raw-frame limit.");
                history = new CaptureBuffer(width, height, fps, pre, post, budget * 1024L * 1024, maxPost);
                scratch = new byte[checked(width * height * 4)];
                for (int i = 0; i < 3; i++)
                {
                    var slot = new ReadbackSlot();
                    allSlots.Add(slot); // Teardown also covers partial initialization.
                    slot.Target = MakeTarget(width, height);
                    slot.Pixels = new NativeArray<byte>(scratch.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    free.Enqueue(slot);
                }
                initialized = true;
                try
                {
                    deathHarmony = new Harmony("local.valheimeventclips.death");
                    PlayerDeathDetector.OnLocalDeath = OnLocalDeath;
                    PlayerDeathDetector.OnError = () => Logger.LogWarning("[Death] Could not inspect local death; gameplay was left unchanged.");
                    PlayerDeathDetector.Install(deathHarmony);
                    Logger.LogInfo("[Death] Local player death detector installed.");
                }
                catch (Exception error) { Logger.LogWarning("[Death] Detector unavailable: " + error.GetType().Name + ". Manual capture remains available."); }
                try
                {
                    bossHarmony = new Harmony("local.valheimeventclips.boss");
                    BossKillDetector.OnKill = OnBossKill;
                    BossKillDetector.ObserveOrdinary = () => epicReady && lootTrigger.Value && lootEnabled.Value;
                    BossKillDetector.OnLootKill = kill => {
                        if (captureEnabled.Value && !paused) lootHighlights.Add(kill, clock.Elapsed.TotalSeconds, highlightWaitSeconds.Value);
                    };
                    BossKillDetector.OnError = () => Logger.LogWarning("[Boss] Unable to verify character kill statistics; boss event skipped.");
                    BossKillDetector.Install(bossHarmony);
                    Logger.LogInfo("[Boss] Local kill-credit detector installed.");
                }
                catch (Exception error) { Logger.LogWarning("[Boss] Detector unavailable: " + error.GetType().Name + ". Other captures remain available."); }
                try
                {
                    attributionHarmony = new Harmony("local.valheimeventclips.boss.attribution");
                    BossAttribution.OnDiagnostic = reason => Logger.LogInfo("[Boss] Final-blow source: " + reason);
                    BossAttribution.Install(attributionHarmony);
                    Logger.LogInfo("[Boss] Final-blow attribution installed.");
                }
                catch (Exception error) { Logger.LogWarning("[Boss] Final-blow attribution unavailable: " + error.GetType().Name); }
                try
                {
                    lootHarmony = new Harmony("local.valheimeventclips.boss.loot");
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
            { name = "ValheimEventClips", antiAliasing = 1, useMipMap = false, filterMode = FilterMode.Bilinear };
            if (!result.Create()) { Destroy(result); throw new InvalidOperationException("RenderTexture creation failed"); }
            return result;
        }

        private void Update()
        {
            if (stopped) return;
            try
            {
                if (relay != null && !relayEnabled.Value) relay.StopSending();
                relay?.Tick(clock.Elapsed.TotalSeconds);
                if (uploadCancellation != null && upload != null && !upload.IsCompleted &&
                    (!DiscordRouting.CanSubmit(uploadSession, true, ZNet.instance, ZNet.instance != null && ZNet.instance.IsServer()) ||
                    (relayCompletion != null && (!relayEnabled.Value || !discordEnabled.Value || !uploadClips.Value || !relay.DeliveryPeerConnected))))
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
                if (Input.GetKeyDown(toggleKey.Value))
                {
                    paused = !paused;
                    Logger.LogInfo(paused ? "[Capture] Paused for baseline comparison." : "[Capture] Recording resumed; allow 5 seconds to warm up.");
                }
                DrainReadbacks();
                bool active = captureEnabled.Value && !paused;
                if (active && epicReady && lootTrigger.Value && lootEnabled.Value)
                    lootHighlights.Poll(now, minimumLootRarity.Value, OnLootHighlight);
                else lootHighlights.Clear();
                if (!active)
                {
                    if (!historyCleared && pending.Count == 0) { history.ClearHistory(); historyCleared = true; }
                }
                else
                {
                    historyCleared = false;
                    if (manualTrigger.Value && Application.isFocused && Input.GetKeyDown(captureKey.Value))
                    {
                        Trigger("manual", "Valheim moment");
                    }
                }
                if (encoding != null && encoding.IsCompleted && (!encoding.IsCompletedSuccessfully || activeBoss?.Loot == null || !activeBoss.Loot.Pending || (activeBoss.BossNumber > 0 && !showBossLoot.Value) || now >= lootDeadline))
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
                        else if (pendingBoss != null && pendingBoss.BossNumber > 0 && filterBossLoot.Value) waitingForLoot = clip;
                        else StartEncoding(clip);
                    }
                }
                if (waitingForLoot != null)
                {
                    string reason;
                    var decision = BossLootFilter.Evaluate(pendingBoss?.Loot, filterBossLoot.Value, pendingBoss != null && pendingBoss.FirstKill, firstKillBypassesRarity.Value, minimumBossRarity.Value, now >= lootDeadline, out reason);
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
                if (timing.Value && now - lastReport >= 10)
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
                if (!captureEnabled.Value || paused || !Application.isFocused) continue;
                try { SubmitCapture(); }
                catch (Exception error) { Logger.LogError("[Capture] Submission failed: " + error.Message); StopCapture(); }
            }
        }

        private void SubmitCapture()
        {
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
            activeKind = pendingKind; activeSession = pendingSession; activeAsHost = pendingAsHost;
            activeBoss = pendingBoss; pendingBoss = null;
            activeOutput = Path.Combine(outputDirectory, pendingKind + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".webp");
            string destination = activeOutput;
            bool flipImage = flip.Value;
            int before = 0;
            for (int i = 0; i < clip.Count; i++) if (clip.GetTimestamp(i) < clip.TriggerTime) before++;
            Logger.LogInfo(string.Format("[WebP] Encoding {0} frames in background helper; pre-event={1}, post-event={2}.", clip.Count, before, clip.Count - before));
            encoding = Task.Run(() => EncoderClient.Encode(clip, encoderPath, destination, width, height, quality, flipImage, shutdown.Token));
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
                if (!relayEnabled.Value || !relay.Offer(activeSession, file, activeKind, message))
                    Logger.LogInfo("[Relay] Clip retained locally: relay disabled, unavailable, busy or clip exceeds 10 MiB.");
                return;
            }
            if (!DiscordRouting.CanSubmit(activeSession, activeAsHost, session, session != null && session.IsServer()))
            {
                Logger.LogInfo("[Discord] Local clip retained: changed or ended sessions cannot submit old clips.");
                return;
            }
            if (!discordEnabled.Value || !uploadClips.Value) return;
            if (upload != null)
            {
                Logger.LogWarning("[Discord] Upload busy; new clip retained locally.");
                return;
            }
            // Snapshot config on Unity's thread; perform all HTTP/file work on a worker.
            var options = new DiscordOptions {
                WebhookUrl = DiscordRouting.Destination(activeKind, webhookUrl.Value,
                    useBossWebhook.Value, bossWebhook.Value, useLootWebhook.Value, lootWebhook.Value, useDeathWebhook.Value, deathWebhook.Value),
                Username = discordUsername.Value,
                Message = EventMessages.FormatPost(activeBoss == null ? activeMessage : BossMessage(activeBoss)),
                SaveLocalCopy = saveLocalCopy.Value,
                MaxUploadBytes = Math.Max(1, Math.Min(100, uploadLimitMiB.Value)) * 1048576L
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
            return DiscordRouting.Destination(kind, webhookUrl.Value, useBossWebhook.Value, bossWebhook.Value,
                useLootWebhook.Value, lootWebhook.Value, useDeathWebhook.Value, deathWebhook.Value);
        }
        private bool CanRelay(string kind)
        {
            if (!relayEnabled.Value || !discordEnabled.Value || !uploadClips.Value || upload != null) return false;
            if (kind == "manual" && !manualTrigger.Value) return false;
            if (kind == "boss" && (!bossTrigger.Value || !bossEnabled.Value)) return false;
            if (kind == "loot" && (!lootTrigger.Value || !lootEnabled.Value)) return false;
            if (kind == "death" && (!deathTrigger.Value || !deathEnabled.Value)) return false;
            Uri endpoint;
            return RelayProtocol.ValidKind(kind) && DiscordWebhook.TryEndpoint(Destination(kind), out endpoint);
        }
        private void ReceiveRelayedClip(RelayBuffer clip, string recorder, Action<bool> completion)
        {
            var session = ZNet.instance;
            if (session == null || !session.IsServer() || !CanRelay(clip.Kind)) { completion(false); return; }
            string name = (recorder ?? "Connected player").Replace("\r", " ").Replace("\n", " ").Replace("*", "").Replace("`", "");
            if (name.Length > 80) name = name.Substring(0, 80);
            string message = clip.Message;
            int firstLine = message.IndexOf('\n');
            if (firstLine < 0) firstLine = message.Length;
            message = message.Insert(firstLine, "\n**Recorded by:** " + name);
            var options = new DiscordOptions { WebhookUrl = Destination(clip.Kind), Username = discordUsername.Value,
                Message = EventMessages.FormatPost(message), SaveLocalCopy = true,
                MaxUploadBytes = Math.Max(1, Math.Min(10, uploadLimitMiB.Value)) * 1048576L };
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
                    return new UploadResult { Success = result.Success, Message = result.Success ? "Client clip uploaded; client retains original." : "Client clip delivery failed; client retains original." };
                }
                catch { return new UploadResult { Success = false, Message = "Client clip delivery cancelled or failed; client retains original." }; }
                finally { try { if (File.Exists(file)) File.Delete(file); } catch { } }
            });
        }
        private void OnLootHighlight(BossKill kill)
        {
            if (!Trigger("loot", "", lootPostSeconds)) return;
            pendingBoss = kill;
            lootDeadline = clock.Elapsed.TotalSeconds + Math.Max(0, Math.Min(25, double.IsNaN(highlightWaitSeconds.Value) ? 12 : highlightWaitSeconds.Value));
            Logger.LogInfo("[Loot] Qualifying ordinary-creature drop captured; minimum=" + minimumLootRarity.Value);
        }
        private void OnBossKill(BossKill kill)
        {
            if (!bossTrigger.Value || !bossEnabled.Value) return;
            Logger.LogInfo("[Boss] Kill credited; boss number=" + kill.BossNumber + ", first kill=" + kill.FirstKill);
            Logger.LogInfo("[Boss] Capture rules: FirstKillOnly=" + firstBossOnly.Value + ", rarity filter=" + filterBossLoot.Value + ", minimum=" + minimumBossRarity.Value + ", first-kill bypass=" + firstKillBypassesRarity.Value);
            if (firstBossOnly.Value && !kill.FirstKill)
            {
                Logger.LogInfo("[Boss] Repeat kill skipped by FirstKillOnly.");
                return;
            }
            if (Trigger("boss", "", bossPostSeconds))
            {
                pendingBoss = kill;
                double wait = lootWaitSeconds.Value;
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
                    loot = EventMessages.Heading(highlightHeader.Value, 2) + "\n" + (kill.Loot == null ? "unavailable" : kill.Loot.Display(highlightMaxItems.Value, highlightQuantity.Value, text => Localization.instance.Localize(text), highlightRarity.Value, highlightModifiers.Value, highlightSockets.Value, highlightUnidentified.Value));
                else if (showBossLoot.Value)
                    loot = EventMessages.Heading(lootHeader.Value, 2) + "\n" + (kill.Loot == null ? "unavailable" : kill.Loot.Display(maxLootItems.Value, showLootQuantity.Value, text => Localization.instance.Localize(text), showRarity.Value, showModifiers.Value, showSockets.Value, showUnidentified.Value));
                string count = kill.Loot != null && kill.Loot.Observed ? kill.Loot.Items.Count.ToString() : "unknown";
                if (highlight) return EventMessages.Loot(highlightMessage.Value, Localization.instance.Localize(kill.EnemyKey), kill.PlayerName, loot, count);
                return EventMessages.Boss(bossMessage.Value, Localization.instance.Localize(kill.EnemyKey), kill.PlayerName, bossNameMode.Value, kill.FinalBlowName, loot, count);
            }
            catch
            {
                Logger.LogWarning("[Loot] Message enrichment failed; sending boss names only.");
                return kill.BossNumber <= 0 ? EventMessages.Loot(highlightMessage.Value, kill.EnemyKey, kill.PlayerName, "unavailable") : EventMessages.Boss(bossMessage.Value, kill.EnemyKey, kill.PlayerName, bossNameMode.Value, kill.FinalBlowName);
            }
        }
        private void OnLocalDeath(Player player, string cause)
        {
            if (!deathTrigger.Value || !deathEnabled.Value) return;
            string message = EventMessages.Death(deathMessage.Value, includePlayerName.Value, playerNameOverride.Value,
                includePlayerName.Value ? player.GetPlayerName() : "", includeCause.Value, cause);
            Trigger("death", message);
        }

        private bool Trigger(string kind, string message, double? postOverride = null)
        {
            if (!initialized || stopped || !captureEnabled.Value || paused) return false;
            if (!history.TryTrigger(clock.Elapsed.TotalSeconds, postOverride))
            {
                Logger.LogInfo("[Capture] " + kind + " trigger ignored: a clip is collecting or encoding.");
                return false;
            }
            pendingBoss = null;
            pendingSession = ZNet.instance;
            pendingAsHost = pendingSession != null && pendingSession.IsServer();
            pendingKind = kind; pendingMessage = message;
            Logger.LogInfo("[Capture] " + kind + " event triggered; buffered frames: " + history.BufferedFrames);
            return true;
        }
        private void OnDisable() { if (initialized || relay != null) StopCapture(); }
        private void StopCapture()
        {
            if (stopped) return;
            stopped = true;
            relay?.Dispose();
            PlayerDeathDetector.OnLocalDeath = null;
            PlayerDeathDetector.OnError = null;
            BossKillDetector.OnKill = null;
            BossKillDetector.OnLootKill = null; BossKillDetector.ObserveOrdinary = null;
            lootHighlights.Clear();
            BossKillDetector.OnError = null;
            BossAttribution.Clear();
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
