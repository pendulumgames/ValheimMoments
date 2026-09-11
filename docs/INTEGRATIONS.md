# Integration and validation notes

This describes 0.13.0, not a guarantee of compatibility with future versions.
Observers do not intentionally change damage, kill credit, rolls or saved statistics.

## Harmony patch inventory

| Target | Observer | Purpose |
| --- | --- | --- |
| Player.OnDeath() | PlayerDeathDetector prefix/postfix | Snapshot local alive state/cause; emit only after the original makes that local player dead. |
| Game.RPC_RegisterKill(long, string, int, int, int, bool) | BossKillDetector prefix/postfix | Compare profile kill counts before/after actual credit. Boss number selects boss or ordinary-loot rules. |
| Character.OnDeath() | BossAttribution prefix/finalizer | Snapshot owner-observed final blow and the same credited-player attacker flags vanilla checks; restore nested context on exceptions. |
| Game.RegisterKill(long, string, int, KillModifiers, int, bool) | BossAttribution prefix | Send separate final-blow and credit-roster metadata to the same recipient before vanilla credit. |
| ZRoutedRpc constructors | BossAttribution postfix | Register attribution channel for each router. |
| Character.OnDeath() | BossLootDetector prefix/finalizer | Scope owner-observed loot, publish its revision, restore context. |
| CharacterDrop.GenerateDropList() | BossLootDetector last-priority postfix | Read the returned roll for the exact character; never reroll it. |
| Game.RegisterKill(long, string, int, KillModifiers, int, bool) | BossLootDetector prefix | Announce the exact loot ticket to credited recipients. |
| ZRoutedRpc constructors | BossLootDetector postfix | Register ticket/result channels and clear stale inbox entries. |
| Ragdoll.Setup | BossLootDetector prefix, Epic Loot enabled | Associate the exact ragdoll with the current character's pending snapshot. |
| Ragdoll.SpawnLoot | BossLootDetector prefix/finalizer, Epic Loot enabled | Restore snapshot during delayed spawning, publish completion, clear association. |
| EpicLoot.LootRoller.RollLootTableAndSpawnObjects, two list-returning overloads | EpicLootAdapter postfix | Read completed spawned items inside the correlated death/ragdoll context. |
| Character.RPC_Damage(long, HitData) | PeriodicAttribution prefix/finalizer | Scope the actual incoming source and victim while status damage is applied; restore nested context. |
| SE_Burning.AddFireDamage(float), AddSpiritDamage(float) | PeriodicAttribution prefix/postfix | Observe accepted additions to the relevant damage pool; retain a name only for one known player ID. |
| SE_Poison.AddDamage(float) | PeriodicAttribution prefix/postfix | Observe equal/stronger replacements; rejected weaker applications do not change the source. |
| SE_Burning.UpdateStatusEffect(float), SE_Poison.UpdateStatusEffect(float) | PeriodicAttribution prefix/finalizer | Scope the exact ticking effect and restore context even on exceptions. |
| Character.ApplyDamage(HitData, bool, bool, HitData.DamageModifier) | PeriodicAttribution prefix | Associate an attackerless tick with its known source by HitData object identity, without changing HitData. |

Source: [death](../src/ValheimMoments/PlayerDeathDetector.cs),
[credit](../src/ValheimMoments/BossKillDetector.cs),
[attribution](../src/ValheimMoments/BossAttribution.cs),
[loot](../src/ValheimMoments/BossLootDetector.cs),
[Epic Loot](../src/ValheimMoments/EpicLootAdapter.cs).
For kill events, supplemental metadata alone cannot trigger capture: actual local game credit is
required. These routed channels are distinct from the direct-peer clip relay.

[PeriodicAttribution](../src/ValheimMoments/PeriodicAttribution.cs) uses weak tables
for effect pools and exact tick objects. Fire and Spirit pools keep a source only
while all observed contributions share a known player network ID. Existing unknown
damage cannot be claimed by a later hit. Drained pools can start fresh. Poison follows
the inspected replacement rule. Non-owner ticks invalidate local effect provenance.
Patches are optional through TrackPeriodicDamage and removed if installation fails.
The existing boss-owner metadata channel shares the resolved final blow with credited
players. The read-only [inspection script](../tools/Inspect-PeriodicDamage.ps1) exposes
the installed IL used to verify these paths.

## Boss credit roster

The owner reads `Character.m_nview.GetZDO()` and the connected player list, testing
`ZDOVars.s_attackers.ToString() + player.m_name` with GetBool, exactly as the inspected
Character.OnDeath credit loop does. It does not infer contribution from distance or
invent its own damage threshold. This also inherits vanilla's name-based attacker
identity semantics. Disconnected players absent from the game's credit loop are not
added. The roster is sorted/deduplicated, names sanitized and limited to 80 characters,
with a 1,024-character payload limit and an explicit count if names are omitted.

A separate KillCredits_v1 routed channel precedes vanilla credit on the same connection.
The bounded inbox is sender/enemy scoped, consumed once, and expires after five seconds.
Only an actual increment in the recipient's profile kill statistic triggers a clip.
The local owner reads the scoped snapshot directly. Missing metadata identifies the
confirmed local credit but labels the complete list unavailable. First-kill statistics,
recorder identity and final blow remain independent. Ordinary-loot credit is unchanged.
Attribution hooks initialize before the headless graphics exit, so dedicated owners can
supply the roster without allocating capture buffers. Live dedicated acceptance remains.

## Epic Loot data

The adapter was checked against installed Epic Loot 0.14.2. It has no compile-time
Epic Loot type dependency and reads the installed rarity enum. API calls obtain
display name, rarity index/name/color and unidentified state. GetMagicItem supplies
Effects, SocketCount and Rarity; GetEffectText supplies actual modifier descriptions.

Unity rich-text tags are removed and field lengths bounded. Unidentified items'
modifiers and sockets are never read for display. Completed items are deduplicated
by object identity across spawn overloads. Snapshot revisions let delayed ragdoll
results update the exact credited kill instead of another enemy.

Without the adapter, manual/death/boss captures and vanilla boss loot remain available.
Ordinary and natural-loot highlights also work without it when MinimumRarity=None. Unknown rarity cannot satisfy a
named threshold. Integration errors are logged without deliberately changing game outcomes.

## Natural loot provenance

[WorldLootDetector](../src/ValheimMoments/WorldLootDetector.cs) tags items only during
owner-observed natural generation. It does not reroll, change inventory capacity,
change stacking rules or alter item stats. It writes/removes only its
`local.valheimmoments.origin.v1` custom-data key, using Valheim's existing item clone,
inventory save/load and ground-item ZDO serialization. The marker has a source category
and random identifier. Unknown provenance fails closed; it is not an anti-cheat signature.

| Target | Observer | Purpose |
| --- | --- | --- |
| Container.AddDefaultItems | prefix/finalizer | Scope the exact natural container inventory during vanilla generation. |
| EpicLoot.PendingChestLoot.RollInternal(Container, string, List<LootTable>) | prefix/finalizer | Track 0.14.2 deferred chest rolls, including rolls made when approached/opened. |
| Container.Load | prefix | Associate the actual inventory with its container before loading. |
| Inventory.Load overloads | prefix/finalizer | Preserve provenance only for natural containers; player/unknown loads cannot trigger. |
| Inventory.AddItem overloads accepting ItemData | prefix/finalizer | Mark exact generated items or consume provenance before clones/saves; compare destination quantities for actual successful additions, including partial failures. |
| Inventory.MoveAll / MoveItemToThis overloads | prefix/finalizer | Verify the source is a natural container holding the item. |
| Humanoid.Pickup(GameObject, bool, bool) | prefix/finalizer | Verify the exact ground item behind a successful player inventory addition. |
| Pickable.RPC_Pick / PickableItem.RPC_Pick / DropOnDestroyed.OnDestroyed | prefix/finalizer | Scope verified owner-controlled natural world drops; exclude player pieces, plants, containers and characters. |
| ItemDrop.OnCreateNew(ItemDrop, bool) | last postfix | Tag/save a newly created ground item inside that scope only. |
| ItemDrop.OnPlayerDrop | prefix | Remove/save provenance on player drops. |
| ItemDrop.AutoStackItems | prefix/finalizer | Invalidate/save ground provenance if stack quantity changes or the operation throws. |
| Container.OnDestroyed | prefix/finalizer | Strip tags before player/gravestone storage spills; preserve proven natural contents. |
| Character.OnDeath / Ragdoll.SpawnLoot | prefix/finalizer | Suppress world-generation context around nested mob drops to avoid second pickup highlights. |

The read-only [inspection script](../tools/Inspect-WorldLoot.ps1) reports the installed
signatures and optionally IL, including item custom-data serialization and deferred
Epic chest generation. All scopes restore on exceptions. Failed zero-add transfers restore the source marker;
positive partial additions consume it and save ground state even if vanilla returns
false. Existing natural destination stacks lose provenance when a deposit could merge
with them. A whole stack opportunity is consumed on its first acquisition, including
when recording is off or busy; no replays of leftovers or subsequent storage transfers.
Container associations use weak tables. Nothing scans the scene or allocates capture
frames for unqualified loot. At most two acquisition batches (chest/world), each capped
at 64 items, wait 0.25 seconds to group Take all before rarity filtering.

The source owner must run this version to tag generation. Already generated untagged
contents, arbitrary mod inventory injection, unsupported generation methods and mixed
stacks are deliberately skipped. Live persistence, co-op and Epic chest acceptance are
tracked in [natural loot checks](NATURAL-LOOT-TEST.md). Headless hosts install provenance
hooks even though their graphics capture remains disabled.

## Capture and encoding

[Plugin](../src/ValheimMoments/Plugin.cs) samples at end of frame, captures into a
RenderTexture, downsamples on the GPU, and requests asynchronous RGBA readback with
three reusable slots. Unity operations and buffer ownership stay on the game thread.
Timestamps represent submission time. Focus loss/missed samples can reduce frame count;
animation timing uses recorded timestamps.

[CaptureBuffer](../src/ValheimMoments.Core/CaptureBuffer.cs) preallocates its pixel
pool. A completed clip retains its frames while rolling history continues. One clip
may collect/encode at a time; extra triggers are skipped. Boss rarity decisions can
hold completed footage awaiting metadata; ordinary-loot candidates hold bounded
metadata until a qualifying drop triggers.

[CaptureSession](../src/ValheimMoments.Core/CaptureSession.cs) clears rolling history
and cancels pending collection when the ZNet session reference changes. GPU readbacks
carry a session revision and cannot enter a later session's history. Completed
worker-owned frames stay valid until their encoder releases them. The plugin also
clears waiting loot captures/candidates at the boundary. These paths have automated
coverage; repeated live leave/rejoin checks remain on the final test list.

[EncoderClient](../src/ValheimMoments/EncoderClient.cs) pipes completed RGBA frames to
a bundled hidden Windows x64 helper using Imazen.WebP 11.0.0 and libwebp 1.6.0. It
writes a partial output and moves it to the final WebP after successful assembly.
There are no raw-frame files, continuous video files or audio. Cancellation/timeout
terminates the helper. Pixel ownership is released on the game thread after encoding.
See [dependency licenses](DEPENDENCIES.md).

## Delivery boundaries

[HostConfiguration](../src/ValheimMoments/HostConfiguration.cs) shares a bounded typed
event-policy snapshot over direct peer RPC (HostSettings_v2). Discord.Enabled is the only shared Discord entry; URLs and Username remain private. Only the current server peer can install
it; servers ignore client snapshots. Unknown/private keys, duplicates, invalid values
and incompatible schemas are rejected atomically. Clients request updates every two
seconds; absent refresh for ten seconds pauses capture. Relay requires a recent
settings exchange. This governs ordinary clients, not modified-client footage claims.

The in-memory overlay preserves client config files. Configuration Manager metadata
makes host rules read-only, renders effective values and hides client-private Discord
settings while connected. The installed manager's Advanced/ReadOnly/CustomDrawer tags
and BuildSettingList method were inspected directly. In 0.13.0 the tag class uses the required ConfigurationManagerAttributes name; the old differently named object was not recognized. Category/Order place local controls first and custom dimensions below the preset. No manager DLL is bundled or required.

[CaptureLimits](../src/ValheimMoments.Core/CaptureLimits.cs) constrains dimensions,
FPS, quality and aspect ratio. Six presets derive a pixel budget from screen aspect. Fitting preserves ratio while reducing dimensions, then FPS; extreme minimum-budget cases use a padded 480 x 270 fallback. Padded rendering requires live verification. This fits frame-pool/raw-clip
budgets. Buffer replacement waits for GPU and encoder work. These bounds apply
regardless of which UI or file supplied the settings.

[ClipRelay](../src/ValheimMoments/ClipRelay.cs) uses connected peers' direct ZRpc
channels. Offers contain fixed event type, caption and file length, followed by paced
16 KiB chunks with acknowledgments. Host settings supply Discord URLs and bot name;
the host adds the connected recorder's name. Clients delete successfully uploaded
originals after host confirmation unless SaveLocalCopy=true; host temporary
relay files are removed after delivery/failure. Crashes can leave temporary files.

One incoming relay transfer/delivery is allowed at a time, with a 10 MiB cap, timeouts
and per-peer offer throttling. The host does not independently verify client captions,
events or rarity. Perspectives are not deduplicated or queued; a busy host may decline
one. See [relay checks](RELAY-TEST.md).

[DiscordWebhook](../src/ValheimMoments/DiscordWebhook.cs) uploads in the background
with secret-safe errors, redirects and mentions disabled. Rate-limit retries are
bounded; other ambiguous failures are not automatically replayed. Failed/skipped
uploads remain local. There is no persistent retry queue. Session checks prevent old
captures from being delivered through a new host's settings.

## Evidence and remaining tests

The user confirmed capture, Discord delivery, deaths, boss attribution/loot, Epic Loot
display, first-kill priority/rarity filtering, ordinary-loot highlights, host/client
F10 delivery and recorder attribution in game on 2026-09-09. The user subsequently
confirmed the broader natural-loot feature works in 0.11.0, while reporting the
single-name boss-credit limitation now addressed in 0.11.1. That roster fix has
automated multi-recipient coverage and awaits its own live confirmation.

An early stationary check reported 240–254 FPS with no noticeable F9 toggle difference.
Early real clips were around 1.78 MB, with one reported at 4.68 MB. These are observations
from earlier builds, not current-encoder benchmarks or a controlled frametime study.
Current WebP-only playback was separately accepted live.

The 0.9.1 synthetic encoder run produced 135 frames at 640×360 over exactly 9,000 ms:
34,044 bytes, about 703 ms encoding, and 23.4 MiB helper peak working set. Synthetic
pixels compress much more readily than gameplay. Independent RIFF inspection and
full decode passed, as did unequal frame durations, RGBA colors, vertical flip,
cancellation and truncated-input rejection. These figures describe one local run.

The event suite includes host-policy checks using actual BepInEx configuration and
simulated peers, alongside event assertions with actual Harmony and behavioral game/API
stand-ins, including simulated direct-peer transfer. Capture-core and fake-HTTP suites
also exist. Automated checks do not replace live multiplayer tests.

Remaining live coverage: automatic co-op death/boss/loot delivery, separate host
destinations, relay disable/disconnect, Windows dedicated hosting, longer memory and
frametime sessions, and broader graphics/network backends. Linux is outside the
packaged Windows x64 support target. Spirit-damage final-blow naming passed the user's
live 0.9.2 boss-kill test; fire/poison still need live verification. Mixed/unknown sources, effects already active before
tracking, ownership gaps, or unsupported damage paths can leave final blow unavailable.
No last-direct-hit guess is used.

## Death quota and optional feedback (0.13.0)

DeathMoments keeps a monotonically numbered, session-scoped death ledger and one pending delivery ticket. Success acknowledges only that ticket's snapshot; concurrent later deaths survive. Failure/cancellation releases the ticket without consuming counts. A sliding bounded quota limits capture attempts; the host also bounds accepted death offers per connected peer. The host does not authenticate client-reported death counts from footage.

ClipRelay.Offer accepts a structured completion observer, invoked once after a matching final result or failure/reset. Transferring all bytes alone cannot emit success. Plugin routes feedback only to the recorder's current session. MomentNotifications uses a single expiring IMGUI banner and lazily generated original 160ms PCM cue; it runs only with graphical capture initialized. Its AudioModule/TextRenderingModule references are game-provided, not bundled. Banners can be captured; WebP audio is unchanged (none).
