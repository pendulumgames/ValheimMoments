# Cinematic Camera: Experimental

0.23.0 provides host-controlled event switches, all off by default. Each produces a separate four-second attachment alongside normal event footage:

| Setting | Shot |
| --- | --- |
| RaidOpening | Elevated sweep toward the raid circle center; delivered with the completed raid moment. |
| BossSpawnIntro | Sweep of a new boss, cached until that exact boss dies; first attachment in the kill post. |
| BossKillEnding | Sweep around the boss death location after kill credit. |
| BiomeDiscovery | Configurable RiseAndReveal, Orbit or ZoomIn around the player on an eligible first biome discovery. |

Normal event eligibility, boss rarity/first-kill policy and discovery history still apply. Spawn intros are not standalone announcements. Missing identity falls back to ordinary footage.

## Framing and presentation controls

DistanceMultiplier defaults to 2 (twice the original distance), clamped to 1–3. PanDegrees controls the full arc, clamped to 0–180 with default 70. RaidMovement defaults to RiseAndReveal, aimed at the raid center, with Orbit available. Solid geometry and an 80 m offset limit can shorten the requested distance. The rise/orbit path does not move the player's camera.

Letterbox defaults on: black bars slide inward from the top and bottom over 0.85 seconds, producing a 2.39:1 viewing window while retaining the original 16:9 file dimensions. The title fades in afterward. Boss titles use the localized name, stars (game level minus one) and reported maximum health. Raid titles use the event message and up to three configured possible foe types; this is not a claim that those foes have already spawned.

The extra camera FlipVertically defaults true to correct the upside-down footage seen in 0.20.0. It is player-owned for graphics-backend differences and independent of normal capture orientation. Other framing controls remain host-owned. Existing cached/encoded clips are not reoriented retroactively.

## Rendering and storage

An ordinary Unity camera renders offscreen without GameCamera or an audio listener. It does not move the player's view. HUD and Moments notifications are excluded. The experimental budget is 640x360, 10 FPS, four seconds, using the player's WebP quality. One camera/encoder job runs at a time, with a 48 MiB frame-pool ceiling and at most four cached shots of 4 MiB each. Busy, severely obstructed, distant or failed shots are skipped.

Native ZNetView initialization distinguishes fresh creation from loading existing network objects. A private synchronized spawn timestamp starts filming immediately after creation by default, following the altar summon delay. BossSpawnDelaySeconds allows 0-5 seconds of extra delay; the scan adds up to 0.5 seconds and eligibility expires three seconds after that delay. Busy clients can miss the intro. This does not guarantee that modded spawn animations or SLS setup have finished. Additional sender-scoped boss identity accompanies existing kill attribution; positions and intros are matched by that identity, not enemy name.

The camera requires loaded surroundings within 100 metres of the player's view. Dedicated servers cannot render; a nearby client supplies footage. Solid-object checks shorten obstructed paths. Lighting, vegetation, sky, graphics backends and camera-mod compatibility need live testing.

Cached footage expires after 30 minutes and clears on session change/shutdown. Owned intermediate files are cleaned after use, with startup cleanup of recognized old crash leftovers. Gallery recovery/pinning policies apply to POVs and packed cinematic data.

## Director and delivery

Use matching 0.23.0 host/clients. Up to two extra WebPs travel with the POV inside private VMCI RIFF chunks. Ordinary WebP readers display the primary POV and ignore these chunks. The host strips them before upload and validates extracted animations using the bounded encoder.

Director chooses one copy of each enabled cinematic from selected participants. Boss arrival is first, followed by optional boss aftermath, then player POVs. Cinematics are never stitched into player footage. Cinematics have a short shot title; player nameplates remain limited to multi-POV posts. Gallery playback displays the primary POV.

The existing 10 MiB relay envelope includes extras, so conservative accounting can reduce the number of POVs that fit. Final per-file/aggregate limits are checked after labels; optional cinematics are omitted if they cannot fit alongside selected POVs. Up to five attachments are possible. Gallery success confirms POV delivery, not inclusion of every optional shot.

## Live acceptance

- Fresh F10: normal upload behavior, with no Discord-link button or metadata lookup.
- Each cinematic: player camera unchanged, upright output, correct sky/lighting/vegetation.
- Boss intro first and separate; two bosses of the same type do not share intros; loading old bosses does not create arrivals.
- Two-player Director: one cinematic per enabled role plus normal POVs within limits.
- Raid opening faces the center; biome reveal rises and tilts down for eligible first discovery.
- Pause, leave, disable options and obstruct the camera: normal capture remains usable.

References: [Unity camera rendering](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera.Render.html), [WebP optional chunks](https://developers.google.com/speed/webp/docs/riff_container), [Discord receipts](https://docs.discord.com/developers/resources/webhook).

BossMovement selects Orbit, RiseAndReveal or ZoomIn for both boss shots. ZoomIn is also available in RaidMovement. It moves straight toward the subject from 2x to 1x distance across four seconds while retaining a fixed 55-degree lens. ZoomIn ignores DistanceMultiplier and PanDegrees; obstruction handling still applies. The former test-build ZoomIn checkbox is no longer used. Obstruction checks use a wider corridor and try three higher viewpoints before shortening the shot; foliage without colliders can still obscure it. HUD health bars and mod overlays are excluded from the extra camera. Boss title health/stars reflect values reported by the game at capture time; mod-specific affix/modifier labels are not read. Normal player footage retains visible HUD, and Discord loot text supports Epic Loot item details.

Special enemies have independent `SpecialEnemySpawnIntro`, `SpecialEnemyKillEnding`, `SpecialEnemyMovement`, and `SpecialEnemySpawnDelaySeconds` controls. Only exact `Special Enemies.EnemyKeys` matches qualify. The same fresh-spawn detection, per-instance identity, framing controls, four-second camera duration, upload limits and fallback rules apply. Their own CaptureMode and rarity policy govern the kill post. Test fresh and loaded special enemies, two same-type instances, and owner-to-recorder attribution in co-op.

Biome discovery: enable `BiomeDiscovery`, select `BiomeMovement` (default RiseAndReveal), and leave `BiomeLetterbox = true` for the same animated black bars and fading title as boss cinematics. BiomeLetterbox is independent of the other events' Letterbox setting. Shared distance, pan and orientation apply; ZoomIn ignores distance/pan. Live checks: discover a new biome with each movement, verify bars and biome title, then disable BiomeLetterbox and confirm a full-canvas biome shot while boss bars remain enabled.

RenderCinematics is a personal, default-on performance toggle. Turning it off cancels local extra-camera work without changing normal POV recording or which peer cinematics the host permits in a post.
