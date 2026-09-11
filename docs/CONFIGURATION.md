# Configuration reference

Applies to Valheim Moments 0.14.0. Launch once to generate
`BepInEx/config/local.valheimmoments.cfg`, then close Valheim before editing it.
Restart after editing the file. Configuration Manager edits apply in game; buffer
changes wait for active GPU/encoder work. Defaults describe a new installation; upgrades preserve
existing settings. Never share a config containing webhook URLs.

## Multiplayer ownership

The host controls every setting except: Capture.Enabled, ManualCaptureKey,
ToggleCaptureKey, Width, Height, FPS, WebPQuality, MemoryBudgetMiB, FlipVertically;
Capture.SizePreset and Capture.SaveLocalCopy; all Notifications settings; and Debug.LogCaptureTiming.
These personal controls remain editable on clients. All other settings are read-only
while connected. Private Discord settings are hidden from joining players' UI.

Host event rules apply in memory without replacing clients' saved settings. Returning
to single-player restores their own preferences and editing access. Host and clients
need matching 0.14.0 versions: client capture waits for host settings and
pauses if updates stop for ten seconds. No webhook URL or Discord Username is synced.

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
| SaveLocalCopy | false | Player-owned local saving independent of Discord. Old Discord.SaveLocalCopy migrates here. Successful uploads delete originals when false; failed delivery retains a recovery copy. |
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

With Discord disabled, SaveLocalCopy=true records locally; both false suppress new triggers. With Discord enabled, SaveLocalCopy selects whether successful uploads keep a local original. Invalid destinations retain recovery files; bounded expiry and a gallery are planned, not yet implemented.

## Discord

In single-player these settings belong to the local player. In multiplayer, the host
owns delivery, destinations and the bot name. Joining players' local webhook,
Enabled and Username cannot override the host. Both sides need the mod and
matching 0.14.0 settings protocol for client delivery. Discord.Enabled automatically gates relay and upload.

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Host/solo Discord delivery switch, synchronized to clients without sharing secrets. Existing explicit false is preserved. |
| WebhookURL | empty | Secret default destination, including manual clips. |
| Username | Valheim Moments | Host-selected bot display name, 1–80 characters. |
| MaxUploadMiB | 10 | Local guard, clamped to 1–100 MiB. Relay limit is 10 MiB or the host's lower limit. Discord may reject a smaller file. |
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
| SoundMode | OnCapture | Off, OnCapture, OnCompletion, or Both. Completion sound plays for confirmed upload or local-only save. |
| Volume | 0.35 | Local cue level, clamped 0-1. The original two-tone cue is deliberately quiet. |

Recording Memory appears only after accepting a capture. Memory Captured means encoding finished; Memory Uploaded requires Discord success or the host's final successful acknowledgement. Local-only captures say Memory Saved. Failures/busy captures use distinct feedback. New feedback replaces the previous banner, expires after 3.5 seconds and clears on session changes. It may appear in the captured footage. WebP output still has no audio. Notification settings do not cancel a capture when edited.

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
| PlayerNameOverride | empty | Optional replacement name for the death message. |

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
| Biomes | true | Observe physical biome transitions, including raw alternate-biome identities. |
| NamedLocations | true | Observe physical entry into a location with a game discovery label. Map-pin reveals do not count. |
| Traders | true | Observe the local character entering an NPC trader's greeting range, bounded to 1-30 game units. |
| PostEventSeconds | 3 | Recording time after a discovery group is accepted, 0-30 seconds. |
| CooldownSeconds | 30 | Minimum seconds between accepted discovery captures, 0-3600. |
| Message | 🧭 Discovered {discovery}! | Supports {discovery} (one or several names) and {player}; Recorded by appends at delivery. |

Nearby discoveries group for 1.25 seconds, up to eight names. The ledger marks observations before capture eligibility: loading, warmup, disabled categories, pause, busy capture, cooldown and delivery failure do not replay discoveries. Warmup lasts at least five seconds or PreEventSeconds, whichever is longer. Existing character-wide exploration cannot reconstruct separate world histories; places visited before installation may qualify on their first tracked return outside warmup.

History is stored locally in `ValheimMoments/State/Discoveries`, with numeric character/world filenames and atomic asynchronous saves. Preserve this folder across upgrades/profile moves. It is bounded to 256 discovery identities per context and 128 files; capacity or corrupt/unwritable history skips further affected discoveries instead of resetting them. It does not modify Valheim save data. Abrupt termination before a pending save completes can lose the most recent ledger update. Deleting this history resets tracking. Client histories are not a server anti-cheat mechanism.

## Special Enemies

All settings belong to the host. Special events use the default Discord route. No maintained enemy list or assumed miniboss flag is required.

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Capture explicitly selected ordinary-enemy kill credits; empty EnemyKeys selects none. |
| EnemyKeys | empty | Exact case-sensitive confirmed kill-credit/stat keys, comma/semicolon/newline separated. At most 64 keys, 128 characters each, 4096 total. No wildcards or prefab-name matching. |
| FirstKillOnly | false | Require the character's first saved kill of that enemy across all worlds. Independent of the per-world discovery ledger. |
| PostEventSeconds | 4 | Seconds after the credited kill, 0-30. Uses ordinary-loot display settings and metadata wait. |
| CooldownSeconds | 60 | Seconds between accepted captures of the same key, 0-3600. Resets on session or selection changes. |
| Message | ⚔ {enemy} defeated! | Supports {enemy}, {player}, {loot}, {item_count}; confirmed recording-character credit appends. |
| LogEnemyKeys | false | Advanced diagnostic: log actual confirmed ordinary-enemy keys on recording clients. Copy the logged key into EnemyKeys; disable afterward. |

Accepted special captures replace the ordinary loot capture for that credited death and bypass ordinary-loot rarity. When a special capture is ineligible, ordinary loot may still qualify normally. Normal bosses remain exclusive to Boss Kill rules. Special captions name the confirmed recording character; they do not claim a full attacker roster or final blow. Clients cannot override the host selection. Unknown identifiers simply do not match.

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
