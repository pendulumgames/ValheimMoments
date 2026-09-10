# Valheim Moments — multiplayer clip relay

Install the same current version on **both the host and recording clients**. Use a
separate test profile. Set MinimumRarity=None only if you want any observed loot drop
to qualify during testing; the shipped default is Legendary.

## Host setup

For 0.11.0, install the matching build on host and recording clients. Older hosts do
not provide policy snapshots; newer clients wait rather than use their own event rules.

Configure Discord.Enabled, WebhookURL and Username on the host. Optional Good Loot,
Boss Kill and Player Death webhook overrides also belong to the host; disabled
overrides use the default. `Discord.EnableClientRelay = true` is the new default on
both sides. The host's trigger enable switches apply to incoming event types.

Clients never send a webhook URL or bot name. Their local Discord.Enabled, Username
and webhook entries do not control relay delivery. The host syncs event,
timing, rarity and formatting rules; clients retain personal performance settings.
The host uses client-supplied event text and a fixed event
type, and adds **Recorded by:** using the authenticated connection's player name.
The host does not independently verify the depicted event or rarity.

## First co-op test

Before recording, check the new settings behavior:

1. Give the client a different local boss rarity/template from the host, then join.
2. In Configuration Manager, boss/loot/death rules should show host values read-only.
   Hotkeys, capture on/off, relay opt-out, dimensions/FPS/quality, memory budget, flip,
   local-copy retention and Advanced timing diagnostics should remain editable.
3. Change a host rule. The client should follow within a few seconds. Webhook entries
   should be hidden on the joining client, and no webhook should appear in its config.
4. Leave and enter single-player. The client's original event preferences should return.
5. With capture idle, try an extreme width/height/FPS via the UI. Bounds should clamp
   inputs; effective aspect ratio and memory-safe resolution appear in the capture log.
   Restore your preferred dimensions afterward. Confirm ordinary F10 still works.

Then verify delivery:

1. Host a world with the current version, then have a second player using that version join.
2. Wait five seconds on the joining client and press F10 there.
3. Expect the client's footage in the **host's** default Discord channel, under the
   **host's** Username, with the joining player's Recorded by line. The client log
   should confirm the host uploaded it. With SaveLocalCopy=false the client then
   deletes its original; with true it keeps the WebP.
4. Try a client boss/loot/death event after the first upload finishes. It should use
   the corresponding host destination (or host default if the override is off).
5. Disable Discord.EnableClientRelay on the host, relaunch, and repeat client F10.
   Expect a local client clip and a declined/unavailable log, with no client-webhook
   fallback. Restore the host setting afterward.
6. Optional disconnect test: leave during a transfer. No completed partial upload
   should occur; the client original stays local. An already accepted Discord post
   cannot be recalled by cancelling its request.

No webhook is needed on the joining client. Do not share the host's secret config.

## Dedicated-server test

Before dedicated hosting, complete the automatic co-op checks below on the already
working listen host.

### Automatic co-op checks

1. Use the current version on both players, in a test world/character where losing equipment or
   skills will not disrupt normal play. Keep the host's event switches enabled.
2. Once in-world, the host can press F9 to pause its own recording while leaving
   incoming relay delivery enabled. This isolates the joining player's perspective.
3. Have the joining player experience a normal in-game death, without pressing F10.
   Expect their death clip, cause when available, and Recorded by name in the host's
   default channel, or PlayerDeathWebhookURL if its override is enabled.
4. After delivery finishes, test a credited ordinary kill with Epic Loot enabled on
   the recording player and creature owner. For easy testing, set the recording
   host's Loot Capture.MinimumRarity to None while closed, then relaunch. Expect
   an automatic clip only if an actual item drops, with Generated loot and their
   Recorded by name. Restore their preferred rarity afterward.
5. Test a credited boss kill separately. FirstKillOnly and the recording player's
   boss rarity settings still apply. Expect boss loot and the selected credit/final
   blow lines. In 0.9.2, periodic damage from one tracked source can supply the final
   blow; mixed/unknown sources may still show unavailable.
6. If separate channels are available, configure one host override locally while
   closed, relaunch, and repeat its event. The event should use that channel and
   the host's bot name; F10 should still use the default channel. Do not send the
   webhook or host config to the joining player.
7. Press F9 on the host again to resume its own recording if it was paused.

Wait for each upload before the next event: the relay has no queue. Report which
event was tested, whether a local clip was created, and where the post appeared.
No configured webhook URL is needed in the report.

### Deferred 0.9.2 periodic-kill checks

The user confirmed that a Spirit-damage boss kill correctly named them in 0.9.2.
The remaining fire/poison and co-op cases below are still pending.

Keep Boss Kill.TrackPeriodicDamage enabled on the creature owner. Use a boss that
can take the chosen damage and ensure your existing boss filters allow the clip.
With one attacking player, apply Spirit damage, then stop attacking and let a periodic
tick finish the boss. Expect that player's final-blow name. Repeat with poison/fire
when practical. A direct finishing hit should continue to identify its attacker.
If multiple players stack Spirit/fire into the same still-active effect, unavailable
is intentional because the remaining damage has multiple sources. These checks and
automatic co-op routing are deferred to final testing at the user's request; they
have not been recorded as live passes.

### Headless hosting

### Session-boundary regression (0.9.3)

In a test world, start an F10 clip and leave during its post-event recording. Rejoin
and wait five seconds, then press F10 again. The new clip should contain only the new
session and upload normally. Repeat while an earlier clip is already encoding: its
local file may finish, but it should not upload through the new session. Look for
the session-change message in the capture log. These live checks remain pending.

### Headless startup

The relay starts before graphics/encoder checks. A headless server should log that
host delivery is ready and graphics capture is disabled. Install the plugin and its
normal dependencies on the Windows BepInEx server, configure its webhook locally,
then repeat client F10. This path compiles against the installed game API but needs
a live dedicated-server check; no dedicated server is available in this workspace.

## Transfer limits and current policy

- One incoming transfer or relayed upload at a time on the host; one outgoing clip
  per client. Host-local uploads share the same uploader. Busy submissions are
  declined, not queued or replayed later.
- Maximum 10 MiB per relayed clip, further restricted by the host's MaxUploadMiB.
  One 16 KiB chunk per acknowledgment, paced to at most 20 chunks/second. Networking
  latency can lower throughput; a 4 MiB clip may take roughly 13 seconds to transfer
  before Discord upload, plus acknowledgment latency.
- Host receive deadline: 120 seconds. Client initial response/read deadline:
  10 seconds; subsequent acknowledgment deadline: 15 seconds; final result wait:
  90 seconds. At most one offer per connected client per 15 seconds is accepted for
  consideration. Failed/timed-out offers are not automatically retried.
- Size, offset, event type, caption length, GUID and RIFF chunk boundaries are
  validated. The host does not decode the media on the game thread. Transfers use
  direct peer RPCs, not client-supplied routed sender IDs.
- File reads, host temporary-file writes and HTTP run on workers. Each side retains
  at most one relay byte buffer up to 10 MiB. The host deletes its temporary file
  after delivery/failure; clients delete originals after confirmed success when
  SaveLocalCopy=false. Failures retain originals. A crash can leave a host RelayTemp file.
- Co-op perspectives are separate submissions. The first accepted transfer wins
  while busy; there is no encounter-wide deduplication or queued alternate view.

Live co-op F10 delivery for both the host and a joining player passed on 2026-09-09,
and the clips looked correct. Version 0.9.1's host/solo Recorded by change also passed
the user's live check.
Dedicated-server behavior, automatic co-op events and broader Steam/PlayFab coverage
still need live testing.
