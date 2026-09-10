# Configuration reference

Applies to Valheim Moments 0.9.5. Launch once to generate
`BepInEx/config/local.valheimmoments.cfg`, then close Valheim before editing it.
Restart after changes. Defaults describe a new installation; upgrades preserve
existing settings. Never share a config containing webhook URLs.

## Capture

These settings belong to each recording player. Dedicated servers relay clips but
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
| PreEventSeconds | 5 | Rolling history duration; must be greater than zero. |
| PostEventSeconds | 2 | Post-trigger duration for manual clips and deaths; zero or more. |
| FPS | 15 | Sampling rate, 1–30. Missed samples are skipped. |
| Width | 640 | Output width, 16–1920 pixels. |
| Height | 360 | Output height, 16–1080 pixels. Screen stretches to this aspect ratio. |
| WebPQuality | 80 | Lossy quality, 1–100. Higher quality can increase file size. |
| MemoryBudgetMiB | 192 | Managed frame-pool budget, 16–512 MiB; excludes GPU and encoder memory. |
| FlipVertically | false | Enable only if recordings appear upside down. |

Bosses and ordinary loot use their own PostEventSeconds. Allocation uses the largest
post-event duration, even if that trigger is disabled. History plus that duration
must not exceed 60 seconds. The raw clip must fit 256 MiB and the whole frame pool
must fit MemoryBudgetMiB. Invalid combinations disable capture and produce a log message.

Let P = ceil(PreEventSeconds × FPS) and Q = ceil(maximum post-event seconds × FPS).
The frame pool occupies `Width × Height × 4 × (2P + Q + 1)` bytes: about 185.4 MiB
at defaults. Increasing resolution, duration or FPS can substantially increase this.

## Discord

In single-player these settings belong to the local player. In multiplayer, the host
owns delivery, destinations and the bot name. Joining players' local webhook,
Enabled and Username cannot override the host. Both sides need the mod and
EnableClientRelay enabled for client delivery.

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | false | Host/solo Discord delivery switch. |
| UploadClips | true | Host/solo upload switch; also gates incoming relay delivery. |
| EnableClientRelay | true | Host accepts client clips; client allows its own clips to be sent. |
| WebhookURL | empty | Secret default destination, including manual clips. |
| Username | Valheim Moments | Host-selected bot display name, 1–80 characters. |
| SaveLocalCopy | false | Delete after successful Discord upload or host relay confirmation. Set true to keep clips. Applies locally to each recording player; failed/skipped uploads remain local. |
| MaxUploadMiB | 10 | Local guard, clamped to 1–100 MiB. Relay limit is 10 MiB or the host's lower limit. Discord may reject a smaller file. |
| UseBossKillWebhook | false | Enable separate boss destination. |
| BossKillWebhookURL | empty | Secret boss destination. |
| UseGoodLootWebhook | false | Enable separate ordinary-loot destination. |
| GoodLootWebhookURL | empty | Secret ordinary-loot destination. |
| UsePlayerDeathWebhook | false | Enable separate death destination. |
| PlayerDeathWebhookURL | empty | Secret death destination. |

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
| LootDrop | true | Allow ordinary-loot captures; also requires Loot Capture.Enabled and the Epic Loot adapter. |

The host also applies these switches and the corresponding event Enabled setting to
incoming clips. Rarity decisions and capture settings remain local to recording
players. The host does not independently verify client footage or loot claims.

## Player Death

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Enable this event. |
| Message | 💀 {player} died! | Supports {player} and {cause}. |
| IncludeCause | true | Include recorded attacker/environmental cause; append if {cause} is absent. |
| IncludePlayerName | true | Use character name/override for {player}; otherwise “A player”. |
| PlayerNameOverride | empty | Optional replacement name for the death message. |

Unresolved causes remain unknown. Periodic damage can lack an attacker even when the
original weapon belonged to a player.

## Boss Kill

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Enable credited boss captures. |
| FirstKillOnly | false | Exclude every repeat kill for this character. |
| FirstKillBypassesRarity | true | Keep this character's first kill regardless of rarity. |
| OnlyCaptureIfLootMeetsRarity | false | Require an observed item meeting MinimumLootRarity, except the first-kill bypass. |
| MinimumLootRarity | Legendary | None accepts all; otherwise a verified Epic Loot rarity. |
| Message | 🏆 {boss} defeated! | Supports {boss}, {player}, {credit}, {killer}, {loot}, {item_count}. |
| PlayerNameMode | Both | KillCredit, FinalBlow or Both; selected names append if omitted. |
| TrackPeriodicDamage | true | Track actual boss Spirit/fire/poison sources; restart required. Must be enabled on the creature owner. |
| PostEventSeconds | 4 | Recording time after the credited kill. |
| LootWaitSeconds | 12 | Metadata wait, clamped to 0–25 seconds; does not extend recording. |
| ShowLoot | true | Include loot in the post; does not change rarity eligibility. |

First kill comes from the character's saved Valheim statistics, including kills before
this mod was installed. It is not per world or per successful upload. FirstKillOnly
takes precedence over every repeat-kill rarity outcome.

In Both mode, {player} means kill credit; in FinalBlow mode it means final blow.
Credit identifies the recording character whom Valheim credited. Final blow is the
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

## Loot Capture

| Key | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Capture qualifying ordinary-creature drops credited to this player; requires Epic Loot adapter. |
| MinimumRarity | Legendary | Minimum observed rarity; None accepts any actual observed item, including vanilla drops. |
| Message | Great loot from {enemy}! | Supports {enemy}, {player}, {loot}, {item_count}; credit and loot append if omitted. |
| PostEventSeconds | 4 | Recording time after qualifying loot is observed. |
| LootWaitSeconds | 12 | Metadata wait after credit, clamped to 0–25 seconds. |

Bosses exclusively use Boss Kill rules. Pickups, crafting and console-spawned items
are not triggers. A long ragdoll delay can put the kill outside the rolling history;
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

| Key | Default | Meaning |
| --- | --- | --- |
| LogCaptureTiming | true | Aggregate capture timing, readback latency and frame counts every ten seconds. |

Event, encoder, upload and attribution diagnostics also use the BepInEx log.
