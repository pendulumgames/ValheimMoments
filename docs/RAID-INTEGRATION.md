# Raid capture integration findings

0.17.0 integration status: Plugin now drives the observer, lifecycle, separate four/six-second captures, worker-owned media, composition and personal default-route delivery. The implementation checkpoints below preserve their earlier context. Gameplay wiring is complete for this fixed timeline; live acceptance, raid grouping, configurable timing and process-crash restart sweeping remain pending.

Inspected the installed Valheim assembly on 2026-09-11 with `tools/Inspect-Raids.ps1` (read-only Mono.Cecil inspection). This document describes implementation evidence, not a shipped raid feature.

## Participation

`RandEventSystem.FixedUpdate` selects the current random event as the local active event only while a local player is inside its area. The area check uses horizontal distance strictly below the event range and excludes positions above y=3000. Leaving calls `SetActiveEvent(null, false)`. A forced event takes precedence; forced boss ambience must not be treated as a raid.

The observer snapshots `GetActiveEvent()` and whether it equals `GetCurrentRandomEvent()` before `SetActiveEvent(RandomEvent, bool)`, then compares actual references afterward. This handles the game's same-name early return without inventing a transition. Only the random event receives entry/leave/end notifications. No local player means no local recording participant. Names are display metadata, not occurrence identifiers.

## Endings are not victories

On the server, `RandomEvent.Update` advances elapsed time unless configured to pause while nobody is in range. A positive duration expires when elapsed time is strictly greater than duration. The caller then clears the random event. `SetRandomEvent` also clears/deactivates the previous event when an administrator resets it or another event replaces it. If the old random event was locally active, it calls `SetActiveEvent(null, true)` in each case.

Consequently, that boolean means an event-ending notification, not a victory. The client `RPC_SetEvent` carries name, elapsed time and position; it does not carry an authoritative termination reason or unique occurrence ID. A local `RandomEvent` object identity must not be sent as a multiplayer identity or replaced by a guess based on a matching name/time.

Initial safe caption: **Raid ended**. The current observer/policy cannot claim all attackers died, the raid timed out naturally, or the player defended it successfully. A future verified natural-expiry caption would require server-side outcome metadata correlated with this exact occurrence. Grouped raid perspectives likewise need a host-generated occurrence ID. Existing creature-death IDs do not solve raid identity.

## Implemented checkpoint

`RaidDetector` observes entered/left/ended transitions without changing game behavior. `RaidMoment` tracks one bounded pending opening per session and local occurrence. Death, leaving, disabled policy, world change or a thirty-minute maximum lifetime abandon it; reentry into the same occurrence cannot replay a skipped/abandoned opening. An unrelated occurrence cannot complete it. Neither component currently registers with Plugin or writes footage.

## Next integration

The capture buffer now has post-only segment collection: pending readbacks submitted before entry are excluded, the completion watermark covers the full segment, and a missed initial readback holds the first available frame to preserve the intended duration. Cancellation immediately permits a normal capture. The composition client launches a hidden, low-priority helper with a 120-second timeout and cancellation, and waits for process exit before returning control to cleanup.

RaidMedia provides one private workspace and at most one active worker for an attempt. Disposal cancels its token and schedules cleanup after the worker completes. Cleanup removes only registered opening/ending/combined filenames and their recognized partials, does not recurse or follow a reparse-point workspace, and preserves unknown files. Tests cover an open exclusive file handle during cancellation, concurrent-worker rejection, disposed-workspace rejection, recognized interrupted partial cleanup and unrelated-file preservation. Process-crash restart sweeping remains pending; these guarantees apply to an attempt whose owner disposes the workspace. Plugin does not yet instantiate this owner or drive segment capture.

The helper now has a bounded `--compose opening ending output quality` path. It preflights both animated WebP containers before decoding (10 MiB each, supported canvas, frame rectangles, cumulative 1,800 frames/60 seconds/256 MiB raw-equivalent limits), requires matching dimensions, then decodes and submits frames sequentially using BGRA. The final frame duration uses the same pinned encoder contract as normal capture. It preserves inputs and existing output files; publication uses a uniquely created partial followed by a move, with owned partial cleanup on normal failure. A hard-killed helper can still leave that partial; parent-owned raid storage and crash cleanup are not implemented yet.

Tests decode a two-segment result and verify colors and irregular frame durations across the join, existing-output/input protection, invalid canvas and truncated input rejection, and normal-failure partial cleanup. Existing normal/close-call encode, decode and cancellation checks pass. This is decode/re-encode composition and can introduce another lossy pass; full raid visual quality and peak memory require live acceptance. No game client calls this helper mode yet.

1. Capture a bounded opening segment and encode it to an owned temporary intermediate. Release its raw pixels and the capture slot while the raid continues.
2. At a valid observed ending, capture the ending segment; combine bounded intermediates into the proposed four-second opening plus six-second ending animation. Verify actual decoded timing and peak memory.
3. Reject missing/failed opening segments, preserve death priority, and expire owned intermediate files on cancellation/session exit. No continuous recording during the raid.
4. Add host settings and personal default-route delivery, or add authoritative raid IDs before enabling multiplayer grouping. Keep unverified outcome wording explicit.

Automated observer tests use real Harmony with behavioral game stand-ins. Production compilation checks API compatibility. Live raid behavior, media concatenation and performance remain untested.
