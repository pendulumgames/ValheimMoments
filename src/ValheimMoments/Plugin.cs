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
    [BepInPlugin("local.valheimmoments", "Valheim Moments", "0.23.2")]
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
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
        private MomentGallery gallery;
        private MomentGallery.Entry activeGallery, uploadGallery;
        private ConfigEntry<KeyCode> galleryKey, keepKey;
        private ConfigEntry<int> recoveryCount, recoveryMiB, recoveryHours, galleryCount;
        private bool galleryOpen, pendingKeep;
        private Vector2 galleryScroll;
        private double nextGalleryTick;
        private string retryConfirmation;
        private readonly Dictionary<string, Texture2D> galleryPreviews = new Dictionary<string, Texture2D>();
        private string previewId;
        private int galleryPage, previewGeneration, pendingPreviewGeneration;
        private Harmony galleryHarmony;
        private Task<byte[]> previewLoad;
        private byte[] raidPreview;
        private ConfigEntry<bool> discordEnabled, saveLocalCopy;
        private ConfigEntry<string> webhookUrl, discordUsername;
        private ConfigEntry<bool> useBossWebhook, useLootWebhook, useDeathWebhook;
        private ConfigEntry<string> bossWebhook, lootWebhook, deathWebhook;
        private ZNet pendingSession, activeSession, uploadSession;
        private bool pendingAsHost, activeAsHost;
        private string activeKind;
        private CancellationTokenSource uploadCancellation;
        private ClipRelay relay;
        private HostHighlightQueue highlightQueue;
        private ConfigEntry<bool> directorEnabled;
        private ConfigEntry<int> directorPerspectives, directorPostMiB;
        private ConfigEntry<double> directorWindow;
        private string directorSignature;
        private ZNet directorSession;
        private double nextDirectorCheck;
        private Task<int> localInspection;
        private Action<int> localInspected;
        private Action<RelayOutcome> relayCompletion;
        private string relayDirectory;
        private ConfigEntry<int> uploadLimitMiB;
        private ConfigEntry<bool> manualTrigger, deathTrigger, deathEnabled, includePlayerName, includeCause;
        private ConfigEntry<string> deathMessage, discordUserId;
        private ConfigEntry<int> deathCaptureLimit;
        private ConfigEntry<double> deathWindowSeconds, notificationVolume;
        private ConfigEntry<bool> cheekyDeaths, notificationsEnabled;
        private ConfigEntry<MomentSoundMode> notificationSound;
        private ConfigEntry<MomentNotificationStyle> notificationStyle;
        private readonly MomentNotifications notifications = new MomentNotifications();
        private readonly DeathMoments deathMoments = new DeathMoments();
        private readonly DeathFlavor deathFlavor = new DeathFlavor();
        private readonly System.Random flavorRandom = new System.Random();
        private DeathMoments.Ticket pendingDeath, activeDeath, uploadDeath;
        private ZNet deathOfferSession;
        private readonly Dictionary<ZRpc, MomentRateLimit> deathOffers = new Dictionary<ZRpc, MomentRateLimit>();
        private ConfigEntry<bool> discoveryEnabled, discoverBiomes, discoverSubBiomes, discoverLocations, discoverTraders;
        private ConfigEntry<string> discoveryWebhook, discoveryMessage, specialKeys, specialMessage;
        private ConfigEntry<BossCaptureMode> specialCaptureMode;
        private ConfigEntry<BossNameMode> specialNameMode;
        private ConfigEntry<bool> specialShowLoot, specialQuantity, specialRarity, specialModifiers, specialSockets, specialUnidentified, useSpecialWebhook;
        private ConfigEntry<int> specialMaxItems;
        private ConfigEntry<double> specialLootWait;
        private ConfigEntry<string> specialLootHeader, specialMinimumRarity, specialWebhook;
        private ConfigEntry<bool> specialEnabled, logEnemyKeys;
        private ConfigEntry<double> discoveryPostSetting, discoveryCooldown, specialPostSetting, specialCooldown;
        private double discoveryPostSeconds, specialPostSeconds, discoveryStarted, discoveryDeadline, nextDiscovery, nextDiscoveryError;
        private readonly List<string> discoveredNames = new List<string>();
        private readonly DiscordIdentity discordIdentity = new DiscordIdentity();
        private DiscoveryHistory discoveryHistory;
        private sealed class SavedDiscovery { internal string Kind, Display; internal Task Save; }
        private readonly List<SavedDiscovery> savingDiscoveries = new List<SavedDiscovery>();
        private Harmony discoveryHarmony;
        private readonly SpecialEnemies specialEnemies = new SpecialEnemies();
        private ConfigEntry<bool> closeEnabled;
        private ConfigEntry<double> closeThreshold, closeRecovery, closeHold, closeCooldown;
        private CloseCall closeCall;
        private CloseCallTimeline closeTimeline = new CloseCallTimeline();
        private ConfigEntry<double> closeFollow, closeSlowSource, closeSlowPlayback, closePlayback, raidOpening, raidEndingSeconds;
        private Harmony closeHarmony;
        private Player closePlayer;
        private double appliedCloseThreshold, appliedCloseRecovery, appliedCloseHold, appliedCloseCooldown;
        private bool closeCollecting, closeSurvived;
        private double appliedMaximumPost;
        private ConfigEntry<bool> raidsEnabled;
        private readonly RaidMoment raidMoment = new RaidMoment();
        private RaidMedia raidMedia;
        private Harmony raidHarmony;
        private Harmony raidIdentityHarmony;
        private RandomEvent raidOccurrence;
        private string raidEventId, activeEventId;
        private Task<string> raidWorker;
        private CaptureBuffer.Clip raidClip;
        private bool raidCollecting, raidEnding, raidComposing;
        private bool raidKeep;
        private Player raidPlayer;
        private ZNet raidSession;
        private string raidRecorder;
        private Harmony deathHarmony;
        private Harmony bossHarmony;
        private ConfigEntry<bool> bossTrigger, bossEnabled;
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
        private ConfigEntry<BossCaptureMode> bossCaptureMode;
        private ConfigEntry<CaptureSizePreset> sizePreset;
        private int sourceWidth, sourceHeight;
        private readonly Dictionary<ConfigEntryBase, string> dimensionDrafts = new Dictionary<ConfigEntryBase, string>();
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
            var tags = new HostConfiguration.ConfigurationManagerAttributes { Order = SettingOrder(section, key) };
            if (section == "Capture" && (key == "Width" || key == "Height")) tags.CustomDrawer = DrawCustomDimension;
            if (section == "Notifications" && key == "Style") tags.CustomDrawer = entry => {
                var setting = (ConfigEntry<MomentNotificationStyle>)entry;
                int selected = GUILayout.SelectionGrid((int)setting.Value, new[] { "Cinematic", "Toast: Minimap", "Toast: Top Right" }, 1);
                if (selected != (int)setting.Value) setting.Value = (MomentNotificationStyle)selected;
            };
            var allTags = new List<object>(description.Tags); allTags.Add(tags);
            var entry = Config.Bind(section, key, value, new ConfigDescription(description.Description,
                SettingRanges.For(section, key, value) ?? description.AcceptableValues, allTags.ToArray()));
            hostSettings.Register(entry, tags);
            return entry;
        }
        private static int SettingOrder(string section, string key)
        {
            string[] order = { "Enabled", "ManualCaptureKey", "ToggleCaptureKey", "SaveLocalCopy", "SizePreset", "Width", "Height", "FPS", "WebPQuality", "MemoryBudgetMiB", "FlipVertically" };
            int index = Array.IndexOf(order, key);
            return section == "Capture" && index >= 0 ? 1000 - index : 0;
        }
        private void DrawHostSetting(ConfigEntryBase entry)
        {
            bool enabled = GUI.enabled;
            try { GUI.enabled = false; GUILayout.Label("Host controlled: " + hostSettings.Display(entry)); }
            finally { GUI.enabled = enabled; }
        }
        private void DrawCustomDimension(ConfigEntryBase entry)
        {
            if (sizePreset == null || Value(sizePreset) != CaptureSizePreset.Custom)
            { GUILayout.Label("Preset selected; effective capture " + width + "x" + height + " at " + fps + " FPS"); return; }
            var number = (ConfigEntry<int>)entry;
            string draft;
            if (!dimensionDrafts.TryGetValue(entry, out draft)) draft = number.Value.ToString();
            GUILayout.BeginHorizontal();
            try
            {
                draft = GUILayout.TextField(draft);
                if (GUILayout.Button("Apply", GUILayout.Width(55)))
                {
                    int parsed;
                    if (int.TryParse(draft, out parsed)) { number.Value = parsed; draft = number.Value.ToString(); }
                }
            }
            finally { GUILayout.EndHorizontal(); }
            dimensionDrafts[entry] = draft;
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
                var migration = new ConfigurationMigration(Config.ConfigFilePath);
                hostSettings = new HostConfiguration(DrawHostSetting,
                    RefreshConfigurationManager, () => { captureSettingsDirty = true; captureSettingsChangedAt = clock.Elapsed.TotalSeconds; }, message => Logger.LogInfo("[Settings] " + message));
                captureEnabled = Bind("Capture", "Enabled", true, "Enable recording. F9 toggles recording for baseline comparison.");
                captureKey = Bind("Capture", "ManualCaptureKey", KeyCode.F10, "Save recent gameplay plus post-event footage locally.");
                toggleKey = Bind("Capture", "ToggleCaptureKey", KeyCode.F9, "Pause/resume recording to compare game performance.");
                notificationsEnabled = Bind("Notifications", "Enabled", true, "Show brief saving and delivery feedback on this player's screen. Disabling also mutes notification cues. Notifications draw after the capture copy to keep them out of recorded footage.");
                notificationStyle = Bind("Notifications", "Style", MomentNotificationStyle.ToastMinimap, "Cinematic: large centered banner. ToastMinimap (Toast: Minimap): follows the minimap with a top-right fallback. ToastTopRight (Toast: Top Right): inset corner toast. Local preference.");
                notificationSound = Bind("Notifications", "SoundMode", MomentSoundMode.OnCapture, "Quiet local cue: Off, OnCapture, OnCompletion, or Both. Never changes game speed or adds audio to WebP clips.");
                notificationVolume = Bind("Notifications", "Volume", 0.35, "Local notification cue volume, 0-1. The cue itself is deliberately quiet.");
                timing = Bind("Debug", "LogCaptureTiming", false,
                    new ConfigDescription("Troubleshooting: log aggregate CPU timing, readback latency and frame counts every 10 seconds.", null, "Advanced"));
                flip = Bind("Capture", "FlipVertically", false, "Enable if the test WebP is upside down on your graphics backend.");
                discordEnabled = Bind("Discord", "Enabled", true, "Host/single-player only: enable Discord delivery. Remote clients send clips to the host and never use local webhook settings.");
                saveLocalCopy = Bind("Capture", "SaveLocalCopy", migration.SaveLocalCopy, "Keep clips on this computer, including when host Discord delivery is disabled. When false, successful unpinned uploads expire after a 30-second Keep grace; failed delivery uses bounded gallery recovery. Both delivery and local saving off means no clip is recorded.");
                galleryKey = Bind("Gallery", "GalleryKey", KeyCode.F8, "Open or close your personal moments gallery.");
                keepKey = Bind("Gallery", "KeepMomentKey", KeyCode.F7, "Permanently keep the current or latest memory, including during capture/upload and 30 seconds after completion.");
                recoveryCount = Bind("Gallery", "RecoveryClips", 20, "Maximum completed unpinned recovery clips, 1-100. Active work and the 30-second Keep grace are excluded.");
                recoveryMiB = Bind("Gallery", "RecoveryMiB", 250, "Recovery storage budget, 10-1024 MiB. Pinned Saved clips are never automatically removed.");
                recoveryHours = Bind("Gallery", "RecoveryHours", 24, "Unpinned recovery expiry, 1-168 hours.");
                galleryCount = Bind("Gallery", "IndexEntries", 200, "Gallery history entries, 20-1000. Removing history never deletes a pinned file.");
                webhookUrl = Bind("Discord", "WebhookURL", "", "Secret: enter locally, never share this config. HTTPS Discord webhook; optional thread_id query.");
                discordUsername = Bind("Discord", "Username", "Valheim Moments", "Host/single-player only: bot display name, 1–30 characters. Remote client values are ignored.");
                useBossWebhook = Bind("Discord", "UseBossKillWebhook", false, "Host only: route boss clips to BossKillWebhookURL; when off, use WebhookURL.");
                bossWebhook = Bind("Discord", "BossKillWebhookURL", "", "Host-only secret: optional boss destination. An enabled but invalid override keeps the clip locally; it does not silently change channels.");
                useLootWebhook = Bind("Discord", "UseGoodLootWebhook", false, "Host only: route loot clips to GoodLootWebhookURL; when off, use WebhookURL.");
                lootWebhook = Bind("Discord", "GoodLootWebhookURL", "", "Host-only secret: optional loot destination.");
                useDeathWebhook = Bind("Discord", "UsePlayerDeathWebhook", false, "Host only: route death clips to PlayerDeathWebhookURL; when off, use WebhookURL.");
                deathWebhook = Bind("Discord", "PlayerDeathWebhookURL", "", "Host-only secret: optional player-death destination.");
                useSpecialWebhook = Bind("Discord", "UseSpecialEnemyWebhook", false, "Host only: route special enemy clips to SpecialEnemyWebhookURL; otherwise use WebhookURL.");
                specialWebhook = Bind("Discord", "SpecialEnemyWebhookURL", "", "Host-only secret: optional special enemy destination. Invalid enabled overrides keep clips locally.");
                discoveryWebhook = Bind("Discord", "DiscoveryWebhookURL", "", "Host-only secret: discovery destination. Blank uses WebhookURL; a nonblank invalid URL keeps the clip locally.");
                discoveryEnabled = Bind("Discoveries", "Enabled", true, "First tracked discoveries per character per world. Startup, disabled, cooldown and busy visits are remembered without replay. Past exploration cannot be reconstructed.");
                discoverBiomes = Bind("Discoveries", "Biomes", true, "First tracked visit to each main biome and distinct named sub-biome per character and world. Hidden sector modifiers do not trigger repeats. Identity is independent of display language.");
                discoverSubBiomes = Bind("Discoveries", "SubBiomes", false, "Also announce distinct named sub-biomes once per character/world. Requires Biomes. Hidden terrain/decoration modifiers do not count. Visits while disabled are remembered silently, so enabling this does not replay earlier visits.");
                discoverLocations = Bind("Discoveries", "NamedLocations", true, "First physical entry into a location with a game discovery label. Distant map pins do not count.");
                discoverTraders = Bind("Discoveries", "Traders", true, "First approach to a Trader within its greeting range, capped at 30m; no hardcoded NPC list.");
                discoveryPostSetting = Bind("Discoveries", "PostEventSeconds", 3.0, "Seconds after a discovery group. Nearby discoveries group for 1.25 seconds before capture.");
                discoveryCooldown = Bind("Discoveries", "CooldownSeconds", 30.0, "Seconds between discovery captures, 0-3600. Initial warmup is at least five seconds.");
                discoveryMessage = Bind("Discoveries", "Message", "\uD83E\uDDED Discovered {discovery}!", "Supports {discovery} (one name or grouped names) and {player}. Recorded by appends at delivery.");
                closeEnabled = Bind("Close Calls", "Enabled", true, "Capture a damage crossing only after surviving FollowUpSeconds. SlowSourceSeconds plays for SlowPlaybackSeconds; the sampled follow-up fills the remaining PlaybackSeconds. Death cancels it. Uses the default Discord destination.");
                raidsEnabled = Bind("Raids", "Enabled", true, "Record configured opening and ending segments, combined through the default webhook. Compatible participant perspectives can share a director post. Leaving, dying, pausing or changing capture settings abandons it. Raid ended does not imply victory; resets also end raids. Opening expires after 30 minutes.");
                closeThreshold = Bind("Close Calls", "ThresholdPercent", 5.0, "Health percentage crossed by actual damage, 1-15. Starting low or food changes alone do not trigger.");
                closeRecovery = Bind("Close Calls", "RecoveryPercent", 20.0, "Health required before rearming, 16-100 percent, sustained for RecoverySeconds.");
                closeHold = Bind("Close Calls", "RecoverySeconds", 10.0, "Continuous recovery observation before rearming, 1-120 seconds.");
                closeCooldown = Bind("Close Calls", "CooldownSeconds", 120.0, "Minimum seconds between attempts, 0-3600. Sustained recovery is also required.");
                closeFollow = Bind("Close Calls", "FollowUpSeconds", 20.0, "Must survive this many source seconds after the hit, 5-60. Death cancels the clip.");
                closeSlowSource = Bind("Close Calls", "SlowSourceSeconds", 1.0, "Source seconds around the hit, 0.5-3: two thirds before and one third after. Capture history expands to cover this.");
                closeSlowPlayback = Bind("Close Calls", "SlowPlaybackSeconds", 3.0, "Playback seconds for the slow segment, 1-5.");
                closePlayback = Bind("Close Calls", "PlaybackSeconds", 10.0, "Total playback seconds, 6-20. Remaining playback time contains the sampled follow-up.");
                raidOpening = Bind("Raids", "OpeningSeconds", 4.0, "Opening segment duration, 1-10 seconds. Kept compressed until the raid ends.");
                raidEndingSeconds = Bind("Raids", "EndingSeconds", 6.0, "Ending segment duration, 1-10 seconds. Total playback is opening plus ending.");
                specialEnabled = Bind("Special Enemies", "Enabled", true, "Capture selected ordinary-enemy kill credits. Empty EnemyKeys selects none. Normal bosses remain exclusive to Boss Kill.");
                specialKeys = Bind("Special Enemies", "EnemyKeys", "", "Exact case-sensitive kill-credit keys separated by commas/semicolons; up to 64 keys/4096 characters. No wildcards. To find a key: have the host enable Advanced LogEnemyKeys, kill an ordinary enemy and receive kill credit, then find '[Special] Confirmed enemy key:' in that recording player's active profile BepInEx/LogOutput.log. Copy the exact value, including any $, and disable LogEnemyKeys afterward. Example: EnemyKeys = $enemy_troll, $enemy_wraith. Vanilla localization reference: https://valheim-modding.github.io/Jotunn/data/localization/translations/English.html (search for the enemy name). This reference is not a guaranteed kill-credit list for your game/mod version; the logged value is authoritative. These are stat keys, not spawn codes or translated names.");
                specialCaptureMode = Bind("Special Enemies", "CaptureMode", BossCaptureMode.AllKills, "AllKills, FirstKillOnly, RarityOnly, FirstKillThenRarity or FirstKillWithRarity. CaptureMode is the sole capture rule; obsolete FirstKillOnly settings are removed.");
                specialNameMode = Bind("Special Enemies", "PlayerNameMode", BossNameMode.Both, "KillCredit, FinalBlow or Both. Supports {credit} and {killer}; owner metadata is required for final blow and shared credits.");
                specialShowLoot = Bind("Special Enemies", "ShowLoot", true, "Show observed vanilla rolls and completed Epic Loot drops when its optional adapter is available.");
                specialQuantity = Bind("Special Enemies", "ShowQuantity", true, "Show item quantities in the special enemy loot summary.");
                specialMaxItems = Bind("Special Enemies", "MaxLootItemsShown", 5, "Maximum entries shown, 1-20; highest verified rarity first, then name. Distinct magic items stay separate.");
                specialLootHeader = Bind("Special Enemies", "LootHeader", "Generated loot:", "Message placeholders {loot} and {item_count}; count is displayed-data entries before the display limit (grouped vanilla types and individual Epic Loot items).");
                specialRarity = Bind("Special Enemies", "ShowRarity", true, "Show verified rarity names and approximate color emojis.");
                specialModifiers = Bind("Special Enemies", "ShowItemModifiers", true, "Show Epic Loot's formatted modifiers for identified items.");
                specialSockets = Bind("Special Enemies", "ShowItemSockets", true, "Show verified socket counts for identified items.");
                specialUnidentified = Bind("Special Enemies", "ShowUnidentifiedStatus", true, "Label unidentified items. Hidden modifiers are never exposed.");
                specialLootWait = Bind("Special Enemies", "LootWaitSeconds", 12.0, "Maximum seconds from special enemy kill to wait for delayed Epic Loot before upload, clamped 0-25. Recording duration remains PostEventSeconds.");
                specialMinimumRarity = Bind("Special Enemies", "MinimumLootRarity", "Legendary", "None accepts all; otherwise an actual Epic Loot rarity name (0.14.2: Magic, Rare, Epic, Legendary, Mythic, Ancient). Used by rarity-based CaptureMode choices. Unknown names or absent Epic Loot fail closed.");
                specialPostSetting = Bind("Special Enemies", "PostEventSeconds", 4.0, "Seconds after the selected enemy kill to show aftermath and observed loot.");
                specialCooldown = Bind("Special Enemies", "CooldownSeconds", 60.0, "Seconds between captures of the same enemy key, 0-3600.");
                specialMessage = Bind("Special Enemies", "Message", "\u2694 {enemy} defeated!", "Supports {enemy}, {boss}, {player}, {credit}, {killer}, {loot}, {item_count}. PlayerNameMode selects confirmed kill credit, final blow or both.");
                logEnemyKeys = Bind("Special Enemies", "LogEnemyKeys", false, new ConfigDescription("Log exact confirmed ordinary-enemy stat keys for configuring EnemyKeys. Local diagnostic; only affects this player's log.", null, "Advanced"));
                uploadLimitMiB = Bind("Discord", "MaxUploadMiB", 10, "Per-file upload guard. Discord can impose its own limit. Allowed range 1–100.");
                directorEnabled = Bind("Director", "Enabled", true, "Host collects perspectives of the same creature death into one Discord post. Manual and personal events remain separate.");
                directorPerspectives = Bind("Director", "MaxPerspectives", 3, "Maximum perspectives per post, 1-3. Selection is deterministic, not a visual-quality ranking.");
                directorPostMiB = Bind("Director", "MaxPostMiB", 20, "Combined attachment-byte budget per post, 1-30 MiB. Set for your Discord destination. Relay retains its 10 MiB per-file cap; oversize perspectives are omitted.");
                directorWindow = Bind("Director", "CollectionSeconds", 10.0, "Wait 1-30 seconds after the first encoded offer for other players. Slower or late recordings may be omitted.");
                manualTrigger = Bind("Triggers", "ManualCapture", true, "Enable the manual hotkey independently of automatic events.");
                deathTrigger = Bind("Triggers", "PlayerDeath", true, "Enable local player death captures.");
                deathEnabled = Bind("Player Death", "Enabled", true, "Enable this event. Triggers.PlayerDeath must also be enabled.");
                deathCaptureLimit = Bind("Player Death", "CaptureLimit", 1, "Host-controlled maximum death captures per player within WindowSeconds, 1-20. Further deaths are summarized on the next eligible death post. At most one death clip can await delivery per recording player.");
                deathWindowSeconds = Bind("Player Death", "WindowSeconds", 60.0, "Host-controlled sliding death capture window in seconds, 1-3600. Counts clear on world/session change. Failed delivery preserves unreported deaths for the next eligible clip.");
                cheekyDeaths = Bind("Player Death", "CheekyMessages", true, "Add one of 20 neutral cheeky lines to the default death caption, without immediate repeats. Custom templates opt in with {flavor}. {extra_deaths} places the suppressed/unshared death count.");
                deathMessage = Bind("Player Death", "Message", "\uD83D\uDC80 {player} died!", "Discord death message. Placeholders: {player}, {cause}. Cause appends automatically when enabled and no placeholder is present.");
                includeCause = Bind("Player Death", "IncludeCause", true, "Include the recorded attacker or environmental cause; unknown when unavailable.");
                includePlayerName = Bind("Player Death", "IncludePlayerName", true, "Replace {player} with the character name; otherwise use A player.");
                discordUserId = Bind("Player Identity", "DiscordUserID", "", "Optional personal numeric Discord user ID, not a username. Appends a clickable mention beside your character in Recorded by and Kill credit. Blank disables it. Sent to the host for current-session attribution; no Discord account verification is performed.");
                bossTrigger = Bind("Triggers", "BossKill", true, "Capture boss kills credited by Valheim to this character.");
                bossEnabled = Bind("Boss Kill", "Enabled", true, "Enable boss capture; Triggers.BossKill must also be enabled.");
                trackPeriodicSetting = Bind("Boss Kill", "TrackPeriodicDamage", true, "Track actual Spirit/fire/poison effects for final-blow attribution. Mixed or unknown sources remain unavailable. Requires the mod on the creature owner. Restart after changing.");
                bossCaptureMode = Bind("Boss Kill", "CaptureMode", migration.BossMode, "AllKills, FirstKillOnly, RarityOnly, or FirstKillThenRarity (always capture your first kill, then require MinimumLootRarity). FirstKillWithRarity preserves the legacy first-only AND rarity combination. Uses this character's saved boss history.");
                bossMessage = Bind("Boss Kill", "Message", "\uD83C\uDFC6 {boss} defeated!", "Discord boss message. Supported placeholders: {boss}, {player}. Loot placeholders: {loot}, {item_count}.");
                bossPostSetting = Bind("Boss Kill", "PostEventSeconds", 4.0, "Seconds to record after a credited boss kill, to show loot dropping. Restart after changing. Longer clips must fit Capture.MemoryBudgetMiB and the encoder's 256 MiB raw-frame limit.");
                bossNameMode = Bind("Boss Kill", "PlayerNameMode", BossNameMode.Both, "KillCredit, FinalBlow or Both. Kill credit lists players credited by Valheim; final blow names the last-hit player when available. Co-op final-blow sharing requires this version on the client owning the boss. Templates support {credit} and {killer}; selected names append when placeholders are absent.");
                showBossLoot = Bind("Boss Kill", "ShowLoot", true, "Show observed vanilla rolls and completed Epic Loot drops when its optional adapter is available.");
                showLootQuantity = Bind("Boss Kill", "ShowQuantity", true, "Show item quantities in the boss loot summary.");
                maxLootItems = Bind("Boss Kill", "MaxLootItemsShown", 5, "Maximum entries shown, 1-20; highest verified rarity first, then name. Distinct magic items stay separate.");
                lootHeader = Bind("Boss Kill", "LootHeader", "Generated loot:", "Message placeholders {loot} and {item_count}; count is displayed-data entries before the display limit (grouped vanilla types and individual Epic Loot items).");
                showRarity = Bind("Boss Kill", "ShowRarity", true, "Show verified rarity names and approximate color emojis.");
                showModifiers = Bind("Boss Kill", "ShowItemModifiers", true, "Show Epic Loot's formatted modifiers for identified items.");
                showSockets = Bind("Boss Kill", "ShowItemSockets", true, "Show verified socket counts for identified items.");
                showUnidentified = Bind("Boss Kill", "ShowUnidentifiedStatus", true, "Label unidentified items. Hidden modifiers are never exposed.");
                lootWaitSeconds = Bind("Boss Kill", "LootWaitSeconds", 12.0, "Maximum seconds from boss kill to wait for delayed Epic Loot before upload, clamped 0-25. Recording duration remains PostEventSeconds.");
                minimumBossRarity = Bind("Boss Kill", "MinimumLootRarity", "Legendary", "None accepts all; otherwise an actual Epic Loot rarity name (0.14.2: Magic, Rare, Epic, Legendary, Mythic, Ancient). Used by rarity-based CaptureMode choices. Unknown names or absent Epic Loot fail closed.");
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
                sizePreset = Bind("Capture", "SizePreset", migration.CustomSize ? CaptureSizePreset.Custom : CaptureSizePreset.Small, "Six aspect-aware sizes: Tiny 480x270, Small 640x360, Medium 854x480, Balanced 960x540, Large 1280x720, Ultra 1920x1080 at 16:9, plus Custom. Effective dimensions/FPS may reduce for memory. Motion, duration, resolution, FPS and quality affect file size. Discord allowance depends on destination/server boosts, not personal Nitro. Current client relay cap: 10 MiB. Measure actual file size; estimates cannot guarantee upload.");
                widthSetting = Bind("Capture", "Width", 640, "Output pixel width, 480-1920. Aspect ratio and memory limits may reduce the effective dimensions.");
                heightSetting = Bind("Capture", "Height", 360, "Output pixel height, 270-1080. Supported aspect ratios: 1:2 through 3:1.");
                fpsSetting = Bind("Capture", "FPS", 15, "Capture sampling rate, 1-30. Missed samples are skipped.");
                qualitySetting = Bind("Capture", "WebPQuality", 80, "Lossy animated WebP quality, 1-100.");
                preSetting = Bind("Capture", "PreEventSeconds", 5.0, "Host-controlled rolling history, 1-30 seconds.");
                postSetting = Bind("Capture", "PostEventSeconds", 2.0, "Host-controlled manual/death post-event duration, 0-30 seconds.");
                budgetSetting = Bind("Capture", "MemoryBudgetMiB", 192, "Managed frame-pool budget, 48-512 MiB. Effective resolution, then FPS, reduces when required to fit; dimensions never fall below 480x270.");
                ConfigurationMigration.Retire(Config);
                hostSettings.RefreshPresentation();
                string pluginDirectory = Path.GetDirectoryName(Info.Location);
                _ = TemporaryCleanup.Start(pluginDirectory);
                relayDirectory = Path.Combine(pluginDirectory, "RelayTemp");
                relay = new ClipRelay(CanRelay, () => Math.Max(1, Math.Min(10, Value(uploadLimitMiB))) * 1048576,
                    ReceiveRelayedClip, message => Logger.LogInfo("[Relay] " + message), hostSettings.PeerHasPolicy, AcceptRelayedEvent, relayDirectory);
                encoderPath = Path.Combine(pluginDirectory, "Encoder", "ValheimMoments.Encoder.exe");
                outputDirectory = Path.Combine(pluginDirectory, "Clips");
                InitializeCinematics();
                gallery = new MomentGallery(Path.Combine(pluginDirectory, "Gallery"));
                discoveryHistory = new DiscoveryHistory(Path.Combine(Paths.ConfigPath, "ValheimMoments", "Discoveries"),
                    message => Logger.LogWarning("[Discovery] " + message), Path.Combine(pluginDirectory, "State", "Discoveries"));
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
                try { raidIdentityHarmony = new Harmony("local.valheimmoments.raid.identity"); RaidIdentity.Install(raidIdentityHarmony); }
                catch (Exception error) { raidIdentityHarmony?.UnpatchSelf(); Logger.LogWarning("[Raid] Shared identity unavailable: " + error.GetType().Name); }
                if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                {
                    Logger.LogInfo("[Relay] Host delivery ready; graphics capture disabled on this headless server.");
                    return;
                }
                if (!SystemInfo.supportsAsyncGPUReadback) throw new NotSupportedException("This graphics backend does not support asynchronous GPU readback.");
                if (!File.Exists(encoderPath)) throw new FileNotFoundException("Bundled Encoder/ValheimMoments.Encoder.exe is missing.");
                ApplyCaptureSettings();
                initialized = true;
                try { galleryHarmony = new Harmony("local.valheimmoments.gallery.input"); GalleryInput.Open = () => galleryOpen && !stopped; GalleryInput.Install(galleryHarmony); }
                catch (Exception error) { galleryHarmony?.UnpatchSelf(); Logger.LogWarning("[Gallery] Input hooks unavailable: " + error.GetType().Name); }
                try
                {
                    discoveryHarmony = new Harmony("local.valheimmoments.discovery");
                    DiscoveryDetector.OnObserved = OnDiscovery;
                    DiscoveryDetector.OnError = () => {
                        double now = clock.Elapsed.TotalSeconds;
                        if (now < nextDiscoveryError) return;
                        nextDiscoveryError = now + 60;
                        Logger.LogWarning("[Discovery] Unsupported observation skipped.");
                    };
                    DiscoveryDetector.Install(discoveryHarmony);
                }
                catch (Exception error) { discoveryHarmony?.UnpatchSelf(); Logger.LogWarning("[Discovery] Observer unavailable: " + error.GetType().Name); }
                try
                {
                    closeHarmony = new Harmony("local.valheimmoments.closecall");
                    LocalDamageDetector.OnDamage = OnLocalDamage;
                    LocalDamageDetector.Install(closeHarmony);
                }
                catch (Exception error) { closeHarmony?.UnpatchSelf(); LocalDamageDetector.OnDamage = null; Logger.LogWarning("[Close Call] Detector unavailable: " + error.GetType().Name); }
                try
                {
                    raidHarmony = new Harmony("local.valheimmoments.raids");
                    RaidDetector.OnTransition = OnRaidTransition;
                    RaidDetector.Install(raidHarmony);
                }
                catch (Exception error) { raidHarmony?.UnpatchSelf(); RaidDetector.OnTransition = null; Logger.LogWarning("[Raid] Observer unavailable: " + error.GetType().Name); }
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
                    BossKillDetector.ObserveOrdinary = () => (Value(lootTrigger) && Value(lootEnabled)) || Value(specialEnabled) || Value(logEnemyKeys);
                    BossKillDetector.OnLootKill = OnOrdinaryKill;
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
            if (Value(closeEnabled)) pre = Math.Max(pre, Value(closeSlowSource));
            double bossPost = Value(bossPostSetting), lootPost = Value(lootPostSetting);
            double discoveryPost = Value(discoveryPostSetting), specialPost = Value(specialPostSetting);
            double maxPost = Math.Max(Math.Max(discoveryPost, specialPost), Math.Max(post, Math.Max(bossPost, lootPost)));
            if (Value(closeEnabled)) maxPost = Math.Max(maxPost, Math.Max(0, Value(closeSlowSource) + Value(closePlayback) - Value(closeSlowPlayback) + 1 - pre));
            if (Value(raidsEnabled)) maxPost = Math.Max(maxPost, Math.Max(Math.Max(Value(raidOpening), Value(raidEndingSeconds)), Value(raidOpening) + Value(raidEndingSeconds) + 1 - pre));
            int requestedWidth, requestedHeight;
            CaptureSizes.Resolve(Value(sizePreset), Screen.width, Screen.height, Value(widthSetting), Value(heightSetting), out requestedWidth, out requestedHeight);
            sourceWidth = Screen.width; sourceHeight = Screen.height;
            var limits = CaptureLimits.Fit(requestedWidth, requestedHeight, Value(fpsSetting), Value(qualitySetting), Value(budgetSetting), pre, maxPost);
            if (history != null && width == limits.Width && height == limits.Height && fps == limits.FPS &&
                appliedMaximumPost == maxPost && appliedPre == pre && appliedPost == post && bossPostSeconds == bossPost && lootPostSeconds == lootPost && discoveryPostSeconds == discoveryPost && specialPostSeconds == specialPost)
            { quality = limits.Quality; captureSettingsDirty = false; return true; }
            // Let GPU requests and an existing encoder finish before replacing storage.
            if (pending.Count != 0 || encoding != null || raidWorker != null) return false;
            waitingForLoot?.Release(); waitingForLoot = null;
            CancelPendingDeath();
            CancelCloseCall();
            CancelRaid();
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
            appliedMaximumPost = maxPost;
            discoveryPostSeconds = discoveryPost; specialPostSeconds = specialPost;
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
                RaidIdentity.Tick();
                UpdateDirector();
                if (relay != null && !Value(discordEnabled)) relay.StopSending();
                relay?.Tick(clock.Elapsed.TotalSeconds);
                if (localInspection != null && localInspection.IsCompleted)
                { int bytes = localInspection.GetAwaiter().GetResult(); localInspection = null; var inspected = localInspected; localInspected = null; inspected?.Invoke(bytes); }
                highlightQueue?.Tick(ZNet.instance, clock.Elapsed.TotalSeconds);
                if (uploadCancellation != null && upload != null && !upload.IsCompleted &&
                    (!Value(discordEnabled) || !DiscordRouting.CanSubmit(uploadSession, true, ZNet.instance, ZNet.instance != null && ZNet.instance.IsServer()) ||
                    (relayCompletion != null && (!Value(discordEnabled) || !relay.DeliveryPeerConnected))))
                    uploadCancellation.Cancel();
                if (upload != null && upload.IsCompleted)
                {
                    var result = upload.GetAwaiter().GetResult();
                    if (result.Success) Logger.LogInfo("[Discord] " + result.Message);
                    else Logger.LogWarning("[Discord] " + result.Message);
                    upload = null;
                    var origin = uploadSession;
                    uploadCancellation?.Dispose(); uploadCancellation = null; uploadSession = null;
                    var completed = relayCompletion; relayCompletion = null;
                    if (completed != null && result.Success) relay.PublishDeliveryReceipt(result.MessageLink);
                    if (completed == null) gallery?.Complete(uploadGallery, result.Success ? "Uploaded" : result.DeliveryUnknown ? "Unknown" : "Failed", result.MessageLink);
                    uploadGallery = null;
                    if (completed == null) FinishMoment(uploadDeath, origin, result.Success, result.Success ? "Sent to Discord" :
                        result.DeliveryUnknown ? "Upload unconfirmed - check Discord" : "Upload failed - memory kept locally");
                    uploadDeath = null;
                    completed?.Invoke(result.Success ? RelayOutcome.Uploaded : result.DeliveryUnknown ? RelayOutcome.Unknown : RelayOutcome.Failed);
                }
            }
            catch (Exception error) { Logger.LogWarning("[Relay] Delivery update failed: " + error.GetType().Name); }
            if (!initialized) return;
            try
            {
                double now = clock.Elapsed.TotalSeconds;
                if (Application.isFocused && Input.GetKeyDown(Value(galleryKey))) { galleryOpen = !galleryOpen; if (!galleryOpen) ClearGalleryPreview(); }
                if (previewLoad != null && previewLoad.IsCompleted)
                {
                    var task = previewLoad; previewLoad = null;
                    if (task.Status == TaskStatus.RanToCompletion && task.Result != null && galleryOpen && pendingPreviewGeneration == previewGeneration)
                    { var texture = new Texture2D(256, 144, TextureFormat.RGBA32, false); texture.LoadRawTextureData(task.Result); texture.Apply(); galleryPreviews[previewId] = texture; }
                    else { var ignored = task.Exception; }
                }
                if (Application.isFocused && Input.GetKeyDown(Value(keepKey))) KeepLatestMoment();
                if (now >= nextGalleryTick) { nextGalleryTick = now + 1; gallery?.Tick(Value(recoveryCount), Value(recoveryMiB) * 1048576L, Value(recoveryHours), Value(galleryCount)); }
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
                discordIdentity.Tick(now, (Value(discordUserId) ?? "").Trim(), Player.m_localPlayer?.GetPlayerName());
                UpdateDiscoveryContext(now); discoveryHistory.Tick(); CompleteSavedDiscoveries(now);
                if (discoveredNames.Count > 0 && now >= discoveryDeadline)
                {
                    string names = string.Join(", ", discoveredNames); discoveredNames.Clear();
                    if (Value(discoveryEnabled) && now >= nextDiscovery && Trigger("discovery", EventMessages.Discovery(Value(discoveryMessage), names, Player.m_localPlayer?.GetPlayerName()), discoveryPostSeconds))
                        nextDiscovery = now + Value(discoveryCooldown);
                }
                if (sourceWidth != Screen.width || sourceHeight != Screen.height)
                { sourceWidth = Screen.width; sourceHeight = Screen.height; captureSettingsDirty = true; captureSettingsChangedAt = now; }
                if (captureSettingsDirty && hostSettings.Ready && now - captureSettingsChangedAt >= 0.5) ApplyCaptureSettings();
                bool active = Value(captureEnabled) && !paused && captureSession.HasSession && hostSettings.Ready && !captureSettingsDirty;
                UpdateCloseCall(now, active);
                UpdateRaid(now, active);
                UpdateCinematics(now, active);
                if (active && Value(lootTrigger) && Value(lootEnabled))
                {
                    lootHighlights.Poll(now, Value(minimumLootRarity), OnLootHighlight);
                    acquisitions.Poll(now, Value(minimumLootRarity), kind => kind == "c" ? Value(chestPickups) : Value(worldPickups), OnLootHighlight);
                }
                else { lootHighlights.Clear(); acquisitions.Clear(); }
                if (!active)
                {
                    if (!historyCleared && pending.Count == 0) { history.ClearHistory(); CancelPendingDeath(); historyCleared = true; }
                }
                else
                {
                    historyCleared = false;
                    if (Value(manualTrigger) && Application.isFocused && Input.GetKeyDown(Value(captureKey)))
                    {
                        Trigger("manual", "Valheim moment");
                    }
                }
                if (encoding != null && encoding.IsCompleted && !CinematicPending(activeCameraKey, now) && (!encoding.IsCompletedSuccessfully || activeBoss?.Loot == null || !activeBoss.Loot.Pending || ((activeBoss.BossNumber > 0 || activeBoss.Special) && !Value(activeBoss.Special ? specialShowLoot : showBossLoot)) || now >= lootDeadline))
                {
                    try
                    {
                        Logger.LogInfo("[WebP] " + encoding.GetAwaiter().GetResult() + "; saved " + activeOutput);
                        AttachCinematics(activeOutput, activeCameraKey);
                        StartUpload(activeOutput);
                    }
                    catch (Exception error) { gallery?.Complete(activeGallery, "Failed"); Logger.LogWarning("[WebP] Clip failed: " + error.Message); FinishMoment(activeDeath, activeSession, false, "Memory capture failed"); }
                    encodingClip.Release(); encodingClip = null; encoding = null;
                }
                // Oldest pending submission is a safe exclusive completion watermark.
                if (active && encoding == null && raidWorker == null && (!closeCollecting || closeSurvived))
                {
                    var clip = history.TryComplete(pending.Count == 0 ? now : pending.Peek().Submitted);
                    if (clip != null)
                    {
                        closeCollecting = closeSurvived = false;
                        if (clip.Count == 0) { clip.Release(); if (raidCollecting) CancelRaid(); CancelPendingDeath(); Logger.LogWarning("[Capture] No frames available; clip discarded."); Notice("No frames available", false, false); }
                        else if (raidCollecting) EncodeRaidSegment(clip);
                        else if (pendingBoss != null && (pendingBoss.BossNumber > 0 || pendingBoss.Special) && BossCaptureRules.UsesRarity(Value(pendingBoss.Special ? specialCaptureMode : bossCaptureMode))) waitingForLoot = clip;
                        else StartEncoding(clip);
                    }
                }
                if (waitingForLoot != null)
                {
                    string reason;
                    var decision = BossLootFilter.Evaluate(pendingBoss?.Loot, BossCaptureRules.UsesRarity(Value(pendingBoss.Special ? specialCaptureMode : bossCaptureMode)), pendingBoss.FirstKill, BossCaptureRules.FirstBypasses(Value(pendingBoss.Special ? specialCaptureMode : bossCaptureMode)), Value(pendingBoss.Special ? specialMinimumRarity : minimumBossRarity), now >= lootDeadline, out reason);
                    if (decision != LootDecision.Wait)
                    {
                        var clip = waitingForLoot; waitingForLoot = null;
                        if (decision == LootDecision.Accept) { Logger.LogInfo("[Loot] Boss clip accepted: " + reason); StartEncoding(clip); }
                        else
                        {
                            clip.Release(); pendingBoss = null;
                            Logger.LogInfo("[Loot] Boss clip skipped: " + reason);
                            Notice("Memory skipped - loot below threshold", false, false);
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
                if (!Application.isFocused) continue;
                try { if (Value(captureEnabled) && !paused) SubmitCapture(); }
                catch (Exception error) { Logger.LogError("[Capture] Submission failed: " + error.Message); StopCapture(); }
                try { if (!stopped) cinematic?.Render(clock.Elapsed.TotalSeconds); } catch { }
                try { if (!stopped && Value(notificationsEnabled)) notifications.Draw(clock.Elapsed.TotalSeconds, Value(notificationStyle)); }
                catch { /* Optional feedback cannot stop capture. */ }
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
            BlitCapture(screen, slot.Target);
            slot.Submitted = now;
            slot.SessionRevision = captureSession.Revision;
            slot.Request = AsyncGPUReadback.RequestIntoNativeArray(ref slot.Pixels, slot.Target, 0, TextureFormat.RGBA32);
            free.Dequeue(); pending.Enqueue(slot);
            double elapsed = clock.Elapsed.TotalMilliseconds - started;
            submitMs += elapsed; maxSubmitMs = Math.Max(maxSubmitMs, elapsed); submitted++;
        }

        private static void BlitCapture(RenderTexture source, RenderTexture target)
        {
            double ratio = (double)source.width / source.height;
            if (Math.Abs(ratio - (double)target.width / target.height) < 0.005)
            { Graphics.Blit(source, target); return; }
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                GL.Clear(true, true, Color.black);
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0, target.width, target.height, 0);
                    float scale = Math.Min((float)target.width / source.width, (float)target.height / source.height);
                    float w = source.width * scale, h = source.height * scale;
                    Graphics.DrawTexture(new Rect((target.width - w) / 2, (target.height - h) / 2, w, h), source);
                }
                finally { GL.PopMatrix(); }
            }
            finally { RenderTexture.active = previous; }
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
            Notice("Saving Memory", true, true);
            encodingClip = clip;
            activeMessage = pendingMessage;
            activeRecorder = pendingRecorder;
            activeKind = pendingKind; activeSession = pendingSession; activeAsHost = pendingAsHost;
            activeEventId = null;
            activeBoss = pendingBoss; pendingBoss = null;
            activeCameraKey = activeBoss?.CinematicSubject ?? (activeKind == "discovery" ? discoveryCameraKey : null);
            cameraWaitUntil = clock.Elapsed.TotalSeconds + 8;
            activeDeath = pendingDeath; pendingDeath = null;
            activeGallery = gallery.Add(activeKind, activeMessage, activeRecorder, activeSession, activeAsHost, pendingKeep || Value(saveLocalCopy));
            activeGallery.Acknowledgement = activeDeath;
            pendingKeep = false;
            activeOutput = gallery.PathFor(activeGallery);
            string destination = activeOutput;
            bool flipImage = Value(flip);
            int encodeWidth = width, encodeHeight = height, encodeQuality = quality;
            int before = 0;
            for (int i = 0; i < clip.Count; i++) if (clip.GetTimestamp(i) < clip.TriggerTime) before++;
            Logger.LogInfo(string.Format("[WebP] Encoding {0} frames in background helper; pre-event={1}, post-event={2}.", clip.Count, before, clip.Count - before));
            var record = activeGallery;
            encoding = Task.Run(() => {
                try { gallery.WritePreview(record, MomentGallery.Preview(clip.GetPixels(Math.Max(0, before - 1)), encodeWidth, encodeHeight, flipImage)); } catch { }
                return EncoderClient.Encode(clip, encoderPath, destination, encodeWidth, encodeHeight, encodeQuality, flipImage, shutdown.Token);
            });
        }

        private static void ReleaseTarget(RenderTexture target)
        {
            if (target == null) return;
            target.Release(); Destroy(target);
        }

        private void StartUpload(string file)
        {
            var record = activeGallery;
            gallery.Uploading(record);
            if (record != null) record.Caption = EventMessages.FormatPost(activeBoss == null ? activeMessage : BossMessage(activeBoss));
            var session = ZNet.instance;
            if (ReferenceEquals(activeSession, session) && hostSettings.Ready && !Value(discordEnabled))
            {
                bool retained = Value(saveLocalCopy) || record?.Pinned == true;
                gallery.Complete(record, retained ? "Saved" : "Not shared");
                FinishMoment(activeDeath, activeSession, retained, retained ? "Memory Saved" : "Memory not shared - saving disabled");
                return;
            }
            if (session != null && !session.IsServer() && !activeAsHost && ReferenceEquals(activeSession, session))
            {
                string message = EventMessages.FormatPost(activeBoss == null ? activeMessage : BossMessage(activeBoss));
                var ticket = activeDeath; var origin = activeSession;
                if (!Value(discordEnabled) || !relay.Offer(activeSession, file, activeKind, message, true, outcome => {
                    gallery.Complete(record, outcome == RelayOutcome.Uploaded ? "Uploaded" : outcome == RelayOutcome.Unknown ? "Unknown" : outcome == RelayOutcome.Omitted ? "Omitted" : "Failed", outcome == RelayOutcome.Uploaded ? relay.LastMessageLink : null);
                    FinishMoment(ticket, origin, outcome == RelayOutcome.Uploaded, outcome == RelayOutcome.Uploaded ? "Sent to Discord" :
                        outcome == RelayOutcome.Unknown ? "Upload unconfirmed - check Discord" :
                        outcome == RelayOutcome.Omitted ? "Perspective omitted - memory kept locally" : "Upload incomplete - memory kept locally"); }, activeBoss?.EventId ?? activeEventId, activeBoss?.FirstKill == true))
                {
                    Logger.LogInfo("[Relay] Clip retained locally: relay disabled, unavailable, busy or clip exceeds 10 MiB.");
                    gallery.Complete(record, "Failed");
                    FinishMoment(ticket, origin, false, "Upload unavailable - memory kept locally");
                }

                return;
            }
            if (!DiscordRouting.CanSubmit(activeSession, activeAsHost, session, session != null && session.IsServer()))
            {
                Logger.LogInfo("[Discord] Local clip retained: changed or ended sessions cannot submit old clips.");
                gallery.Complete(record, "Failed");
                FinishMoment(activeDeath, activeSession, false, "Memory kept locally");
                return;
            }
            if (!Value(discordEnabled)) { gallery.Complete(record, "Failed"); return; }
            if (Value(directorEnabled) && highlightQueue != null)
            {
                QueueHostClip(file);
                return;
            }
            if (highlightQueue?.Busy == true) { gallery.Complete(record, "Failed"); FinishMoment(activeDeath, activeSession, false, "Upload busy - memory kept locally"); return; }
            if (upload != null)
            {
                Logger.LogWarning("[Discord] Upload busy; new clip retained locally.");
                gallery.Complete(record, "Failed");
                FinishMoment(activeDeath, activeSession, false, "Upload busy - memory kept locally");
                return;
            }
            // Snapshot config on Unity's thread; perform all HTTP/file work on a worker.
            var options = new DiscordOptions {
                WebhookUrl = Destination(activeKind),
                Username = Value(discordUsername),
                Message = EventMessages.RecordedPost(activeBoss == null ? activeMessage : BossMessage(activeBoss), activeRecorder),
                SaveLocalCopy = true,
                MaxUploadBytes = Math.Max(1, Math.Min(100, Value(uploadLimitMiB))) * 1048576L
            };
            discordIdentity.Apply(options);
            Logger.LogInfo("[Discord] Starting background upload.");
            uploadSession = session;
            uploadGallery = record;
            uploadDeath = activeDeath;

            uploadCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            var token = uploadCancellation.Token;
            string cameraDirectory = Path.Combine(Path.GetDirectoryName(outputDirectory), "RaidTemp", Guid.NewGuid().ToString("N"));
            byte allowed = AllowedCinematics(activeKind); string helper = encoderPath; int q = quality;
            var clips = new[] { new DiscordClip(file, activeRecorder, true) };
            upload = Task.Run(() => UploadCinematicBatch(clips, options, options.MaxUploadBytes, helper, q, cameraDirectory, false, allowed, token));
        }

        private void UpdateDirector()
        {
            if (relay == null) return;
            double now = clock.Elapsed.TotalSeconds;
            if (highlightQueue != null && ReferenceEquals(directorSession, ZNet.instance) && now < nextDirectorCheck) return;
            nextDirectorCheck = now + 0.5;
            string signature = Value(directorEnabled) + ":" + Value(directorPerspectives) + ":" + Value(directorPostMiB) + ":" + Value(directorWindow) + ":" + Value(uploadLimitMiB);
            if (highlightQueue == null || (signature != directorSignature && !highlightQueue.Busy))
            {
                directorSignature = signature;
                highlightQueue = new HostHighlightQueue(new HighlightDirector(Value(directorPerspectives),
                    Math.Min(10, Value(uploadLimitMiB)) * 1048576L, Value(directorPostMiB) * 1048576L, Value(directorWindow)), 60L * 1048576, SendHighlightGroup);
                highlightQueue.Reset(ZNet.instance);
                highlightQueue.CanUpload = () => upload == null;
            }
            if (!ReferenceEquals(directorSession, ZNet.instance)) { directorSession = ZNet.instance; highlightQueue.Reset(directorSession); }
            relay.Collect = Value(directorEnabled) ? CollectPerspective : null;
        }

        private bool CollectPerspective(RelayFile file, string recorder, long peer, Func<bool> connected,
            Func<Action<string>, bool> transfer, Action<RelayOutcome> complete)
        {
            var origin = ZNet.instance;
            var item = new QueuedPerspective {
                Offer = new HighlightOffer(file.Id, file.EventId, file.Kind, peer, EpicLootAdapter.Plain(recorder, 80), file.Size, file.PersonalFirst),
                Message = file.Message, Keep = true, Transfer = transfer, Complete = complete, Release = file.Dispose,
                Receipt = result => { if (result.Success) relay.PublishReceipt(file.Id, result.MessageLink); },
                Eligible = () => Value(directorEnabled) && ReferenceEquals(origin, ZNet.instance) && connected() && CanRelay(file.Kind)
            };
            return highlightQueue.Offer(origin, item, clock.Elapsed.TotalSeconds) == HighlightAdmission.Accepted;
        }

        private void QueueHostClip(string file)
        {
            var record = activeGallery;
            if (localInspection != null) { gallery.Complete(record, "Failed"); FinishMoment(activeDeath, activeSession, false, "Queue busy - memory kept locally"); return; }
            var origin = activeSession; var ticket = activeDeath; string kind = activeKind;
            string eventId = activeBoss?.EventId ?? activeEventId, recorder = EpicLootAdapter.Plain(activeRecorder, 80);
            bool first = activeBoss?.FirstKill == true, keep = true;
            string message = EventMessages.FormatPost(activeBoss == null ? activeMessage : BossMessage(activeBoss));
            localInspected = bytes => {
                var item = new QueuedPerspective {
                    Offer = new HighlightOffer(Guid.NewGuid().ToString("N"), eventId, kind, long.MinValue, recorder, bytes, first),
                    File = file, Keep = keep, Message = message,
                    Eligible = () => Value(directorEnabled) && DiscordRouting.CanSubmit(origin, true, ZNet.instance, ZNet.instance != null && ZNet.instance.IsServer()) && CanRelay(kind),
                    Receipt = result => { if (result.Success) gallery.Complete(record, "Uploaded", result.MessageLink); },
                    Complete = outcome => { gallery.Complete(record, outcome == RelayOutcome.Uploaded ? "Uploaded" : outcome == RelayOutcome.Unknown ? "Unknown" : "Omitted"); FinishMoment(ticket, origin, outcome == RelayOutcome.Uploaded,
                        outcome == RelayOutcome.Uploaded ? "Sent to Discord" : outcome == RelayOutcome.Unknown ? "Upload unconfirmed - check Discord" : "Perspective omitted - memory kept locally"); }
                };
                if (bytes == 0 || highlightQueue.Offer(origin, item, clock.Elapsed.TotalSeconds) != HighlightAdmission.Accepted) item.Complete(RelayOutcome.Omitted);
            };
            localInspection = RelayFile.Inspect(file);

        }

        private Task<UploadResult> SendHighlightGroup(QueuedPerspective[] items, CancellationToken token)
        {
            string message = items[0].Message;
            if (items.Length == 1) message = EventMessages.RecordedPost(message, items[0].Offer.Recorder);
            else
            {
                message = message.Replace("**First boss kill for this character!**\n", "").Replace("\n**First boss kill for this character!**", "");
                if (message.Length > 1450) message = message.Substring(0, char.IsHighSurrogate(message[1449]) ? 1449 : 1450) + "…";

                for (int i = 0; i < items.Length; i++) message += "\n* " + (i + 1) + ". **Recorded by:** " + items[i].Offer.Recorder + (items[i].Offer.PersonalFirst ? " (first kill for this character)" : "");
            }
            var options = new DiscordOptions { WebhookUrl = Destination(items[0].Offer.Kind), Username = Value(discordUsername), Message = message,
                MaxUploadBytes = Math.Min(10, Value(uploadLimitMiB)) * 1048576L };
            discordIdentity.Apply(options);
            long budget = Value(directorPostMiB) * 1048576L;
            var clips = new DiscordClip[items.Length];
            for (int i = 0; i < clips.Length; i++) clips[i] = new DiscordClip(items[i].File, items[i].Offer.Recorder, items[i].Keep);
            string helper = encoderPath;
            int labelQuality = quality;
            string labelDirectory = Path.Combine(Path.GetDirectoryName(outputDirectory), "RaidTemp", Guid.NewGuid().ToString("N"));
            byte allowed = AllowedCinematics(items[0].Offer.Kind);
            return Task.Run(async () => {
                var result = await UploadCinematicBatch(clips, options, budget, helper, labelQuality, labelDirectory, clips.Length > 1, allowed, token).ConfigureAwait(false);
                if (!result.Success) Logger.LogWarning("[Director] " + result.Message);
                return result;
            });
        }

        private void OnDestroy() { StopCapture(); }
        private string Destination(string kind)
        {
            if (kind == "special" && Value(useSpecialWebhook)) return Value(specialWebhook);
            return DiscordRouting.Destination(kind, Value(webhookUrl), Value(useBossWebhook), Value(bossWebhook),
                Value(useLootWebhook), Value(lootWebhook), Value(useDeathWebhook), Value(deathWebhook), Value(discoveryWebhook));
        }
        private bool CanRelay(string kind)
        {
            if (!Value(discordEnabled) || (!Value(directorEnabled) && (upload != null || highlightQueue?.Busy == true))) return false;
            if (kind == "manual" && !Value(manualTrigger)) return false;
            if (kind == "boss" && (!Value(bossTrigger) || !Value(bossEnabled))) return false;
            if (kind == "loot" && (!Value(lootTrigger) || !Value(lootEnabled))) return false;
            if (kind == "death" && (!Value(deathTrigger) || !Value(deathEnabled))) return false;
            if (kind == "discovery" && !Value(discoveryEnabled)) return false;
            if (kind == "special" && !Value(specialEnabled)) return false;
            if (kind == "closecall" && !Value(closeEnabled)) return false;
            if (kind == "raid" && !Value(raidsEnabled)) return false;
            Uri endpoint;
            return RelayProtocol.ValidKind(kind) && DiscordWebhook.TryEndpoint(Destination(kind), out endpoint);
        }
        private bool AcceptRelayedEvent(ZRpc peer, string kind, double now)
        {
            if (kind != "death") return true;
            if (!ReferenceEquals(deathOfferSession, ZNet.instance)) { deathOffers.Clear(); deathOfferSession = ZNet.instance; }
            var live = new HashSet<ZRpc>();
            foreach (var connection in ZNet.instance.GetPeers()) live.Add(connection.m_rpc);
            var stale = new List<ZRpc>();
            foreach (var item in deathOffers) if (!live.Contains(item.Key)) stale.Add(item.Key);
            foreach (var item in stale) deathOffers.Remove(item);
            MomentRateLimit quota;
            if (!deathOffers.TryGetValue(peer, out quota))
            {
                if (deathOffers.Count >= 128) return false;
                deathOffers.Add(peer, quota = new MomentRateLimit());
            }
            return quota.TryTake(now, Value(deathCaptureLimit), Value(deathWindowSeconds));
        }
        private void ReceiveRelayedClip(RelayFile clip, string recorder, Action<RelayOutcome> completion)
        {
            var session = ZNet.instance;
            if (session == null || !session.IsServer() || !CanRelay(clip.Kind)) { completion(RelayOutcome.Failed); return; }
            var options = new DiscordOptions { WebhookUrl = Destination(clip.Kind), Username = Value(discordUsername),
                Message = EventMessages.RecordedPost(clip.Message, recorder), SaveLocalCopy = true,
                MaxUploadBytes = Math.Max(1, Math.Min(10, Value(uploadLimitMiB))) * 1048576L };
            discordIdentity.Apply(options);
            uploadSession = session; relayCompletion = completion;
            uploadCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            var token = uploadCancellation.Token;
            string file = clip.FilePath;
            string cameraDirectory = Path.Combine(Path.GetDirectoryName(outputDirectory), "RaidTemp", Guid.NewGuid().ToString("N"));
            byte allowed = AllowedCinematics(clip.Kind); string helper = encoderPath; int q = quality;
            upload = Task.Run(async () => {
                try
                {
                    token.ThrowIfCancellationRequested();
                    var result = await UploadCinematicBatch(new[] { new DiscordClip(file, recorder, true) }, options, options.MaxUploadBytes,
                        helper, q, cameraDirectory, false, allowed, token).ConfigureAwait(false);
                    if (!result.Success) Logger.LogWarning("[Discord] " + result.Message);
                    result.Message = result.Success ? "Client clip uploaded; success confirmation sent." :
                        result.DeliveryUnknown ? "Client clip delivery unknown; check Discord before retrying. Client retains original." : "Client clip delivery failed; client retains original.";
                    return result;
                }
                catch { return new UploadResult { Success = false, Message = "Client clip delivery cancelled or failed; client retains original." }; }
                finally { try { if (File.Exists(file)) File.Delete(file); } catch { } }
            });
        }
        private void OnOrdinaryKill(BossKill kill)
        {
            if (Value(logEnemyKeys)) Logger.LogInfo("[Special] Confirmed enemy key: " + EpicLootAdapter.Plain(kill.EnemyKey, 128));
            if (!Value(captureEnabled) || paused) return;
            specialEnemies.Configure(Value(specialKeys));
            double now = clock.Elapsed.TotalSeconds;
            if (Value(specialEnabled) && specialEnemies.Eligible(kill, BossCaptureRules.FirstOnly(Value(specialCaptureMode)), now) && Trigger("special", "", specialPostSeconds))
            {
                kill.Special = true; pendingBoss = kill; lootDeadline = now + Value(specialLootWait);
                StartBossAftermath(kill);
                specialEnemies.Captured(kill.EnemyKey, now, Value(specialCooldown)); return;
            }
            if (Value(lootTrigger) && Value(lootEnabled)) lootHighlights.Add(kill, now, Value(highlightWaitSeconds));
        }
        private void UpdateDiscoveryContext(double now)
        {
            long player = 0, world = 0;
            try {
                if (ZNet.instance != null && Player.m_localPlayer != null)
                { player = Player.m_localPlayer.GetPlayerID(); world = ZNet.instance.GetWorldUID(); }
            } catch { player = 0; world = 0; }
            if (discoveryHistory.Use(player, world)) { discoveryStarted = now; discoveredNames.Clear(); savingDiscoveries.Clear(); nextDiscovery = 0; }
        }
        private void OnDiscovery(string kind, string identity, string name, string legacyIdentity)
        {
            if (!initialized || stopped) return;
            double now = clock.Elapsed.TotalSeconds; UpdateDiscoveryContext(now);
            string historyKind = kind == "subbiome" ? "biome" : kind;
            if (!discoveryHistory.Visit(historyKind + ":" + identity, legacyIdentity == null ? null : historyKind + ":" + legacyIdentity)) return;
            Task saved = discoveryHistory.Flush();
            if (now - discoveryStarted < Math.Max(5, Value(preSetting)) || !hostSettings.Ready || !Value(discoveryEnabled) ||
                !Value(captureEnabled) || paused || now < nextDiscovery ||
                ((kind == "biome" || kind == "subbiome") && !Value(discoverBiomes)) ||
                (kind == "subbiome" && !Value(discoverSubBiomes)) || (kind == "location" && !Value(discoverLocations)) || (kind == "trader" && !Value(discoverTraders))) return;
            string display = EpicLootAdapter.Plain(Localization.instance.Localize(name), 160);
            if (string.IsNullOrWhiteSpace(display)) return;
            if (savingDiscoveries.Count < 8) savingDiscoveries.Add(new SavedDiscovery { Kind = kind, Display = display, Save = saved });
        }
        private void CompleteSavedDiscoveries(double now)
        {
            for (int i = savingDiscoveries.Count - 1; i >= 0; i--)
            {
                var pendingDiscovery = savingDiscoveries[i];
                if (!pendingDiscovery.Save.IsCompleted) continue;
                savingDiscoveries.RemoveAt(i);
                if (pendingDiscovery.Save.IsFaulted) { var ignored = pendingDiscovery.Save.Exception; continue; }
                if (pendingDiscovery.Save.IsCanceled || !hostSettings.Ready || !Value(discoveryEnabled) || !Value(captureEnabled) || paused || now < nextDiscovery) continue;
                string kind = pendingDiscovery.Kind, display = pendingDiscovery.Display;
                if (((kind == "biome" || kind == "subbiome") && !Value(discoverBiomes)) || (kind == "subbiome" && !Value(discoverSubBiomes)) ||
                    (kind == "location" && !Value(discoverLocations)) || (kind == "trader" && !Value(discoverTraders))) continue;
                if (discoveredNames.Count == 0) discoveryDeadline = now + 1.25;
                if ((kind == "biome" || kind == "subbiome") && Value(cinematicBiome) && Player.m_localPlayer != null)
                {
                    string key = Guid.NewGuid().ToString("N");
                    if (StartCinematic(key, 4, Player.m_localPlayer.GetCenterPoint(), Player.m_localPlayer.transform, 3, now, display + "\nFirst discovery")) discoveryCameraKey = key;
                }
                if (discoveredNames.Count < 8 && !discoveredNames.Contains(display)) discoveredNames.Add(display);
            }
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
            Logger.LogInfo("[Boss] Capture rules: FirstKillOnly=" + BossCaptureRules.FirstOnly(Value(bossCaptureMode)) + ", rarity filter=" + BossCaptureRules.UsesRarity(Value(bossCaptureMode)) + ", minimum=" + Value(minimumBossRarity) + ", first-kill bypass=" + BossCaptureRules.FirstBypasses(Value(bossCaptureMode)));
            if (BossCaptureRules.FirstOnly(Value(bossCaptureMode)) && !kill.FirstKill)
            {
                Logger.LogInfo("[Boss] Repeat kill skipped by FirstKillOnly.");
                return;
            }
            if (Trigger("boss", "", bossPostSeconds))
            {
                pendingBoss = kill;
                StartBossAftermath(kill);
                double wait = Value(lootWaitSeconds);
                lootDeadline = clock.Elapsed.TotalSeconds + (double.IsNaN(wait) ? 12 : Math.Max(0, Math.Min(25, wait)));
            }
        }
        private string BossMessage(BossKill kill)
        {
            try
            {
                bool highlight = kill.BossNumber <= 0 && !kill.Special;
                string loot = null;
                if (highlight)
                    loot = EventMessages.Heading(Value(highlightHeader), 2) + "\n" + (kill.Loot == null ? "unavailable" : kill.Loot.Display(Value(highlightMaxItems), Value(highlightQuantity), text => Localization.instance.Localize(text), Value(highlightRarity), Value(highlightModifiers), Value(highlightSockets), Value(highlightUnidentified)));
                else if (kill.Special && Value(specialShowLoot))
                    loot = EventMessages.Heading(Value(specialLootHeader), 2) + "\n" + (kill.Loot == null ? "unavailable" : kill.Loot.Display(Value(specialMaxItems), Value(specialQuantity), text => Localization.instance.Localize(text), Value(specialRarity), Value(specialModifiers), Value(specialSockets), Value(specialUnidentified)));
                else if (!kill.Special && Value(showBossLoot))
                    loot = EventMessages.Heading(Value(lootHeader), 2) + "\n" + (kill.Loot == null ? "unavailable" : kill.Loot.Display(Value(maxLootItems), Value(showLootQuantity), text => Localization.instance.Localize(text), Value(showRarity), Value(showModifiers), Value(showSockets), Value(showUnidentified)));
                string count = kill.Loot != null && kill.Loot.Observed ? kill.Loot.Items.Count.ToString() : "unknown";
                if (highlight && kill.Acquired) return EventMessages.FoundLoot(Value(pickupMessage), kill.EnemyKey, kill.PlayerName, loot, count);
                if (kill.Special) return EventMessages.Boss(Value(specialMessage).Replace("{enemy}", "{boss}"), Localization.instance.Localize(kill.EnemyKey), BossAttribution.CreditLabel(kill.CreditNames, kill.PlayerName), Value(specialNameMode), kill.FinalBlowName, loot, count, kill.FirstKill);
                if (highlight) return EventMessages.Loot(Value(highlightMessage), Localization.instance.Localize(kill.EnemyKey), kill.PlayerName, loot, count);
                return EventMessages.Boss(Value(bossMessage), Localization.instance.Localize(kill.EnemyKey), BossAttribution.CreditLabel(kill.CreditNames, kill.PlayerName), Value(bossNameMode), kill.FinalBlowName, loot, count, kill.FirstKill);
            }
            catch
            {
                Logger.LogWarning("[Loot] Message enrichment failed; sending boss names only.");
                if (kill.Special) return EventMessages.Boss(Value(specialMessage).Replace("{enemy}", "{boss}"), kill.EnemyKey, BossAttribution.CreditLabel(kill.CreditNames, kill.PlayerName), Value(specialNameMode), kill.FinalBlowName, firstKill: kill.FirstKill);
                if (kill.Acquired) return EventMessages.FoundLoot(Value(pickupMessage), kill.EnemyKey, kill.PlayerName, "unavailable");
                return kill.BossNumber <= 0 ? EventMessages.Loot(Value(highlightMessage), kill.EnemyKey, kill.PlayerName, "unavailable") : EventMessages.Boss(Value(bossMessage), kill.EnemyKey, BossAttribution.CreditLabel(kill.CreditNames, kill.PlayerName), Value(bossNameMode), kill.FinalBlowName, firstKill: kill.FirstKill);
            }
        }
        private void CancelRaid()
        {
            raidMoment.Abandon();
            if (raidCollecting) history?.CancelPending();
            raidCollecting = false;
            raidMedia?.Dispose(); raidMedia = null;
        }
        private void OnRaidTransition(RandomEvent occurrence, RaidTransition transition)
        {
            if (!initialized || stopped) return;
            SynchronizeCaptureSession();
            bool eligible = Value(raidsEnabled) && Value(captureEnabled) && !paused && hostSettings.Ready &&
                !captureSettingsDirty && captureSession.HasSession && Player.m_localPlayer != null &&
                !Player.m_localPlayer.IsDead() && (Value(discordEnabled) || Value(saveLocalCopy));
            double now = clock.Elapsed.TotalSeconds;
            if (transition == RaidTransition.Left) { CancelRaid(); return; }
            if (transition == RaidTransition.Entered)
            {
                var result = raidMoment.Enter(ZNet.instance, occurrence, now, eligible,
                    eligible && !history.IsBusy && raidWorker == null && raidMedia == null);
                if (result == RaidMomentResult.Abandoned) { CancelRaid(); return; }
                if (result != RaidMomentResult.Started) return;
                raidMedia = new RaidMedia(Path.Combine(Path.GetDirectoryName(outputDirectory), "RaidTemp"));
                raidPlayer = Player.m_localPlayer; raidSession = ZNet.instance; raidRecorder = raidPlayer.GetPlayerName();
                raidOccurrence = occurrence; raidEventId = RaidIdentity.For(occurrence); raidKeep = false;
                raidCameraKey = Guid.NewGuid().ToString("N");
                if (Value(cinematicRaid)) StartCinematic(raidCameraKey, 3, occurrence.m_pos + Vector3.up * 2, null, 8, now,
                    RaidCaption(occurrence));
                raidEnding = raidComposing = false;
                raidCollecting = history.TryTriggerSegment(now, Value(raidOpening));
                if (!raidCollecting) CancelRaid();
            }
            else
            {
                var result = raidMoment.End(ZNet.instance, occurrence, now, eligible);
                if (result == RaidMomentResult.Abandoned) { CancelRaid(); return; }
                if (result != RaidMomentResult.Ended) return;
                if (raidMedia == null || raidWorker != null || history.IsBusy || !eligible || !File.Exists(raidMedia.Opening))
                { CancelRaid(); return; }
                raidEnding = true; raidCollecting = history.TryTriggerSegment(now, Value(raidEndingSeconds));
                if (!raidCollecting) CancelRaid();
            }
        }
        private void EncodeRaidSegment(CaptureBuffer.Clip clip)
        {
            if (raidEnding) Notice("Saving Memory", true, true);
            raidCollecting = false; raidClip = clip;
            var media = raidMedia;
            if (media == null) { clip.Release(); raidClip = null; return; }
            string path = raidEnding ? media.Ending : media.Opening;
            string helper = encoderPath; int w = width, h = height, q = quality; bool flipImage = Value(flip);
            raidWorker = media.Start(token => {
                raidPreview = MomentGallery.Preview(clip.GetPixels(0), w, h, flipImage);
                return EncoderClient.Encode(clip, helper, path, w, h, q, flipImage, token);
            });
        }
        private void UpdateRaid(double now, bool active)
        {
            if (raidMedia != null && !raidEnding)
            {
                string id = RaidIdentity.For(raidOccurrence);
                if (raidEventId != null && id != null && id != raidEventId) CancelRaid();
                else if (id != null) raidEventId = id;
            }
            if (raidMedia != null)
            {
                bool valid = active && Value(raidsEnabled) && ReferenceEquals(raidSession, ZNet.instance) &&
                    raidPlayer != null && raidPlayer == Player.m_localPlayer && !raidPlayer.IsDead();
                if (!valid || (!raidEnding && raidMoment.Observe(ZNet.instance, now, true, true, true) == RaidMomentResult.Abandoned)) CancelRaid();
            }
            if (raidWorker == null || !raidWorker.IsCompleted) return;
            bool success = false;
            try { raidWorker.GetAwaiter().GetResult(); success = true; }
            catch (Exception error) { if (raidMedia != null) Logger.LogWarning("[Raid] Segment failed: " + error.GetType().Name); }
            raidWorker = null; raidClip?.Release(); raidClip = null;
            if (!success || raidMedia == null) { CancelRaid(); return; }
            var media = raidMedia;
            if (!raidEnding) return; // Opening is encoded; normal capture may resume throughout the raid.
            if (!raidComposing)
            {
                raidComposing = true;
                string helper = encoderPath; int q = quality;
                raidWorker = media.Start(token => EncoderClient.Compose(helper, media.Opening, media.Ending, media.Combined, q, token));
                return;
            }
            activeGallery = gallery.Add("raid", "# Raid ended\nOpening and aftermath from this player's perspective.", raidRecorder, raidSession, raidSession.IsServer(), raidKeep || Value(saveLocalCopy));
            string destination = gallery.PathFor(activeGallery);
            try
            {
                if (raidPreview != null) { try { gallery.WritePreview(activeGallery, raidPreview); } catch { } }
                File.Move(media.Combined, destination);
                AttachCinematics(destination, raidCameraKey);
                activeKind = "raid"; activeMessage = "# Raid ended\nOpening and aftermath from this player's perspective.";
                activeRecorder = raidRecorder; activeSession = raidSession; activeAsHost = raidSession.IsServer();
                activeEventId = raidEventId;
                activeBoss = null; activeDeath = null; activeOutput = destination;
                StartUpload(destination);
            }
            catch { gallery.Complete(activeGallery, "Failed"); throw; }
            finally { CancelRaid(); }
        }
        private void CancelCloseCall()
        {
            closeCall?.Cancel();
            if (closeCollecting) history?.CancelPending();
            closeCollecting = closeSurvived = false;
        }
        private void UpdateCloseCall(double now, bool active)
        {
            double threshold = Value(closeThreshold), recovery = Value(closeRecovery), hold = Value(closeHold), cooldown = Value(closeCooldown);
            if (closeCall == null || threshold != appliedCloseThreshold || recovery != appliedCloseRecovery ||
                hold != appliedCloseHold || cooldown != appliedCloseCooldown || closePlayer != Player.m_localPlayer ||
                closeTimeline.FollowUpSeconds != Value(closeFollow) || closeTimeline.SlowSourceSeconds != Value(closeSlowSource) ||
                closeTimeline.SlowMilliseconds != (int)Math.Round(Value(closeSlowPlayback) * 1000) ||
                closeTimeline.DurationMilliseconds != (int)Math.Round(Value(closePlayback) * 1000))
            {
                CancelCloseCall(); closePlayer = Player.m_localPlayer;
                appliedCloseThreshold = threshold; appliedCloseRecovery = recovery; appliedCloseHold = hold; appliedCloseCooldown = cooldown;
                closeCall = new CloseCall(threshold / 100, recovery / 100, hold, cooldown, Value(closeFollow));
                closeTimeline = new CloseCallTimeline(Value(closeSlowSource), Value(closeFollow),
                    (int)Math.Round(Value(closeSlowPlayback) * 1000), (int)Math.Round(Value(closePlayback) * 1000));
            }
            if (!active || !Value(closeEnabled) || closePlayer == null)
            { CancelCloseCall(); return; }
            var result = closeCall.Observe(now, closePlayer.GetHealth(), closePlayer.GetMaxHealth(), !closePlayer.IsDead());
            if (result == CloseCallResult.Cancelled) CancelCloseCall();
            if (result == CloseCallResult.Survived) closeSurvived = true;
        }
        private void OnLocalDamage(Player player, float before, float after, float maximumBefore, float maximumAfter, bool alive)
        {
            if (!initialized || stopped) return;
            SynchronizeCaptureSession();
            bool active = Value(captureEnabled) && !paused && captureSession.HasSession && hostSettings.Ready && !captureSettingsDirty;
            double now = clock.Elapsed.TotalSeconds;
            UpdateCloseCall(now, active);
            if (!active || !Value(closeEnabled)) return;
            var result = closeCall.Damage(now, before, after, maximumBefore, maximumAfter, alive);
            if (result == CloseCallResult.Cancelled) { CancelCloseCall(); return; }
            if (result != CloseCallResult.Started) return;
            if (Trigger("closecall", "# Barely survived!\nSurvived " + closeTimeline.FollowUpSeconds + " seconds after a critical hit.", close: true))
            { closeCollecting = true; closeSurvived = false; }
            else closeCall.Cancel();
        }
        private void OnLocalDeath(Player player, string cause)
        {
            CancelRaid();
            CancelCloseCall();
            if (!Value(deathTrigger) || !Value(deathEnabled)) return;
            if (!initialized || stopped) return;
            SynchronizeCaptureSession();
            if (!captureSession.HasSession) return;
            deathMoments.Observe();
            var ticket = deathMoments.Reserve();
            if (ticket == null) return;
            if (history.IsBusy || paused || !Value(captureEnabled) || !hostSettings.Ready || captureSettingsDirty ||
                (!Value(discordEnabled) && !Value(saveLocalCopy)) || !deathMoments.TakeSlot(clock.Elapsed.TotalSeconds, Value(deathCaptureLimit), Value(deathWindowSeconds)))
            { deathMoments.Complete(ticket, false); return; }
            string message = EventMessages.Death(Value(deathMessage), Value(includePlayerName), null,
                Value(includePlayerName) ? player.GetPlayerName() : "", Value(includeCause), cause,
                Value(cheekyDeaths) ? deathFlavor.Next(flavorRandom) : null, ticket.Additional);
            if (Trigger("death", message)) pendingDeath = ticket;
            else deathMoments.Complete(ticket, false);
        }

        private bool Trigger(string kind, string message, double? postOverride = null, bool close = false)
        {
            if (raidWorker != null) return false;
            if (!initialized || stopped || !Value(captureEnabled) || paused) return false;
            if (!hostSettings.Ready || captureSettingsDirty || (!Value(discordEnabled) && !Value(saveLocalCopy))) return false;
            SynchronizeCaptureSession();
            if (!captureSession.HasSession || Player.m_localPlayer == null) return false;
            if (!(close ? history.TryTriggerCloseCall(clock.Elapsed.TotalSeconds, closeTimeline, fps) : history.TryTrigger(clock.Elapsed.TotalSeconds, postOverride)))
            {
                Logger.LogInfo("[Capture] " + kind + " trigger ignored: a clip is collecting or encoding.");
                if (kind == "manual") Notice("Memory capture busy", false, false);
                return false;
            }
            pendingBoss = null;
            pendingDeath = null;
            pendingSession = ZNet.instance;
            pendingAsHost = pendingSession != null && pendingSession.IsServer();
            pendingKind = kind; pendingMessage = message;
            pendingKeep = false;
            pendingRecorder = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : null;
            Logger.LogInfo("[Capture] " + kind + " event triggered; buffered frames: " + history.BufferedFrames);

            return true;
        }
        private void OnDisable() { if (initialized || relay != null) StopCapture(); }
        private void SynchronizeCaptureSession()
        {
            if (!captureSession.Observe(ZNet.instance)) return;
            CancelRaid(); raidMoment.Reset(ZNet.instance);
            CancelCloseCall(); closeCall?.Reset(); closePlayer = null;
            deathMoments.Clear(); pendingDeath = null; notifications.Clear();
            discoveredNames.Clear(); specialEnemies.Clear(); DiscoveryDetector.Clear();
            discoveryHistory?.Use(0, 0);
            waitingForLoot?.Release(); waitingForLoot = null;
            pendingBoss = null;
            lootHighlights.Clear(); acquisitions.Clear();
            historyCleared = true;
            consecutiveErrors = 0;
            Logger.LogInfo("[Capture] Session changed; buffered footage and pending capture cleared.");
            ResetCinematics();
        }
        private void CancelPendingDeath() { deathMoments.Complete(pendingDeath, false); pendingDeath = null; }
        private void FinishMoment(DeathMoments.Ticket ticket, ZNet origin, bool success, string text)
        {
            deathMoments.Complete(ticket, success);
            if (ReferenceEquals(origin, ZNet.instance)) Notice(text, false, success);
        }
        private void Notice(string text, bool starting, bool sound)
        {
            if (!initialized || stopped || !Value(notificationsEnabled)) return;
            var mode = Value(notificationSound);
            bool play = sound && (mode == MomentSoundMode.Both || (starting ? mode == MomentSoundMode.OnCapture : mode == MomentSoundMode.OnCompletion));
            notifications.Show(text, clock.Elapsed.TotalSeconds, play, (float)Value(notificationVolume));
        }
        private void OnGUI()
        {
            if (!initialized || stopped) return;
            try
            {
                if (galleryOpen) DrawGallery();
            }
            catch { }
        }
        private void KeepLatestMoment()
        {
            if (raidMedia != null && (raidCollecting || raidWorker != null || history?.IsBusy != true))
            { raidKeep = true; Notice("This raid memory will be kept if completed", false, true); return; }
            if (history != null && history.IsBusy && encoding == null && raidWorker == null)
            { pendingKeep = true; Notice("This memory will be kept", false, true); return; }
            var entries = gallery?.Snapshot();
            bool kept = entries != null && entries.Length > 0 && gallery.Pin(entries[0].Id);
            Notice(kept ? "Memory kept" : "No local memory available", false, kept);
        }
        private void DrawGallery()
        {
            float w = Math.Min(960, Screen.width - 30), h = Math.Min(720, Screen.height - 30);
            var area = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            if (Event.current.type == EventType.MouseDown && !area.Contains(Event.current.mousePosition))
            { galleryOpen = false; ClearGalleryPreview(); Event.current.Use(); return; }
            var entries = gallery.Snapshot();
            int pages = Math.Max(1, (entries.Length + 5) / 6);
            galleryPage = Math.Min(galleryPage, pages - 1);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.BeginHorizontal();
            var heading = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            heading.normal.textColor = new Color(1f, 0.85f, 0.52f);
            GUILayout.Label("VALHEIM MOMENTS", heading);
            if (GUILayout.Button("Close (" + Value(galleryKey) + ")", GUILayout.Width(130))) { galleryOpen = false; ClearGalleryPreview(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("Your memories  |  Keep: " + Value(keepKey) + "  |  Click outside to close");
            GUILayout.Label("Keep a memory to preserve its file. Successful uploads expire locally after 30 seconds.");
            if (gallery.Error != null) GUILayout.Label(gallery.Error);
            GUILayout.BeginHorizontal();
            GUI.enabled = galleryPage > 0;
            if (GUILayout.Button("Previous", GUILayout.Width(100))) { galleryPage--; galleryScroll = Vector2.zero; ClearGalleryPreview(); }
            GUI.enabled = true; GUILayout.Label("Page " + (galleryPage + 1) + " / " + pages);
            GUI.enabled = galleryPage + 1 < pages;
            if (GUILayout.Button("Next", GUILayout.Width(100))) { galleryPage++; galleryScroll = Vector2.zero; ClearGalleryPreview(); }
            GUI.enabled = true; GUILayout.EndHorizontal();
            galleryScroll = GUILayout.BeginScrollView(galleryScroll);
            if (entries.Length == 0) GUILayout.Label("Your next adventure starts this gallery.");
            var visibleIds = new HashSet<string>();
            for (int n = galleryPage * 6; n < Math.Min(entries.Length, galleryPage * 6 + 6); n++)
            {
                var e = entries[n]; visibleIds.Add(e.Id);
                GUILayout.BeginHorizontal(GUI.skin.box);
                Texture2D thumbnail;
                if (!galleryPreviews.TryGetValue(e.Id, out thumbnail) && previewLoad == null)
                {
                    galleryPreviews[e.Id] = null; previewId = e.Id; pendingPreviewGeneration = previewGeneration;
                    string path = gallery.PreviewPath(e.Id);
                    previewLoad = Task.Run(() => { try { return File.Exists(path) && new FileInfo(path).Length == 256 * 144 * 4 ? File.ReadAllBytes(path) : null; } catch { return null; } });
                }
                float thumbWidth = Math.Min(256, w * 0.32f);
                if (thumbnail != null) GUILayout.Label(thumbnail, GUILayout.Width(thumbWidth), GUILayout.Height(thumbWidth * 144 / 256));
                else GUILayout.Box(e.Busy ? "Developing memory..." : "No preview available", GUILayout.Width(thumbWidth), GUILayout.Height(thumbWidth * 144 / 256));
                GUILayout.BeginVertical();
                GUILayout.Label(e.Kind.ToUpperInvariant() + "  |  " + e.Recorder, heading);
                GUILayout.Label(e.Created.ToLocalTime().ToString("g"));
                GUILayout.Label(e.Status + (e.Busy ? " (working)" : "") + "  |  " + (e.Pinned ? "Kept permanently" : "Temporary") + "  |  " + (e.Bytes / 1048576.0).ToString("F2") + " MiB");
                bool available = e.Bytes > 0;
                GUILayout.BeginHorizontal();
                GUI.enabled = !e.Pinned && (available || e.Busy);
                if (GUILayout.Button(e.Pinned ? "Kept" : "Keep this moment")) gallery.Pin(e.Id);
                GUI.enabled = available && !e.Busy;
                if (GUILayout.Button("Open file location")) OpenGalleryFile(gallery.PathFor(e));
                GUI.enabled = true; GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUI.enabled = true;
                if (e.Status == "Failed" || e.Status == "Unknown" || e.Status == "Omitted")
                {
                    GUI.enabled = available && !e.Busy && e.Attempts < 3 && ReferenceEquals(e.Origin, ZNet.instance) && e.Origin != null &&
                        DateTime.UtcNow >= e.Completed.AddSeconds(30 * Math.Pow(2, e.Attempts));
                    if (GUILayout.Button("Retry (" + e.Attempts + "/3)"))
                    { if (e.Status == "Unknown") retryConfirmation = e.Id; else RetryGallery(e.Id, false); }
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
                if (!available && !e.Busy) GUILayout.Label("Local footage has expired or is unavailable.");
                if (retryConfirmation == e.Id)
                {
                    GUILayout.Label("Discord may already have received this memory. Retrying can post a duplicate.");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Retry anyway")) { RetryGallery(e.Id, true); retryConfirmation = null; }
                    if (GUILayout.Button("Cancel")) retryConfirmation = null;
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical(); GUILayout.EndHorizontal();
                if (e.Busy && thumbnail == null && previewLoad == null) galleryPreviews.Remove(e.Id);
            }
            GUILayout.EndScrollView(); GUILayout.EndArea();
            foreach (var id in new List<string>(galleryPreviews.Keys))
                if (!visibleIds.Contains(id)) { if (galleryPreviews[id] != null) Destroy(galleryPreviews[id]); galleryPreviews.Remove(id); }
        }
        private void OpenGalleryFile(string file)
        {
            try
            {
                if (!File.Exists(file)) { Notice("Local footage is no longer available", false, false); return; }
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + Path.GetFullPath(file) + "\"") { UseShellExecute = true });
            }
            catch { Notice("Unable to open file location", false, false); }
        }
        private void ClearGalleryPreview()
        {
            previewGeneration++; retryConfirmation = null;
            foreach (var texture in galleryPreviews.Values) if (texture != null) Destroy(texture);
            galleryPreviews.Clear();
        }
        private void RetryGallery(string id, bool confirmed)
        {
            if (!hostSettings.Ready || !Value(discordEnabled) || encoding != null || raidWorker != null || history?.IsBusy == true ||
                upload != null || localInspection != null || highlightQueue?.Busy == true)
            { Notice("Retry unavailable while capture or delivery is busy", false, false); return; }
            var session = ZNet.instance;
            var e = gallery.BeginRetry(id, session, session != null && session.IsServer(), confirmed);
            if (e == null) { Notice("Retry unavailable or still cooling down", false, false); return; }
            activeGallery = e; activeKind = e.Kind; activeMessage = e.Caption; activeRecorder = e.Recorder;
            activeSession = session; activeAsHost = e.AsHost; activeBoss = null; activeDeath = null;
            var deathTicket = e.Acknowledgement as DeathMoments.Ticket;
            if (deathMoments.Reopen(deathTicket)) activeDeath = deathTicket;
            activeEventId = null;
            activeOutput = gallery.PathFor(e);
            try { StartUpload(activeOutput); } catch { gallery.Complete(e, "Failed"); FinishMoment(activeDeath, activeSession, false, "Retry failed"); }
        }
        private void StopCapture()
        {
            if (stopped) return;
            stopped = true;
            cinematic?.Dispose(); cinematic = null; cinematicHarmony?.UnpatchSelf();
            galleryOpen = false; ClearGalleryPreview(); gallery?.Flush();
            GalleryInput.Open = null; galleryHarmony?.UnpatchSelf();
            CancelRaid(); RaidDetector.OnTransition = null; raidHarmony?.UnpatchSelf();
            raidIdentityHarmony?.UnpatchSelf(); RaidIdentity.Clear();
            CancelCloseCall(); LocalDamageDetector.OnDamage = null; closeHarmony?.UnpatchSelf();
            notifications.Dispose(); deathMoments.Clear(); deathOffers.Clear();
            DiscoveryDetector.OnObserved = null; DiscoveryDetector.OnError = null; DiscoveryDetector.Clear();
            discoveryHarmony?.UnpatchSelf();
            var flush = discoveryHistory?.Flush();
            if (flush != null) _ = flush.ContinueWith(task => { var ignored = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            hostSettings?.Dispose();
            highlightQueue?.Stop();
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
