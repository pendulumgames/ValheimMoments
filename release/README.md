# Valheim Moments

[Source code and issue reports](https://github.com/PendulumGames/ValheimMoments)

Save the moments worth sharing: boss victories, rare drops, unfortunate deaths,
and anything you catch with a hotkey. Valheim Moments turns recent gameplay into
animated WebP clips and can send them to Discord.

**Windows x64 beta.** Manual capture, deaths, boss summaries and loot highlights have
been tested in game. Host and joining-player F10 clips have reached Discord in a live
co-op test, with the smaller WebP-only encoder's clips confirmed visually.
Dedicated-server testing remains pending.

## Features

* **F10** captures recent gameplay. **F9** pauses/resumes recording.
* Automatic player-death clips, including the recorded cause when available.
* Boss clips with first-kill rules, kill credit, final-blow attribution and loot.
* Optional Epic Loot rarity, modifiers, sockets and unidentified-item display.
* Ordinary-enemy loot highlights with a configurable minimum rarity.
* Independent boss and ordinary-loot display options, with Markdown headings and bullets.
* Host-owned Discord channel routing and bot name. Clients relay footage to the host.
* Local WebP files remain available when delivery fails.

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

## Discord setup — host or single-player

Launch once, exit, then edit `BepInEx/config/local.valheimmoments.cfg` locally:

```ini
[Discord]
Enabled = true
WebhookURL = YOUR_DISCORD_WEBHOOK_URL
Username = Valheim Moments
UploadClips = true
SaveLocalCopy = true
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

## Boss and loot settings

The [complete configuration reference](https://github.com/PendulumGames/ValheimMoments/blob/main/docs/CONFIGURATION.md)
lists all settings, defaults, templates and host/client ownership.

`[Boss Kill]` includes `FirstKillOnly`, `FirstKillBypassesRarity`,
`OnlyCaptureIfLootMeetsRarity`, `MinimumLootRarity`, and `PlayerNameMode`
(`KillCredit`, `FinalBlow`, or `Both`). First kill means the character's first recorded
kill in Valheim's saved statistics, not its first uploaded clip.

`TrackPeriodicDamage = true` lets the boss owner track Spirit/fire/poison effects for
final-blow attribution. A tick from one known player can supply their name; mixed or
unknown sources stay unavailable. The creature owner needs 0.9.2 with this option
enabled. Restart after changing it. This addition has automated coverage and awaits
live periodic-kill testing.

`[Loot Capture] MinimumRarity` defaults to `Legendary`. `None` is useful for testing
any observed drop. Bosses exclusively use boss rules and do not produce a second
ordinary-loot event. Ordinary-loot highlights require Epic Loot's optional adapter;
item pickups, crafting and console-spawned items do not trigger them.

Both sections have `MaxLootItemsShown`, `ShowQuantity`, `ShowRarity`, `ShowItemModifiers`,
`ShowItemSockets`, `ShowUnidentifiedStatus`, and `LootHeader`. Highest rarity sorts first.
Unidentified modifiers and sockets stay hidden. Restart after editing configuration.

## Current limits

* Relay clips are limited to 10 MiB, also subject to the host's lower upload limit.
* Relay transfers are paced and take time before Discord upload. The host accepts one
  incoming transfer/upload at a time. Busy events are declined without automatic retry.
* Client originals are always retained. Host relay temp files are removed after delivery
  or failure; a process crash can leave a `RelayTemp` file behind.
* Co-op perspectives are separate submissions, with no encounter-wide deduplication.
* Long ragdoll delays can put the killing blow outside an ordinary-loot clip's history.
* Periodic damage can leave final-blow attribution unavailable. No guessed player is shown.
* Discord may reject files according to its current limits; failed uploads retain footage.
* Linux/macOS/Steam Deck recording are not supported by this Windows package.

## Credits

Valheim Moments' original code is licensed under MIT, copyright 2026 Pendulum.
Significant portions of the code and the icon were created using AI tools.
The Thunderstore listing uses the **AI Generated** category.

Uses Imazen.WebP and libwebp. Their license and attribution files are included in
the plugin's `Licenses` folder. Game, Unity, BepInEx, Harmony and Epic Loot binaries
are not redistributed in this package. Icon created with AI-assisted image generation.
This is an unofficial community mod, not affiliated with Iron Gate or Coffee Stain.
