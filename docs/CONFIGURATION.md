# Configuration reference

## Cinematic Camera: Experimental (host controlled)

| Setting | Default | Behavior |
| --- | --- | --- |
| RenderCinematics | true | Personal: render extra camera footage on this computer. Off cancels local cinematic work; normal recording and host event policy continue. |
| RaidOpening | false | Separate elevated shot toward the raid center, included when the raid ends. |
| SpecialEnemySpawnIntro | false | Fresh selected special enemy arrival; attached when that exact enemy dies. |
| SpecialEnemyKillEnding | false | Selected special enemy aftermath camera. |
| SpecialEnemyMovement | Orbit | Independent Orbit, RiseAndReveal or ZoomIn; shares distance, pan, letterbox and flip controls with bosses. |
| SpecialEnemySpawnDelaySeconds | 0 | 0-5 seconds after fresh creation; loaded existing enemies do not qualify. |
| BossSpawnIntro | false | Fresh boss arrival shot, retained up to 30 minutes and attached first when the same boss dies. |
| BossKillEnding | false | Separate cinematic sweep of the boss death location. |
| BiomeDiscovery | false | Enable the separate cinematic on eligible first biome discovery; uses BiomeMovement and BiomeLetterbox. |
| BiomeMovement | RiseAndReveal | RiseAndReveal, Orbit or ZoomIn around the player. Shares DistanceMultiplier and PanDegrees; ZoomIn ignores them and approaches from 2x to 1x over four seconds. |
| BiomeLetterbox | true | Animated black bars and fading biome title, matching boss presentation. Independent of Letterbox for other events. |
| DistanceMultiplier | 2 | 1–3 times the original camera distance. Original framing is the closest; obstruction and the 80 m offset cap may shorten it. |
| PanDegrees | 70 | Total horizontal sweep, 0–180 degrees. |
| RaidMovement | RiseAndReveal | RiseAndReveal, Orbit, or ZoomIn toward the raid center. ZoomIn approaches from 2x to 1x distance over four seconds. |
| BossMovement | Orbit | Orbit, RiseAndReveal, or ZoomIn for boss arrivals and aftermath. ZoomIn approaches the boss from 2x to 1x distance over four seconds. |
| BossSpawnDelaySeconds | 0 | Wait 0-5 seconds after actual boss creation, following the altar summon delay. Scan adds up to 0.5 seconds; eligibility lasts three seconds after the delay. |
| Letterbox | true | Slide top/bottom bars inward over 0.85 seconds to a 2.39:1 window inside the 16:9 file. Cinematic footage only; biome discoveries use BiomeLetterbox. |
| FlipVertically | true | Player-owned orientation correction for the extra camera; independent of Capture.FlipVertically. |

ZoomIn is a movement dropdown choice with a fixed 55-degree lens and a straight approach. It ignores DistanceMultiplier and PanDegrees; obstruction checks can shorten the path. The old test-build ZoomIn checkbox is no longer used. Select BossMovement = ZoomIn to enable it for bosses.

Fixed experimental camera budget: four seconds, 640x360, 10 FPS, local WebP quality; one camera/encoder job at a time. Director includes each enabled shot once, within existing upload limits. Requires matching 0.23.0 clients/host. Normal footage remains the fallback. See [camera behavior and live tests](CINEMATIC-CAMERA.md).

Boss arrivals display localized name, actual star count and reported maximum health. Raid openings display their localized event message and up to three configured possible enemy types when available. These are spawn-time snapshots; unavailable details are omitted. Titles fade in after the shot begins. All controls above are host-owned except RenderCinematics and the camera FlipVertically correction.

## Player Identity (personal)

| Key | Default | Meaning |
| --- | --- | --- |
| DiscordUserID | empty | Your numeric Discord user ID, not username. Adds your character name followed by a user mention in parentheses to Recorded by and Kill credit. Blank opts out. Shared only with the connected host for attribution. |

Discord resolves the displayed account/server name for the mention; no account lookup or OAuth verification is performed. Duplicate character names are left unmentioned. Missing/unregistered/disconnected players retain character-only attribution. Only configured user mentions are allowed; role and everyone pings remain disabled. The character name in titles, gameplay, gallery and video nameplates remains unchanged. PlayerNameOverride and the separate Special Enemies.FirstKillOnly switch are removed on upgrade; CaptureMode remains authoritative and existing explicit choices are preserved.

## Upgrade preservation

Before a mod-manager update can replace the old plugin directory, close Valheim and run the bundled Preserve-DiscoveryHistory.ps1 with -ProfilePath pointing to the profile containing BepInEx. It validates and merges all character/world discovery journals into BepInEx/config/ValheimMoments/Discoveries, backs up originals there in a separate DiscoveryBackups directory, and leaves plugin-side files intact. Repeating it is safe. The manual installer runs this automatically before copying binaries. Run it for each recording player's profile, not just the server. The new plugin cannot recover history that an updater has already deleted.

Applies to Valheim Moments 0.23.0. Launch once to generate
`BepInEx/config/local.valheimmoments.cfg`, then close Valheim before editing it.
Restart after editing the file. Configuration Manager edits apply in game; buffer
changes wait for active GPU/encoder work. Defaults describe a new installation; upgrades preserve
existing settings. Never share a config containing webhook URLs.

## Multiplayer ownership

The host controls every setting except: Capture.Enabled, ManualCaptureKey,
ToggleCaptureKey, Width, Height, FPS, WebPQuality, MemoryBudgetMiB, FlipVertically;
Capture.SizePreset and Capture.SaveLocalCopy; Cinematic Camera: Experimental.FlipVertically; all Notifications and Gallery settings; and Debug.LogCaptureTiming.
These personal controls remain editable on clients. All other settings are read-only
while connected. Private Discord settings are hidden from joining players' UI.

Host event rules apply in memory without replacing clients' saved settings. Returning
to single-player restores their own preferences and editing access. Host and clients
need matching 0.23.0 versions: client capture waits for host settings and
pauses if updates stop for ten seconds. No webhook URL or Discord Username is synced.

## Gallery (player controlled)

Recovery limits also apply during shutdown; flushing never resets them to defaults. The 30-second completion grace takes precedence over age expiry. If index.bin cannot be read, the gallery preserves it and unrecognized existing media, shows a persistent error, and does not overwrite it with new history. Close the game and back up the Gallery folder before repairing/restoring the index. Until then, new history cannot persist across restart; permanent kept files still remain on disk.

| Key | Default | Behavior |
| --- | --- | --- |
| GalleryKey | F8 | Toggle the personal gallery. |
| KeepMomentKey | F7 | Keep the collecting/encoding or latest available memory. |
| RecoveryClips | 20 | Completed temporary clips, 1-100; active work and 30-second Keep grace excluded. |
| RecoveryMiB | 250 | Temporary budget, 10-1024 MiB; permanent Saved clips excluded. |
| RecoveryHours | 24 | Temporary expiry, 1-168 hours. |
| IndexEntries | 200 | History entries, 20-1000; evicting history never deletes a saved original. |

Gallery/Recovery contains temporary originals; Gallery/Saved contains permanent kept originals. Successful unpinned uploads expire after 30 seconds. Failed/omitted/unknown deliveries use recovery quotas. The old Clips folder is preserved; existing historical clips are not automatically imported or deleted. Thumbnails remain with bounded gallery history and load automatically beside each card's information/actions, six entries per page. F8 (or your configured key) toggles the gallery; clicking outside also closes it. Mouse-look and scroll zoom are blocked while browsing. Open file location selects surviving footage in Explorer. Retry appears only for Failed, Omitted or Unknown outcomes and retains session, cooldown, file and attempt limits. The Discord-link button and its optional metadata lookup have been removed. Existing gallery history remains compatible.

Retries are deliberate, never automatic: maximum three, with 30/60/120-second backoff. Unknown delivery requires a duplicate-post confirmation. Original session and host/client role must still match; restarting or switching worlds disables retry for old records. Discord routes and usernames remain host controlled and are never stored in gallery metadata. Correct destination/authentication/size errors before retrying. Keep works while an encoder/upload owns the original; permanent moves wait for completion. Disk failures are shown in the gallery.

## Director delivery

Host-owned multiplayer collection is enabled by default. A confirmed creature-death occurrence ID groups boss, special-enemy and kill-loot perspectives. Manual, death, discovery and missing-ID clips remain individual and skip the collection delay, although a busy transfer/upload queue can still delay them. The host's own perspective is selected first when available, then connected peers in stable connection order; this is not visual-quality scoring. The primary selected perspective supplies the shared caption/loot summary; individual recorder names and first-kill status are listed separately.

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Collect compatible perspectives into one Discord post. Disable for individual delivery. |
| MaxPerspectives | 3 | Select 1–3 distinct recorders per post. |
| MaxPostMiB | 20 | Combined attachment bytes, clamped to 1–30 MiB; excludes multipart overhead. Set for your Discord destination. |
| CollectionSeconds | 10 | Wait 1–30 seconds from the first encoded offer. Late perspectives can be omitted. |

Offers reserve at most 60 MiB across 16 queued perspectives before remote footage is transferred. At most one client transfer and one grouped upload run at a time. The per-file guard is the lower of Discord.MaxUploadMiB and 10 MiB while the director is enabled, including the host's own clip. Larger clips are omitted and retained; automatic re-encoding/tier detection are not implemented. The queue expires offers after 20 minutes and uses bounded wait heartbeats. Completed-event deduplication covers the latest 256 groups for up to 30 minutes within the same session.

Selected multi-perspective clips receive a bottom-center recorder nameplate through a second encoding pass. Single-perspective files remain unchanged. Final encoded sizes are checked against the per-file and aggregate budgets; a preparation/size failure retains originals for recovery. Only successfully included attachments receive Sent to Discord and qualify for removal according to each player's SaveLocalCopy. Omitted/failed clips remain local. An uncertain confirmation says to check Discord before retrying. Cancellation preserves a host temporary file until any HTTP reader finishes; crash/access-failure orphan cleanup remains pending. Matching 0.23.0 host and client versions are required.

## Capture

Performance controls belong to each recording player; PreEventSeconds and
PostEventSeconds belong to the host. Dedicated servers relay clips but
do not record a screen. Output has no audio.

Recording requires a session and local player. Changing sessions clears buffered
footage and cancels any collecting clip. Allow the pre-event history to warm up after
joining. An already encoding clip retains its frames and can finish locally, but
cannot submit through a different session.

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Enable recording; F9 temporarily pauses/resumes it. |
| ManualCaptureKey | F10 | Manual capture key; also requires Triggers.ManualCapture. |
| ToggleCaptureKey | F9 | Pause/resume key. |
| SaveLocalCopy | false | Player-owned local saving independent of Discord. Old Discord.SaveLocalCopy migrates here. Successful unpinned uploads delete originals after a 30-second grace; failed delivery uses bounded recovery. |
| SizePreset | Small | Tiny, Small, Medium, Balanced, Large, Ultra, or Custom. Six aspect-aware pixel budgets; 16:9 equivalents below. Old Width/Height migrate to Custom. |
| PreEventSeconds | 5 | Host-controlled history duration, 1–30 seconds. |
| PostEventSeconds | 2 | Host-controlled duration for manual clips and deaths, 0–30 seconds. |
| FPS | 15 | Sampling rate, 1–30. Missed samples are skipped. |
| Width | 640 | Output width, 480–1920 pixels. |
| Height | 360 | Output height, 270–1080 pixels. Effective aspect ratio is limited to 1:2 through 3:1. |
| WebPQuality | 80 | Lossy quality, 1–100. Higher quality can increase file size. |
| MemoryBudgetMiB | 192 | Managed frame-pool budget, 48–512 MiB; excludes GPU and encoder memory. |
| FlipVertically | false | Enable only if recordings appear upside down. |

Bosses, ordinary loot, discoveries and special enemies use their own PostEventSeconds. Allocation uses the largest
post-event duration, even if that trigger is disabled. History plus that duration
must not exceed 60 seconds. The raw clip must fit 256 MiB and the whole frame pool
must fit MemoryBudgetMiB. Numeric values are clamped in both UI and config file.
Capture preserves the source aspect ratio, padding mismatched/custom canvases. If the combination exceeds
memory limits, effective dimensions reduce while preserving their ratio, then FPS reduces. Extreme long/portrait requests may need a padded 480 x 270 canvas at 1 FPS. The log reports the actual
dimensions/FPS/allocation; stored resolution and FPS preferences are retained.

Let P = ceil(PreEventSeconds × FPS) and Q = ceil(maximum post-event seconds × FPS).
The frame pool occupies `Width × Height × 4 × (2P + Q + 1)` bytes: about 185.4 MiB
at defaults. Increasing resolution, duration or FPS can substantially increase this.

### Size presets and upload budgets

At 16:9: Tiny 480x270, Small 640x360, Medium approximately 854x480, Balanced 960x540, Large 1280x720, Ultra 1920x1080. Other screens use their aspect ratio with similar pixel budgets. Minimums can make adjacent presets converge. Custom Width/Height appear directly below the picker; they are editable only in Custom mode. The log reports effective dimensions and FPS.

WebP size varies with motion, detail, duration, FPS, dimensions and quality; quality is not a target bitrate. A representative gameplay benchmark for per-preset estimates remains pending; synthetic encoder checks are not reliable size estimates for combat. Check actual encoded file sizes.

Discord's current API source lists a 20 MiB base attachment limit; server boosts can affect destination allowances. Personal Nitro is not a webhook entitlement. MaxUploadMiB is a host guard, not automatic tier detection. Client relay remains capped at 10 MiB in this milestone. See [Discord's API reference](https://github.com/discord/discord-api-docs/blob/main/developers/reference.mdx#uploading-files).

With Discord disabled, SaveLocalCopy=true records locally; both false suppress new triggers. With Discord enabled, SaveLocalCopy selects whether successful uploads keep a local original. Invalid destinations retain recovery files; bounded expiry and gallery controls apply as described above.

## Discord

In single-player these settings belong to the local player. In multiplayer, the host
owns delivery, destinations and the bot name. Joining players' local webhook,
Enabled and Username cannot override the host. Both sides need the mod and
matching 0.23.0 settings protocol for client delivery. Discord.Enabled automatically gates relay and upload.

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Host/solo Discord delivery switch, synchronized to clients without sharing secrets. Existing explicit false is preserved. |
| WebhookURL | empty | Secret default destination, including manual clips. |
| Username | Valheim Moments | Host-selected bot display name, 1–30 characters. |
| MaxUploadMiB | 10 | Local guard, clamped to 1–100 MiB. Relay limit is 10 MiB or the host's lower limit. Discord may reject a smaller file. |
| UseSpecialEnemyWebhook | false | Route special enemy clips to SpecialEnemyWebhookURL when enabled. |
| SpecialEnemyWebhookURL | empty | Host-only secret. An invalid enabled override keeps clips locally. |
| UseBossKillWebhook | false | Enable separate boss destination. |
| BossKillWebhookURL | empty | Secret boss destination. |
| UseGoodLootWebhook | false | Enable separate ordinary-loot destination. |
| GoodLootWebhookURL | empty | Secret ordinary-loot destination. |
| UsePlayerDeathWebhook | false | Enable separate death destination. |
| PlayerDeathWebhookURL | empty | Secret death destination. |
| DiscoveryWebhookURL | empty | Secret discovery destination. Blank uses WebhookURL; nonblank invalid URLs retain the clip instead of changing channels. No separate enable switch. |

Disabled overrides use WebhookURL. An enabled override with a missing/invalid URL
retains the clip locally instead of changing channels. Supported HTTPS Discord webhook
URLs can include an optional thread_id query. There is no client webhook fallback if
relay delivery is unavailable. Changing sessions cancels pending delivery rather than
submitting old footage through a new host.

Every post has **Recorded by:** beneath its title. Host/solo names are captured at
the event trigger; joining-player names come from the host's connected peer record.
This line is separate from bot Username, kill credit and final blow. Player Death's
name override and IncludePlayerName affect only the death message, not Recorded by.

Existing installs retain their configured SaveLocalCopy value: change it to false to
enable successful-upload cleanup. Session changes do not delete saved files. Previously
saved clips and failed/skipped uploads are not automatically purged by this option.

## Triggers

| Key | Default | Meaning |
| --- | --- | --- |
| ManualCapture | true | Allow the manual key. |
| PlayerDeath | true | Allow local death captures; also requires Player Death.Enabled. |
| BossKill | true | Allow credited boss captures; also requires Boss Kill.Enabled. |
| LootDrop | true | Allow loot captures; also requires Loot Capture.Enabled. Named rarity thresholds require Epic Loot. |

The host also applies these switches and the corresponding event Enabled setting to
incoming clips. Recording clients evaluate the host's synced rarity/event rules.
The host does not independently verify footage or loot claims from modified clients.

## Notifications

These controls belong to each recording player and appear after their Capture controls.

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Brief themed on-screen feedback. Off also mutes the cue. |
| Style | ToastMinimap | Cinematic: centered banner; ToastMinimap (Toast: Minimap): below the existing minimap with corner fallback; ToastTopRight (Toast: Top Right): inset corner. All slide and fade. |
| SoundMode | OnCapture | Off, OnCapture, OnCompletion, or Both. Completion sound plays for confirmed upload or local-only save. |
| Volume | 0.35 | Local cue level, clamped 0-1. The original two-tone cue is deliberately quiet. |

Saving Memory appears after the selected ending footage is collected, as encoding begins. Sent to Discord requires Discord success or the host's final successful acknowledgement. Local-only captures say Memory Saved. The intermediate Memory Captured notice is removed. Failures/busy captures use distinct feedback. New feedback replaces the previous notice, expires after 3.5 seconds and clears on session changes. Notifications render after the screenshot copy to exclude them from captured footage; live visual validation is still required. The minimap toast follows the existing small HUD minimap and falls back when unavailable; replacement or camera-space minimap mods need live compatibility testing. WebP output still has no audio. Notification settings do not cancel a capture when edited.

## Player Death

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Enable this event. |
| CaptureLimit | 1 | Host-owned maximum death capture attempts per player within WindowSeconds, 1-20. |
| WindowSeconds | 60 | Sliding window in seconds, 1-3600. |
| CheekyMessages | true | One of 20 neutral lines with no immediate repeat for the default caption. Custom templates opt in with {flavor}. |
| Message | 💀 {player} died! | Supports {player}, {cause}, {flavor}, and {extra_deaths}. |
| IncludeCause | true | Include recorded attacker/environmental cause; append if {cause} is absent. |
| IncludePlayerName | true | Use character name/override for {player}; otherwise “A player”. |

Confirmed deaths while the event is enabled are counted even when a clip is suppressed or capture is busy/paused. The next eligible death caption reports additional deaths since the last shared death. Only confirmed upload, or a completed local-only save, consumes that snapshot. Failed delivery preserves the count; new deaths during upload remain for the next post. Only one death clip can await delivery per player. Counts reset on session change; no historical death footage is queued.

The host also limits incoming death offers per connection. Existing general relay throttling still applies, so a high CaptureLimit is not a guaranteed delivery rate. Unknown/modified client death counts are not independently verified. Manual clips do not reset the automatic death quota.

Unresolved causes remain unknown. Periodic damage can lack an attacker even when the
original weapon belonged to a player.

## Boss Kill

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Enable credited boss captures. |
| CaptureMode | FirstKillThenRarity | Always capture first kill, then require rarity. Other modes: AllKills, FirstKillOnly, RarityOnly; FirstKillWithRarity preserves the old combined first-only AND rarity restriction. Existing policy migrates. |
| MinimumLootRarity | Legendary | None accepts all; otherwise a verified Epic Loot rarity. |
| Message | 🏆 {boss} defeated! | Supports {boss}, {player}, {credit}, {killer}, {loot}, {item_count}. |
| PlayerNameMode | Both | KillCredit, FinalBlow or Both; selected names append if omitted. |
| TrackPeriodicDamage | true | Host-controlled tracking of actual boss Spirit/fire/poison sources on the creature owner. |
| PostEventSeconds | 4 | Recording time after the credited kill. |
| LootWaitSeconds | 12 | Metadata wait, clamped to 0–25 seconds; does not extend recording. |
| ShowLoot | true | Include loot in the post; does not change rarity eligibility. |

First kill comes from the character's saved Valheim statistics, including kills before
this mod was installed. It is not per world or per successful upload. CaptureMode determines whether repeat kills need qualifying rarity or are excluded.

In Both mode, {player} means kill credit; in FinalBlow mode it means final blow.
Credit lists the connected players whose attacker records Valheim checks when awarding
that boss kill. Spectators are excluded. Names are sorted and bounded; a missing
owner snapshot falls back to the confirmed recording character with "full list
unavailable". {credit} and {player} in Both/KillCredit mode use that roster. Personal
first-kill history and Recorded by stay tied to the recording character. Final blow is the
last-hit player when resolvable, otherwise “unavailable”; it does not mean highest
damage. TrackPeriodicDamage can resolve Spirit/fire/poison ticks from one known player.
Mixed or unknown effect sources remain unavailable. Fire/Spirit additions combine;
weaker poison applications leave the existing source alone, while accepted equal or
stronger poison replaces it. Tracking never changes damage or game kill credit.
Only boss final-blow attribution uses this feature; death-cause messages are unchanged.

With filtering enabled and no first-kill bypass, missing qualifying data skips the
clip at the metadata deadline. Epic Loot 0.14.2 provides Magic, Rare, Epic, Legendary,
Mythic and Ancient; names/ranks are loaded from the adapter. Unknown names or
unavailable Epic Loot cannot satisfy a named rarity threshold.

## Discoveries

All settings belong to the host. Physical exploration is observed on each recording client; a dedicated server has no personal discoveries or camera. These events use DiscoveryWebhookURL or the default route.

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Capture first tracked discoveries per character per world. |
| Biomes | true | First tracked physical visit to each main biome per character/world. |
| SubBiomes | false | Host-controlled: also announce distinct named sub-biomes once. Requires Biomes. Hidden modifiers do not count; visits while disabled are saved silently and do not replay when enabled. |
| NamedLocations | true | Observe physical entry into a location with a game discovery label. Map-pin reveals do not count. |
| Traders | true | Observe the local character entering an NPC trader's greeting range, bounded to 1-30 game units. |
| PostEventSeconds | 3 | Recording time after a discovery group is accepted, 0-30 seconds. |
| CooldownSeconds | 30 | Minimum seconds between accepted discovery captures, 0-3600. |
| Message | 🧭 Discovered {discovery}! | Supports {discovery} (one or several names) and {player}; Recorded by appends at delivery. |

Nearby discoveries group for 1.25 seconds, up to eight names. The ledger marks observations before capture eligibility: loading, warmup, disabled categories, pause, busy capture, cooldown and delivery failure do not replay discoveries. Warmup lasts at least five seconds or PreEventSeconds, whichever is longer. Existing character-wide exploration cannot reconstruct separate world histories; places visited before installation may qualify on their first tracked return outside warmup.

History is stored locally in `BepInEx/config/ValheimMoments/Discoveries`, outside the replaceable plugin folder, with numeric character/world filenames and atomic asynchronous saves. Existing `ValheimMoments/State/Discoveries` journals migrate automatically when the character/world is loaded; legacy files are preserved. Existing variant records are retained and matched as legacy aliases when observed, seeding the new identity silently. Named sub-biomes use stable untranslated naming data; hidden modifiers and display language changes do not create a new identity. Preserve the config history when moving profiles. It is bounded to 256 discovery identities per context and 128 files; capacity or corrupt/unwritable history skips further affected discoveries instead of resetting them. It does not modify Valheim save data. Announcements wait for a successful durable journal write. Failed writes suppress the announcement; a crash before commit may lose the observation but cannot leave an already-posted discovery unrecorded. Deleting this history resets tracking. Client histories are not a server anti-cheat mechanism.

## Special Enemies

Event settings belong to the host; LogEnemyKeys is a local diagnostic. Special events use the default Discord route unless UseSpecialEnemyWebhook is enabled. No maintained enemy list or assumed miniboss flag is required.

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Capture explicitly selected ordinary-enemy kill credits; empty EnemyKeys selects none. |
| EnemyKeys | empty | Exact case-sensitive confirmed kill-credit/stat keys, comma/semicolon/newline separated. At most 64 keys, 128 characters each, 4096 total. No wildcards or prefab-name matching. |
| CaptureMode | AllKills | AllKills, FirstKillOnly, RarityOnly, FirstKillThenRarity, FirstKillWithRarity, using this character's saved enemy history. |
| PlayerNameMode | Both | KillCredit, FinalBlow or Both; supports {credit} and {killer}. |
| ShowLoot | true | Include the independently configured special enemy loot summary. |
| MinimumLootRarity | Legendary | Minimum verified rarity for rarity-based capture modes; None accepts all. |
| LootWaitSeconds | 12 | 0-25 seconds to wait for completed loot metadata. |
| MaxLootItemsShown | 5 | 1-20 entries, highest verified rarity first. |
| LootHeader | Generated loot: | Heading for special enemy loot. |
| ShowQuantity | true | Item quantities. |
| ShowRarity | true | Verified rarity names and color emojis. |
| ShowItemModifiers | true | Identified Epic Loot modifiers. |
| ShowItemSockets | true | Verified sockets. |
| ShowUnidentifiedStatus | true | Label unidentified items without exposing hidden modifiers. |
| PostEventSeconds | 4 | Seconds after the credited kill, 0-30. Uses its own loot display settings and metadata wait. |
| CooldownSeconds | 60 | Seconds between accepted captures of the same key, 0-3600. Resets on session or selection changes. |
| Message | ⚔ {enemy} defeated! | Supports {enemy}, {boss}, {player}, {credit}, {killer}, {loot}, {item_count}; selected attribution appends if omitted. |
| LogEnemyKeys | false | Local advanced diagnostic: log actual confirmed ordinary-enemy keys on recording clients. Copy the logged key into EnemyKeys; disable afterward. |

To find a key, enable your local Advanced `LogEnemyKeys` setting, kill an ordinary enemy and receive kill credit, then search that recording player's active profile `BepInEx/LogOutput.log` for `[Special] Confirmed enemy key:`. Copy the exact value, including any `$`, into `EnemyKeys`, then disable the diagnostic. Example: `EnemyKeys = $enemy_troll, $enemy_wraith`.

[Jotunn's English localization table](https://valheim-modding.github.io/Jotunn/data/localization/translations/English.html) lists vanilla name keys; search for the enemy's name. It is a localization reference, not a guaranteed list of confirmed kill-credit keys for your installed game and mods. The logged value is authoritative, especially for modded enemies. Spawn-code lists are not equivalent.

Accepted special captures replace the ordinary loot capture for that credited death and bypass ordinary-loot rarity. When a special capture is ineligible, ordinary loot may still qualify normally. Normal bosses remain exclusive to Boss Kill rules. Special captions support the same confirmed-credit roster and final-blow attribution as bosses; missing owner metadata remains explicitly unavailable. Special capture modes apply their own rarity threshold. Clients cannot override the host selection. Unknown identifiers simply do not match.

## Loot Capture

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Capture qualifying ordinary kill loot and verified natural acquisitions. None works without Epic Loot. |
| CaptureChestPickups | true | First acquisition of tracked natural chest loot; host controlled. |
| CaptureWorldPickups | true | First acquisition of tracked natural pickable/breakable drops; host controlled. |
| PickupMessage | Great loot from {source}! | Supports {source}, {player}, {loot}, {item_count}; collector and loot append if omitted. |
| MinimumRarity | Legendary | Minimum observed rarity; None accepts any actual observed item, including vanilla drops. |
| Message | Great loot from {enemy}! | Supports {enemy}, {player}, {loot}, {item_count}; credit and loot append if omitted. |
| PostEventSeconds | 4 | Recording time after qualifying loot is observed. |
| LootWaitSeconds | 12 | Metadata wait after credit, clamped to 0–25 seconds. |

Bosses exclusively use Boss Kill rules; their drops do not gain a second pickup trigger.
Only proven natural chest/world acquisitions qualify; player storage, gravestones,
player drops, crafted items, older untagged loot and unknown generation paths are excluded.
Natural generation hooks run on hosts/clients even with capture disabled, including
headless hosts, and store a small item custom-data marker. Successful transfers consume
it even when disabled, busy or below threshold. Deposits and mixed stacks lose eligibility.
The first partial acquisition reports its actual quantity and consumes the whole stack
opportunity. Failed transfers with no items acquired can be retried. Take all groups
items for 0.25 seconds (maximum 64 entries per source category). PickupMessage is used
for acquisitions; Message remains the credited-kill template.

A long ragdoll delay can put the kill outside the rolling history;
boss capture instead preserves kill footage while waiting for metadata. None still
requires an actual drop. Display limits never determine eligibility.

## Loot display settings

These keys exist independently under both Boss Kill and Loot Capture. New Loot
Capture entries inherit existing boss values once, then remain independent.

| Key | New-install default | Meaning |
| --- | --- | --- |
| ShowQuantity | true | Show quantities. |
| ShowRarity | true | Show verified rarity labels and approximate colored markers. |
| ShowItemModifiers | true | Show Epic Loot's formatted identified-item modifiers. |
| ShowItemSockets | true | Show identified-item socket counts. |
| ShowUnidentifiedStatus | true | Label unidentified items; disabling never reveals hidden properties. |
| MaxLootItemsShown | 5 | Display 1–20 entries, highest rarity first, then deterministic name order. |
| LootHeader | Generated loot: | Subheading above the item list. |

Vanilla quantities group by prefab; distinct magic items stay separate. {item_count}
counts entries before the display limit, rather than total stack quantity. Posts use
a # title, ## loot subheading, item bullets and bold attribution labels. Captions are
limited to 2,000 characters and may truncate long loot summaries.

When ShowRarity is enabled, an identical rarity at the beginning of the localized
item name is shown only once. Each modifier/socket line uses exactly two leading
spaces followed by `* -# ` for the requested Discord sub-bullet/subtext format.

## Debug

Shown only when Configuration Manager's **Advanced** filter is enabled. This remains
a personal troubleshooting control. The config section stays Debug to preserve
existing preferences; existing true values are not reset during upgrade.

| Key | Default | Meaning |
| --- | --- | --- |
| LogCaptureTiming | false | Aggregate capture timing, readback latency and frame counts every ten seconds. |

Event, encoder, upload and attribution diagnostics also use the BepInEx log.

## Close Calls (host controlled)

| Key | Default | Behavior |
| --- | --- | --- |
| Enabled | true | Actual damage crossing followed by twenty seconds alive; personal clip, main webhook. |
| ThresholdPercent | 5 | Crossing from above to at/below this health percentage; 1-15. |
| RecoveryPercent | 20 | Must regain 16-100% health before rearming. |
| RecoverySeconds | 10 | Hold recovery continuously for 1-120 seconds. |
| CooldownSeconds | 120 | Minimum 0-3600 seconds between attempts; recovery is also required. |
| FollowUpSeconds | 20 | Must survive 5-60 source seconds after the hit. |
| SlowSourceSeconds | 1 | 0.5-3 source seconds around the hit: two thirds before, one third after. Default slowdown shows the hit at playback second 2, then one second of slow aftermath. |
| SlowPlaybackSeconds | 3 | 1-5 playback seconds for the slow segment. |
| PlaybackSeconds | 10 | 6-20 total playback seconds; the remaining time contains the sampled follow-up. |

Defaults stretch one source second around the hit to three playback seconds (two thirds before impact, one third after), then compress the remaining survival footage into seven playback seconds. Survival still requires twenty seconds after the hit. The settings above customize this with independent safe ranges: slow playback is always shorter than total playback. Capture history expands for the slow source and memory fitting reserves the selected output frame budget. Death, pause, policy loss, character/world change or reconfiguration cancels pending footage. Recovery plus cooldown is still required after cancellation. No game timescale changes or generated frames.

## Raids (host controlled)

| Key | Default | Behavior |
| --- | --- | --- |
| Enabled | true | Four-second opening plus six-second ending; personal default-route clip labeled Raid ended. |
| OpeningSeconds | 4 | 1-10 seconds after local entry. |
| EndingSeconds | 6 | 1-10 seconds after observed end. Total duration is the sum of both segments. |

Opening and ending durations are configurable. Leaving, death, pause, host-policy loss, character/session change or capture reconfiguration abandons the attempt. An opening expires after thirty minutes. Busy or missing segments skip delivery. Normal capture resumes after opening encoding; death can replace a collecting segment but cannot immediately reuse raw frames still owned by an encoder. Memory fitting reserves both the longest segment and the combined raw-equivalent composition budget. Composition uses a second lossy encoding pass. Intermediate files are removed after workers finish on normal cancellation; restart cleanup removes recognized transient files older than 24 hours. Host-issued occurrence IDs allow participant perspectives to share director posts; missing or unmatched identity remains personal.
