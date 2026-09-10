# Valheim Moments 0.8.0 — multiplayer clip relay

Install the same current version on **both the host and recording clients**. Use a
separate test profile. Set MinimumRarity=None only if you want any observed loot drop
to qualify during testing; the shipped default is Legendary.

## Host setup

Configure Discord.Enabled, WebhookURL and Username on the host. Optional Good Loot,
Boss Kill and Player Death webhook overrides also belong to the host; disabled
overrides use the default. `Discord.EnableClientRelay = true` is the new default on
both sides. The host's trigger enable switches apply to incoming event types.

Clients never send a webhook URL or bot name. Their local Discord.Enabled, Username
and webhook entries do not control relay delivery. Capture and rarity settings remain
local in this increment. The host uses client-supplied event text and a fixed event
type, and adds **Recorded by:** using the authenticated connection's player name.
The host does not independently verify the depicted event or rarity.

## First co-op test

1. Host a world with 0.8.0, then have a second player using 0.8.0 join.
2. Wait five seconds on the joining client and press F10 there.
3. Expect the client's footage in the **host's** default Discord channel, under the
   **host's** Username, with the joining player's Recorded by line. The client log
   should confirm the host uploaded it. The client keeps the original WebP.
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
  after delivery/failure; clients always retain originals, even with SaveLocalCopy
  disabled. A process crash can leave a host RelayTemp file behind.
- Co-op perspectives are separate submissions. The first accepted transfer wins
  while busy; there is no encounter-wide deduplication or queued alternate view.

Standalone/host F10 and existing automatic captures continue to use the same settings
and formatting as 0.7.1. Relay network performance and Steam/PlayFab behavior still
need the live co-op test above.
