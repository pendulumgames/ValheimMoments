# Valheim Moments

[Source code and issue reports](https://github.com/PendulumGames/ValheimMoments)

Save the moments worth sharing: boss victories, rare drops, unfortunate deaths,
and anything you catch with a hotkey. Valheim Moments turns recent gameplay into
animated WebP clips and can send them to Discord.

**Windows x64 beta - 0.11.1.** Manual capture, deaths, boss summaries, Epic Loot,
ordinary loot and natural acquisitions have passed user testing. Host and joining-player
F10 clips have reached Discord, and the WebP clips were confirmed visually.

This release fixes boss **Kill Credit** to include every player Valheim credited for
the fight. **Recorded by** identifies the clip's recorder; **Final Blow** identifies
the finishing player when known. Install **0.11.1 on the host and every recording
client** for the complete metadata. The new roster has automated coverage; its final
live co-op check remains pending. Dedicated hosting, detailed loot edge cases,
settings UI/sync and sustained performance checks remain documented test items.

## Features

* **F10** captures recent gameplay. **F9** pauses/resumes recording.
* Automatic player-death clips, including the recorded cause when available.
* Boss clips with first-kill rules, all credited players, final-blow attribution and loot.
* Optional Epic Loot rarity, modifiers, sockets and unidentified-item display.
* Ordinary-enemy loot plus tracked natural chest/world pickups, using a shared minimum rarity.
* Independent boss and ordinary-loot display options, with Markdown headings and bullets.
* Host-owned Discord channel routing and bot name. Clients relay footage to the host.
* Local WebP files remain available when delivery fails.

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
UploadClips = true
SaveLocalCopy = false
EnableClientRelay = true
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
ignored in multiplayer. `EnableClientRelay = false` on a client prevents it sending
clips; on the host it disables incoming clip delivery. No host webhook credentials
are sent to clients. Every post includes **Recorded by:** with the recording character's
name. For joining players, the host takes this name from their connection; host and
solo clips use the local character's name captured when the event triggers.
Client event captions/rarity decisions are not independently verified by the host.

The host owns automatic triggers, rarity/first-kill rules, timing and post formatting.
These settings are read-only on clients in Configuration Manager and follow the host
in memory. Clients keep hotkeys, capture/relay opt-out, performance, image flip,
local-copy retention and Advanced timing diagnostics editable. Their config files
retain preferences for single-player. Client capture waits for host settings; older
clients without the settings exchange cannot relay clips.

## Boss and loot settings

The [complete configuration reference](https://github.com/PendulumGames/ValheimMoments/blob/main/docs/CONFIGURATION.md)
lists all settings, defaults, templates and host/client ownership.

`[Boss Kill]` includes `FirstKillOnly`, `FirstKillBypassesRarity`,
`OnlyCaptureIfLootMeetsRarity`, `MinimumLootRarity`, and `PlayerNameMode`
(`KillCredit`, `FinalBlow`, or `Both`). First kill means the character's first recorded
kill in Valheim's saved statistics, not its first uploaded clip.

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

* Relay clips are limited to 10 MiB, also subject to the host's lower upload limit.
* Relay transfers are paced and take time before Discord upload. The host accepts one
  incoming transfer/upload at a time. Busy events are declined without automatic retry.
* Successfully uploaded clips are deleted by default, including client originals after
  host confirmation. Set SaveLocalCopy=true on the recording player to keep them.
  Failed/skipped clips remain local. Host relay temp files are removed after delivery
  or failure; a process crash can leave a `RelayTemp` file behind.
* Co-op perspectives are separate submissions, with no encounter-wide deduplication.
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

* First biome and notable location/trader discoveries, detected from game discovery events.
* Selected achievement unlocks, driven by the game's achievement definitions rather than a hardcoded list.
* Miniboss/special encounter clips: investigate game progression markers and configurable identifiers. No universal miniboss flag has been verified, so automatic coverage of every future enemy is not promised.
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
