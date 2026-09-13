# Valheim Moments — multiplayer clip relay

## Final 0.18 acceptance pass

Use matching 0.21.0 builds on host and clients. No installation was performed during development.

## 0.23.0 camera and event acceptance

Use matching 0.23.0 host/clients. Verify Barely Survived impact at second two with slow aftermath through second three. Test independent special-enemy capture modes, loot settings, attribution, routing and arrival/aftermath cameras. Test all BiomeMovement choices and default-on BiomeLetterbox, including independent full-canvas behavior when disabled. Check DistanceMultiplier clamps a saved 8 to 3; try all three boss movement dropdown choices (Orbit, RiseAndReveal, ZoomIn) in a forest. ZoomIn should approach from 2x to 1x with no horizontal sweep or lens change, regardless of DistanceMultiplier/PanDegrees. Repeat ZoomIn for a raid. Verify upright letterboxing/titles and unchanged player view. Summon a boss at its altar: default delay should catch arrival following the native summon delay; try an extra two seconds and confirm existing loaded bosses do not replay intros. Verify optional intro delivery with that same boss kill. Foliage without colliders and modded spawn animations need explicit observation.

## 0.21.0 cinematic and Discord acceptance

Run the [cinematic camera checklist](CINEMATIC-CAMERA.md#live-acceptance). These switches default off. Confirm the Discord-link button is absent. Test host and client POVs with the same boss arrival as a separate first attachment, and repeat with two bosses to verify identity matching.

## Presentation acceptance

- Open/close F8 and click outside. Move the mouse and scroll over cards: no player look or camera zoom; normal input returns after closing. Verify mouse and controller, close/reopen, empty gallery, several pages and deletion/expiry while open.
- Thumbnails appear automatically on the left, information/actions on the right. Open file location selects the surviving original in Explorer; expired files cannot open. Retry is absent for successful/local-only/working entries and retains cooldown/attempt/session rules for failed, omitted or unknown delivery.
- Make a fresh upload on host and client. Confirm no Discord-link button or link-unavailable message is shown.
- Exercise Cinematic, Toast: Minimap and Toast: Top Right, sound modes, moved/hidden minimap, normal and ultrawide display. Verify slide/fade and readable text. Saving Memory starts after ending footage; Sent to Discord follows confirmed receipt, without a third captured notice.
- Inspect clips while notifications overlap ongoing recording: no notification pixels should appear, including another memory's delivery toast. Check the active graphics backend; this renderer requires live validation.
- Two-person director boss clip: both files contain the correct bottom-center nameplate; no Perspectives heading. Single-perspective/manual/death clips have no nameplate. Appended Cause label is bold. Verify post sizes remain within configured budgets after label re-encoding.

1. Open F8; verify cursor access, blocked character input, close/reopen, thumbnails, local WebP opening and Configuration Manager ordering. Remap F7/F8; verify local controls remain editable for joining players.
2. Press F10 then F7 during recording, encoding and uploading. Verify Gallery/Saved preserves the clip. Upload another without Keep: original expires after 30 seconds, thumbnail/history remains. Check SaveLocalCopy with host Discord disabled.
3. Cause a known delivery failure; inspect gallery status. Correct the host destination and retry in the same session. Check backoff and the three-attempt limit. Unknown delivery must require duplicate confirmation. Disconnect/reconnect or restart: old retries must stay disabled.
4. Host/client enter and remain in one raid; verify one grouped post when exact identity is available. Leave, die, reset or switch worlds: no fabricated survival. Try customized opening/ending durations and verify total playback.
5. Test direct/periodic close-call hits, poison oscillation and death during follow-up. Customize follow-up and slow/total playback, verifying survival requirement and exact playback duration.
6. Confirm receipt links where available, omitted perspectives, disabled event policies, dedicated-host coordination and no client-controlled destinations or bot names.
7. Record quiet movement, forest motion and particle-heavy combat at selected sizes/FPS/quality. Note bytes, actual dimensions, duration, encode time and FPS. These live measurements are required before publishing typical-size estimates; synthetic tests do not establish them.

Install the same current version on **both the host and recording clients**. Use a
separate test profile. Set MinimumRarity=None only if you want any observed loot drop
to qualify during testing; the shipped default is Legendary.

## Host setup

For 0.21.0, install the matching build on host and recording clients. Older hosts do
not provide policy snapshots; newer clients wait rather than use their own event rules.

Configure Discord.Enabled, WebhookURL and Username on the host. Optional Good Loot,
Boss Kill and Player Death webhook overrides also belong to the host; disabled
overrides use the default. Discord.Enabled controls host and client delivery. Capture.SaveLocalCopy is player-owned. Matching 0.15.0 settings/relay protocols are required.

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
   Hotkeys, capture on/off, size preset, Custom dimensions/FPS/quality, memory budget, flip,
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
5. Disable Discord.Enabled on the host. On the client set Capture.SaveLocalCopy=true and press F10: expect a local clip and no relay. Set SaveLocalCopy=false too: F10 should produce no clip. Restore the host delivery setting and confirm client uploads resume.
6. Optional disconnect test: leave during a transfer. No completed partial upload
   should occur; the client original stays local. An already accepted Discord post
   cannot be recalled by cancelling its request.

No webhook is needed on the joining client. Do not share the host's secret config.

## Notification and death milestone check (when convenient)

1. On F10 acceptance, expect Recording Memory with the optional quiet cue. Encoding completion may briefly say Memory Captured; Memory Uploaded must wait for actual Discord success. A busy F10 must not announce successful recording.
2. On a joining client, verify the final notification follows host upload success, not just transfer completion. The host should not see another player's personal capture notification.
3. With host Discord disabled and local Capture.SaveLocalCopy=true, expect Memory Saved and a local file. Test Notifications.SoundMode=Off and a lower Volume independently on each player.
4. With default death quota, record one death and let delivery finish. A second death within 60 seconds should not create another death clip. Die again after the window: the next death post should report one additional intervening death (more if additional deaths occurred). Another player's quota is independent.
5. Default captions should include a neutral cheeky line without immediate repeats; custom templates should retain their wording unless they contain {flavor}. Inspect the cause and recorded-by formatting.
6. Settings/sizing checks from 0.12.0 remain pending if not already completed. Observe banner size/orientation, cue volume and whether the banner in footage is acceptable. No urgent test is needed during the current playing session.

## Final boss-credit check - 0.14.0

1. Both players contribute damage to a boss; keep a third non-attacking player nearby if available.
2. With PlayerNameMode=Both, each successfully delivered clip should list both credited players, exclude the spectator, identify its own recorder, and show the separate final blow.
3. Repeat with only one attacker and with a different player landing the final blow.
4. Confirm personal first-kill/rarity rules still apply per recording character. KillCredit/FinalBlow modes and {credit}/{killer}/{player} templates should keep their documented meaning.
5. If a roster cannot be obtained, expect the confirmed recorder plus "full list unavailable". Do not interpret that fallback as a complete participant list. Record the version on the boss-owning peer as well as host/clients.

Automated tests cover local-owner and remote-recipient rosters, spectators, missing
metadata, bounded names, expiration, exception cleanup and unchanged trigger gating.
The final live roster result has not yet been reported.

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
5. Test a credited boss kill separately. CaptureMode and the recording player's
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

- One incoming transfer and one upload at a time on the host; one outgoing clip per
  client. The director reserves bounded metadata/byte capacity before requesting selected
  transfers. Host-local clips share the same queue. Excess offers are omitted.
- Maximum 10 MiB per relayed clip, further restricted by the host's MaxUploadMiB.
  One 16 KiB chunk per acknowledgment, paced to at most 20 chunks/second. Networking
  latency, worker disk I/O and tick pacing can substantially lower throughput.
- Host transfer inactivity deadline: 30 seconds, with an absolute deadline of 30 seconds
  plus 0.5 seconds per chunk. Initial client response/read deadline: 10 seconds;
  subsequent acknowledgments: 30 seconds; final result wait: 90 seconds. Queue wait
  heartbeats refresh a 30-second wait, bounded to 20 minutes at the host. At most one
  offer per connected client per 15 seconds is considered. No automatic retry.
- Size, offset, event type, caption length, GUID and RIFF chunk boundaries are
  validated. The host does not decode the media on the game thread. Transfers use
  direct peer RPCs, not client-supplied routed sender IDs.
- File reads, host temporary-file writes and HTTP run on workers using bounded chunks,
  rather than whole-clip relay byte arrays. The host deletes its temporary file
  after delivery/failure; clients delete originals after confirmed success when
  SaveLocalCopy=false. Failures retain originals. A crash can leave a host RelayTemp file.
- Compatible creature-death perspectives group within the collection window. Late,
  oversized and excess perspectives are omitted; missing metadata stays separate.

Live co-op F10 delivery for both the host and a joining player passed on 2026-09-09,
and the clips looked correct. Version 0.9.1's host/solo Recorded by change also passed
the user's live check.
Dedicated-server behavior, automatic co-op events and broader Steam/PlayFab coverage
still need live testing.

## Discovery and special-enemy milestone (0.14.0)

These checks can wait for the next convenient test session. Install matching 0.15.0 on the host and recording clients with Valheim closed. Preserve the plugin's State folder. The test package includes the configuration reference.

1. Join a world, wait for host policy and at least five seconds of history. Login itself should not generate discovery clips. Enter a biome not yet tracked in this character/world; expect one discovery with Recorded by. Nearby labeled location/trader discoveries may group into the same caption.
2. Leave and revisit, then quit/reload and revisit again: no repeat. With the same character in a second world, test an untracked destination after warmup. With another character in the first world, confirm its independent history. Existing exploration before installation can count as first tracked; do not interpret it as a migration failure.
3. Physically approach a trader; another player's distant greeting and a remotely revealed map pin must not count. Check a real named-location banner; unlabeled dungeons are not promised. Change language and revisit a known biome: identity should stay the same.
4. Disable a discovery category, visit a new place, then enable and revisit: no replay. Repeat while paused or within cooldown. Busy/failed captures are remembered too. Try two nearby discoveries for grouped names and two well-separated ones after cooldown.
5. Leave DiscoveryWebhookURL blank to use the main channel. Set a valid dedicated destination on the host and confirm discovery routing. Nonblank invalid URLs must retain recovery clips, not post to the main channel. Joining clients cannot view/change URLs or event policy.
6. On the host, enable Special Enemies.LogEnemyKeys in Advanced. Kill a candidate and copy the logged confirmed identifier into EnemyKeys. Disable the diagnostic. Repeat after capture warmup: expect a special clip without a rarity requirement, the recording character's kill credit, observed loot and the main Discord destination.
7. Test a second configured key, a nonmatching enemy, the cooldown boundary, and FirstKillOnly using a genuinely new character/enemy statistic. A normal boss still follows Boss Kill rules. An accepted special kill must not also schedule an ordinary kill-loot clip for that death. Spectators should not gain a confirmed credit.
8. Repeat a discovery and special kill as a joining client, then with a Windows dedicated host when available. Existing relay limits/busy rules apply; separate perspectives are not grouped yet. Confirm final upload feedback and SaveLocalCopy cleanup only after host success.

Automated tests cover persistence, world/character separation, loading switches, corrupt and full histories, configured matching/cooldowns, hook scoping, routing and host policy. These are not substitutes for the in-game checks above. Previously deferred size/UI/audio/death-rate and final boss-credit checks remain applicable.

## Multiplayer director milestone (0.15.0)

Install matching 0.15.0 on the host and every recording client when convenient, with Valheim closed. This is the first live acceptance test of grouped delivery; earlier automated checks used simulated game/peer APIs.

1. Use two or three credited players on one boss. Ensure generated clips satisfy boss rarity/first-kill rules. Expect one Discord post containing the selected perspectives and each recorder's label. If only one character has a first kill, that status must not apply to everyone.
2. Confirm the host's perspective participates. Repeat with a dedicated host to verify client-only selection. No camera footage is expected from a headless server.
3. Set MaxPerspectives=1, then 2/3. Excluded players should see an omission message and retain their originals. Included players should receive Memory Uploaded only after Discord confirms the post.
4. Lower MaxPostMiB enough that fewer perspectives fit. Confirm omission without unchanged oversized retries. Raise the budget within your Discord destination allowance and retry a new event.
5. Test two same-name bosses killed close together. Their shared occurrence IDs must keep their recordings separate. Manual F10, personal deaths and discoveries must remain separate posts.
6. Verify Capture.SaveLocalCopy independently on each recorder. Successfully included clips follow that preference; omitted/failed/uncertain clips remain. Host RelayTemp files should disappear after delivery finishes.
7. Disconnect a selected player during transfer, and switch/close the host world while HTTP is pending. No old clip may move into the next world's queue. Missing final confirmation should retain originals and warn to check Discord.
8. Try a deliberately slow encoder/late offer, invalid webhook, and Director.Enabled=false. Late/failed offers must not announce an upload; disabling the director should restore individual delivery after pending grouped work cancels.
9. Review Configuration Manager: Director controls are host-owned and locked on joining players. Confirm the four new settings are ordered/readable and changing them does not overwrite player preferences.

Known limits: 10 MiB per-file director/relay cap; 60 MiB total queued reservation; at most 16 offered perspectives and eight pending selector groups; no visual-quality scoring, automatic re-encoding or tier detection. Primary perspective supplies the shared caption/loot summary. Collection starts after encoding, so different encoding times can cause late omission. Failed-file expiry/gallery/Keep remain pending.

## Close-call milestone (0.16.0)

Use matching 0.16.0 host and clients when convenient, with the game closed for installation. No test is required during the current play session.

1. Take damage crossing the configured health threshold, survive twenty seconds, and confirm a ten-second slow/fast clip with Recorded by in the default host Discord destination.
2. Repeat with periodic damage. Health oscillating below the recovery percentage must not produce repeated captures. Recover for the configured hold and wait out cooldown before the next qualifying crossing.
3. Die during follow-up, including near its end: no close-call post; the ordinary death clip follows its existing quota rules.
4. Pause capture, disconnect, or change capture settings during follow-up: no close-call post. Confirm subsequent manual/death capture still works.
5. Confirm joining players cannot edit Close Calls policy and their local webhook cannot redirect delivery. Discord disabled plus SaveLocalCopy enabled should retain a local clip.
6. Check smoothness, effective capture settings and FPS impact at your preferred resolution. Synthetic timing tests do not establish in-game performance.

## Raid milestone (0.21.0)

Install matching host/clients only with Valheim closed, when convenient.

1. Participate in a random raid, remain alive/in range, and observe its end. Expect one ten-second opening/aftermath clip, labeled Raid ended, with Recorded by in the default host destination.
2. Leave, die, pause capture, change capture settings, or disconnect during the attempt. No raid post should follow; later manual capture must work.
3. Confirm ordinary captures work between the opening encode and raid ending. If the ending occurs while another clip is busy, the raid pair is skipped.
4. Admin reset should say Raid ended, never raid victory. Forced boss ambience must not start a raid capture.
5. Confirm the attempt's RaidTemp directory disappears after normal completion/cancellation. Test host-disabled Discord with SaveLocalCopy enabled for local output.
6. Check join timing, visual quality and FPS. Grouped raid perspectives and restart orphan cleanup are not part of this release.
