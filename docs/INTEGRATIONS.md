# Integration and validation notes

This describes 0.9.1, not a guarantee of compatibility with future versions.
Observers do not intentionally change damage, kill credit, rolls or saved statistics.

## Harmony patch inventory

| Target | Observer | Purpose |
| --- | --- | --- |
| Player.OnDeath() | PlayerDeathDetector prefix/postfix | Snapshot local alive state/cause; emit only after the original makes that local player dead. |
| Game.RPC_RegisterKill(long, string, int, int, int, bool) | BossKillDetector prefix/postfix | Compare profile kill counts before/after actual credit. Boss number selects boss or ordinary-loot rules. |
| Character.OnDeath() | BossAttribution prefix/finalizer | Scope owner-observed last-hit attribution around vanilla credit; restore nested context even on exceptions. |
| Game.RegisterKill(long, string, int, KillModifiers, int, bool) | BossAttribution prefix | Send final-blow metadata to the same credited recipient before vanilla credit. |
| ZRoutedRpc constructors | BossAttribution postfix | Register attribution channel for each router. |
| Character.OnDeath() | BossLootDetector prefix/finalizer | Scope owner-observed loot, publish its revision, restore context. |
| CharacterDrop.GenerateDropList() | BossLootDetector last-priority postfix | Read the returned roll for the exact character; never reroll it. |
| Game.RegisterKill(long, string, int, KillModifiers, int, bool) | BossLootDetector prefix | Announce the exact loot ticket to credited recipients. |
| ZRoutedRpc constructors | BossLootDetector postfix | Register ticket/result channels and clear stale inbox entries. |
| Ragdoll.Setup | BossLootDetector prefix, Epic Loot enabled | Associate the exact ragdoll with the current character's pending snapshot. |
| Ragdoll.SpawnLoot | BossLootDetector prefix/finalizer, Epic Loot enabled | Restore snapshot during delayed spawning, publish completion, clear association. |
| EpicLoot.LootRoller.RollLootTableAndSpawnObjects, two list-returning overloads | EpicLootAdapter postfix | Read completed spawned items inside the correlated death/ragdoll context. |

Source: [death](../src/ValheimMoments/PlayerDeathDetector.cs),
[credit](../src/ValheimMoments/BossKillDetector.cs),
[attribution](../src/ValheimMoments/BossAttribution.cs),
[loot](../src/ValheimMoments/BossLootDetector.cs),
[Epic Loot](../src/ValheimMoments/EpicLootAdapter.cs).
Supplemental metadata alone cannot trigger capture: actual local game credit is
required. These routed channels are distinct from the direct-peer clip relay.

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
Ordinary-loot highlights require it in this version. Unknown rarity cannot satisfy a
named threshold. Integration errors are logged without deliberately changing game outcomes.

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

[EncoderClient](../src/ValheimMoments/EncoderClient.cs) pipes completed RGBA frames to
a bundled hidden Windows x64 helper using Imazen.WebP 11.0.0 and libwebp 1.6.0. It
writes a partial output and moves it to the final WebP after successful assembly.
There are no raw-frame files, continuous video files or audio. Cancellation/timeout
terminates the helper. Pixel ownership is released on the game thread after encoding.
See [dependency licenses](DEPENDENCIES.md).

## Delivery boundaries

[ClipRelay](../src/ValheimMoments/ClipRelay.cs) uses connected peers' direct ZRpc
channels. Offers contain fixed event type, caption and file length, followed by paced
16 KiB chunks with acknowledgments. Host settings supply Discord URLs and bot name;
the host adds the connected recorder's name. Clients retain originals; host temporary
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
F10 delivery and recorder attribution in game on 2026-09-09.

An early stationary check reported 240–254 FPS with no noticeable F9 toggle difference.
Early real clips were around 1.78 MB, with one reported at 4.68 MB. These are observations
from earlier builds, not current-encoder benchmarks or a controlled frametime study.
Current WebP-only playback was separately accepted live.

The 0.9.1 synthetic encoder run produced 135 frames at 640×360 over exactly 9,000 ms:
34,044 bytes, about 703 ms encoding, and 23.4 MiB helper peak working set. Synthetic
pixels compress much more readily than gameplay. Independent RIFF inspection and
full decode passed, as did unequal frame durations, RGBA colors, vertical flip,
cancellation and truncated-input rejection. These figures describe one local run.

The event suite passes 166 assertions with actual Harmony and behavioral game/API
stand-ins, including simulated direct-peer transfer. Capture-core and fake-HTTP suites
also exist. Automated checks do not replace live multiplayer tests.

Remaining live coverage: automatic co-op death/boss/loot delivery, separate host
destinations, relay disable/disconnect, Windows dedicated hosting, longer memory and
frametime sessions, and broader graphics/network backends. Linux is outside the
packaged Windows x64 support target. Periodic Spirit/fire/poison damage can omit the
attacker and leave final blow unavailable; assigning the last direct attacker without
tracking the actual status-effect source would risk incorrect credit.
