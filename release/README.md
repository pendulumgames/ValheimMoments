# Valheim Moments

[Source code and issue reports](https://github.com/PendulumGames/ValheimMoments)

Save the moments worth sharing: boss victories, rare drops, unfortunate deaths,
and anything you catch with a hotkey. Valheim Moments turns recent gameplay into
animated WebP clips and can send them to Discord.

**Windows x64 beta - 0.15.0.** Manual capture, deaths, boss summaries, Epic Loot,
ordinary loot and natural acquisitions have passed user testing. Host and joining-player
F10 clips have reached Discord, and the WebP clips were confirmed visually.

This release adds grouped multiplayer highlights: compatible creature-death recordings share one Discord post with up to three labeled perspectives. Discoveries and configurable special-enemy captures from 0.14 are included. Settings, sizing, notifications and death-rate improvements from 0.12/0.13 are included. Install **0.15.0 on the host and every recording client**. Automated checks cover persistence, rules and hook behavior; in-game exploration, visual/audio and live co-op acceptance remain pending.

## Features

* **F10** captures recent gameplay. **F9** pauses/resumes recording.
* Automatic player-death clips with cause, configurable rate limit, intervening-death summary and optional cheeky captions.
* Brief Recording Memory / Memory Saved / Memory Uploaded feedback, with player-controlled quiet sound.
* Boss clips with first-kill rules, all credited players, final-blow attribution and loot.
* First tracked biome, labeled-location and trader discoveries per character per world.
* Host-configured special-enemy clips with exact kill-credit keys and per-enemy cooldowns.
* Optional Epic Loot rarity, modifiers, sockets and unidentified-item display.
* Ordinary-enemy loot plus tracked natural chest/world pickups, using a shared minimum rarity.
* Independent boss and ordinary-loot display options, with Markdown headings and bullets.
* Host-owned Discord channel routing and bot name. Clients relay footage to the host.
* Local WebP files remain available when delivery fails.

## Discoveries and special enemies

Discoveries default on. Physically entering a new biome, a location with a game discovery label, or a trader's greeting range can capture a moment. Nearby discoveries group briefly into one caption. The host can disable categories and set the cooldown; `Discord.DiscoveryWebhookURL` uses the main webhook when blank.

History lives in `ValheimMoments/State/Discoveries` beside the plugin, separately for each character/world pair. Keep this folder when updating. It remembers observations even while recording is paused, disabled or busy, so those visits are not replayed later. Initial loading/warmup is silent. Valheim's existing character history cannot reliably reconstruct exploration in each world: an older destination can count as a first **tracked** visit after installing this version. Revealed map pins alone do not count. Not every dungeon has a discovery label, and unsupported mod sources are skipped.

Special enemies default to an empty selection. The host enables the Advanced `Special Enemies.LogEnemyKeys` diagnostic, kills a candidate, then copies its exact confirmed stat key from the BepInEx log into `Special Enemies.EnemyKeys`. Use commas or semicolons for multiple keys. These are case-sensitive kill-credit identifiers, not prefab names or translated display names; no wildcards are used. Turn the diagnostic off afterward.

Special captures use the main Discord route, do not require rare loot, and show the recording character's confirmed kill credit plus observed loot. Their `FirstKillOnly` option uses existing character-wide kill statistics across worlds. Normal bosses always use Boss Kill rules; an accepted special capture replaces the ordinary loot capture for that credited death. Matching shared death IDs allow these perspectives to join the multiplayer director.

## Natural treasure highlights

The host can toggle `Loot Capture.CaptureChestPickups` and `CaptureWorldPickups`
independently; both default on. MinimumRarity still defaults to Legendary. Use None
for vanilla items or broad testing. Pickup posts identify **Collected by:** and the
recorder, and use the Good Loot Discord route.

Only loot tagged during a verified natural generation path qualifies, on successful
acquisition. Opening a chest alone does not record. Player storage, player drops,
gravestones, crafting and deposits into dungeon chests do not qualify. Older untagged
contents and unsupported mod sources are skipped. Mixing stacks can remove their
eligibility; splitting or picking up a leftover stack does not generate repeat clips.
Quick transfers such as Take all are grouped for 0.25 seconds, with at most 64 entries.

Provenance is small item custom data saved through Valheim's normal world/item save
paths. It is consumed even if recording is disabled or busy, so old loot cannot be
replayed later. Mod removal leaves harmless custom data on unclaimed tracked items.
See the [natural loot test checklist](https://github.com/PendulumGames/ValheimMoments/blob/main/docs/NATURAL-LOOT-TEST.md).

## Install

Install using Thunderstore Mod Manager or r2modman, then launch **Modded**.
BepInEx is installed as a dependency. **Epic Loot is optional**, tested with version
0.14.2, and is not automatically installed by this package.

For multiplayer relay, install this version on the host and every recording client.
The host needs the mod even if it does not record footage. Windows headless servers
run relay delivery without allocating a graphics capture buffer.

Version 0.9.0 renames the plugin, helper and config identifier. Before upgrading from
an earlier build, close Valheim and move the previous plugin folder out of the active
profile. Keep its `Clips` folder and copy the previous mod config to
`BepInEx/config/local.valheimmoments.cfg` before launching. Do not overwrite an existing
new config without comparing your settings. Installing both plugin identities can
cause duplicate recording and uploads. The new plugin folder is `ValheimMoments`.

## First capture

1. Enter a world and wait at least five seconds.
2. Press **F10**.
3. Wait for encoding to finish. Find the result in this mod's `Clips` folder under
   the profile's `BepInEx/plugins` directory.

Defaults are **640×360, 15 FPS, five seconds before the event**, and two seconds after
manual/death events. Boss and ordinary-loot clips default to four seconds after.
WebP animations have **no audio**. They are not MP4 videos.

Recording uses a bounded frame buffer (roughly 185 MiB at defaults) and a separate
background encoder. Only one clip collects/encodes at a time; extra triggers are skipped.
Actual performance depends on your hardware and settings.

Resolution is clamped to 480–1920 wide and 270–1080 high, FPS to 1–30 and quality to
1–100. Aspect ratios stay between 1:2 and 3:1. Effective resolution reduces to a floor of 480 x 270, then FPS reduces when needed
to fit the memory budget (48 MiB minimum); the log reports actual settings.

Changing sessions clears the rolling buffer and cancels a clip still collecting.
Wait five seconds after entering the new world for a full history. An already running
encoder can finish its local file, but that old clip cannot upload through the new session.

## Discord setup — host or single-player

Launch once, exit, then edit `BepInEx/config/local.valheimmoments.cfg` locally:

```ini
[Discord]
Enabled = true
WebhookURL = YOUR_DISCORD_WEBHOOK_URL
Username = Valheim Moments

[Capture]
SaveLocalCopy = false
SizePreset = Small
```

Create the webhook in the destination Discord server's integrations settings. Treat
its URL as a secret. Never share a configured file, profile export containing it, or
a screenshot of the URL. No webhook is included with this download.

To use separate destinations, enable the matching host option and enter its URL:

```ini
UseGoodLootWebhook = false
GoodLootWebhookURL =
UseBossKillWebhook = false
BossKillWebhookURL =
UsePlayerDeathWebhook = false
PlayerDeathWebhookURL =
```

When an override is off, that event uses the default webhook. An enabled but invalid
override retains the clip locally. Manual clips always use the default destination.
The host's `Username` is used for every destination.

**Joining clients need no webhook.** Their local webhook and bot-name settings are
ignored in multiplayer. The host Discord.Enabled setting controls uploads and relay. No host webhook credentials or Username are sent to clients. Every post includes **Recorded by:** with the recording character's
name. For joining players, the host takes this name from their connection; host and
solo clips use the local character's name captured when the event triggers.
Client event captions/rarity decisions are not independently verified by the host.

The host owns automatic triggers, rarity/first-kill rules, timing and post formatting.
These settings are read-only on clients in Configuration Manager and follow the host
in memory. Clients keep hotkeys, capture opt-out, performance, image flip,
local-copy retention and Advanced timing diagnostics editable. Their config files
retain preferences for single-player. Client capture waits for host settings; older
clients without the settings exchange cannot relay clips.

## Capture sizes and saving

Choose Tiny, Small, Medium, Balanced, Large or Ultra for an aspect-aware size, or Custom to edit Width and Height directly below the picker. At 16:9 these correspond to approximately 480x270, 640x360, 854x480, 960x540, 1280x720 and 1920x1080. Memory limits may reduce effective dimensions/FPS. Mismatched canvases are padded instead of stretched.

Capture.SaveLocalCopy=true also works with host Discord disabled. With both off, new clip triggers are suppressed. With Discord on and saving off, confirmed uploads are deleted; failed uploads still remain for recovery. The planned gallery, Keep key and bounded failure expiry are not in this milestone.

Discord upload allowance depends on the destination/server boost level. Personal Nitro does not establish the webhook allowance. This release retains its conservative 10 MiB client relay cap; check actual file sizes. Motion and detail make resolution/FPS/quality estimates uncertain.

## Death limits and notifications

Player Death.CaptureLimit defaults to 1 per 60-second WindowSeconds. Further confirmed deaths are counted for the next eligible death post; failed delivery does not erase the count. Each player has a separate quota, and counts reset with the session. Custom death templates opt into cheeky text using {flavor}; {extra_deaths} places the additional-death count.

Notifications are player-owned: Enabled, SoundMode (Off / OnCapture / OnCompletion / Both), and Volume. Memory Uploaded appears only after Discord accepts the clip, including a final host acknowledgement for joining players. Memory Captured means encoding finished. Local-only recording says Memory Saved. The banner may appear in footage; the WebP itself remains silent.

## Boss and loot settings

The [complete configuration reference](https://github.com/PendulumGames/ValheimMoments/blob/main/docs/CONFIGURATION.md)
lists all settings, defaults, templates and host/client ownership.

`[Boss Kill]` includes `CaptureMode`, `MinimumLootRarity`, and `PlayerNameMode`
(`KillCredit`, `FinalBlow`, or `Both`). First kill means the character's first recorded
kill in Valheim's saved statistics, not its first uploaded clip. FirstKillThenRarity always captures that first kill and filters later kills by rarity. Existing rules migrate; new configs default to this mode.

Kill Credit follows Valheim's credited-attacker records; simply being nearby does
not add someone. Names are sorted, and the list is bounded for Discord. If complete
metadata is missing, the confirmed recording character is shown with "full list
unavailable" rather than claiming that player was the only contributor. The
{credit} placeholder (and {player} in Both/KillCredit mode) uses this list. First-kill
history still belongs to the recording character. Ordinary-kill loot messages retain
their individual credited character; natural pickup messages use Collected by.

`TrackPeriodicDamage = true` lets the boss owner track Spirit/fire/poison effects for
final-blow attribution. A tick from one known player can supply their name; mixed or
unknown sources stay unavailable. The creature owner follows the host's synced option.
Spirit-damage final-blow attribution passed a
live boss-kill test; fire/poison and additional co-op cases retain automated coverage.

`[Loot Capture] MinimumRarity` defaults to `Legendary`. `None` is useful for testing
any observed drop. Bosses exclusively use boss rules and do not produce a second
ordinary-loot or pickup event. Named rarity thresholds require Epic Loot's optional
adapter; None also supports vanilla loot. Verified natural pickups use the separate
PickupMessage. Crafting and untracked console-spawned items do not trigger highlights.

Both sections have `MaxLootItemsShown`, `ShowQuantity`, `ShowRarity`, `ShowItemModifiers`,
`ShowItemSockets`, `ShowUnidentifiedStatus`, and `LootHeader`. Highest rarity sorts first.
Unidentified modifiers and sockets stay hidden. Restart after editing the config file;
Configuration Manager edits apply in game, with buffer changes waiting for active work.

## Current limits

The host's **Director** section defaults to Enabled=true, MaxPerspectives=3, MaxPostMiB=20 and CollectionSeconds=10. It reserves space before requesting selected footage. Host footage is primary when available; remaining selection follows connected-peer order. The main caption/loot summary comes from the primary perspective, with recorder labels and personal first-kill status listed separately. These are host-owned settings; each player keeps control of SaveLocalCopy.

The director also applies the 10 MiB per-file cap to host footage. Oversized, late or excess perspectives are kept locally and omitted. No automatic quality reduction or server-tier detection is claimed. Discord can still reject a post below the configured guard. Unconfirmed delivery retains originals and asks you to check Discord before retrying.

* Relay clips are limited to 10 MiB, also subject to the host's lower upload limit.
* Relay transfers are paced and take time before Discord upload. The host accepts one
  incoming transfer and one grouped upload at a time. The director reserves bounded queue capacity; excess/expired offers are omitted without automatic retry.
* Successfully uploaded clips are deleted by default, including client originals after
  host confirmation. Set SaveLocalCopy=true on the recording player to keep them.
  Failed/skipped clips remain local. Host relay temp files are removed after delivery
  or failure; a process crash can leave a `RelayTemp` file behind.
* Compatible creature-death perspectives share a post when offered within the host collection window. Missing IDs, manual captures and personal discovery/death events remain separate.
* Long ragdoll delays can put the killing blow outside an ordinary-loot clip's history.
* Periodic damage can leave final-blow attribution unavailable. No guessed player is shown.
* Discord may reject files according to its current limits; failed uploads retain footage.
* Linux/macOS/Steam Deck recording are not supported by this Windows package.

## Future Roadmap

These are future ideas, not features included in this release. Prioritize game-provided
events and saved progression data so new content needs as little mod maintenance as
possible. Host configuration should control categories, filters, first-time rules and
cooldowns; avoid needing a new build just to add an enemy or achievement identifier.
Game API changes can still require compatibility updates.

* Extend discovery coverage where additional reliable game events are available.
* Selected achievement unlocks, driven by the game's achievement definitions rather than a hardcoded list.
* Director fallback encoding and larger transfer budgets, survival-validated close calls, and a personal gallery with Keep and bounded retries.
* Optional raid start/completion highlights, with per-event controls and cooldowns.
* Group overlapping progression events to avoid duplicate posts for the same moment.

## Credits

Valheim Moments' original code is licensed under MIT, copyright 2026 Pendulum.
Significant portions of the code and the icon were created using AI tools.
The Thunderstore listing uses the **AI Generated** category.

Uses Imazen.WebP and libwebp. Their license and attribution files are included in
the plugin's `Licenses` folder. Game, Unity, BepInEx, Harmony and Epic Loot binaries
are not redistributed in this package. Icon created with AI-assisted image generation.
This is an unofficial community mod, not affiliated with Iron Gate or Coffee Stain.
