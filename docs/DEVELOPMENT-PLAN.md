# Valheim Moments development plan

Updated: 2026-09-10. Baseline: 0.11.1.

This is the implementation backlog for the user's September feature request. Existing 0.11.1 packages remain unchanged. Items are planned unless listed in the progress section below. Do not describe pending features as available in release READMEs.

## Progress: 0.13.0 notifications and death milestone

User deferred live checks and authorized continued development. This build includes 0.12.0 unchanged in scope plus themed recording/save/upload feedback, optional original quiet cue, local notification settings, host-controlled death rate limits and 20 neutral cheeky captions. A structured relay completion callback ensures upload feedback occurs only after final host acknowledgement. Death counters acknowledge a particular pending snapshot, so later deaths and failed uploads are not lost. Host admission also bounds incoming death offers per connection.

Default quota: one death capture attempt per 60-second sliding window. Counters reset with the session. Custom captions opt into flavor with {flavor}; {extra_deaths} places the additional death count. Default flavor lines were made neutral rather than guessing gravity, food or equipment causes. New notification settings do not invalidate capture buffers.

Live UI/audio, source orientation/padding, local-only save and co-op checks can be batched into a later session. No installation while the user plays. Discovery/special-enemy work is next; director, segmented raid/close-call capture, gallery and Keep remain pending. Bounded failed-file retention and measured size estimates remain outstanding foundation items.

Validation: 1,756 assertions passed (core 1,244; HTTP 43; boss 55; loot 45; filter 31; Epic 19; relay 30; periodic 23; host settings 63; death moments 139; natural loot 43; death/messages 21). Full release checks, 70-key documentation coverage, format/provenance checks and installed Configuration Manager contract passed. Artifact: artifacts/Valheim_Moments-0.13.0.zip, 518,703 bytes, SHA256 B627753ED96CB3B127712EDA3613DCAC4BB7D70C187AC928367A567792B60A75. Built separately, not installed or submitted to Thunderstore.

## Progress: 0.12.0 configuration and sizing milestone (included)

Implemented: Discord.Enabled default true with explicit prior values preserved; host-only Username retained; redundant upload/relay switches retired; host delivery-enabled bit synchronized through settings protocol v2; local SaveLocalCopy migration and local-only recording; boss CaptureMode with equivalent migration of all old combinations and explicit first-kill caption; six size presets plus Custom, aspect-preserving fitting and padded fallback; ordered player controls and recognized, visibly locked host settings tags.

Found and corrected the Configuration Manager tag class-name mismatch: the installed manager specifically recognizes ConfigurationManagerAttributes. Added an installed-assembly metadata contract check. Custom dimensions use an Apply button so typing an intermediate value does not immediately clamp it before the number is complete.

Pending from the foundation: gallery-aware centralized file lifecycle and bounded failure retention (will land with the gallery groundwork); empirical gameplay size estimates; live UI/rendering verification. Notifications, death cooldown/personality, discoveries, special enemies, director, segmented events, gallery and Keep remain future milestones. GalleryKey defaults to F8 when its screen exists; it is not an inert setting in 0.12.0. Relay stays capped at 10 MiB until its bounded transfer redesign.

Build separately while the user plays. Do not install this milestone into the running Test profile.

Validation: 1,604 assertions passed (core 1,244; HTTP 43; boss 55; loot 45; filter 31; Epic 19; relay 22; periodic 23; host settings/migration 58; natural pickups 43; death/messages 21). Full release build, 64-key documentation coverage, animated WebP format checks, package validation and native-binary provenance passed. Installed Configuration Manager metadata contract also passed. No live game test or installation was performed.

Artifact: artifacts/Valheim_Moments-0.12.0.zip, 513,097 bytes, SHA256 B869E29EA8701660E14654950DE0D7AA4FC16951C29BE47FD7C4D8CC1A4CD831. Separate test ZIP includes updated configuration and co-op instructions.

Next convenient live milestone check: inspect local controls first/gray host rows on a joining client, switch a size preset and review an F10 clip (especially padding/orientation), and confirm local-only saving with host Discord off. No need to stop playing solely to install this build now. Matching 0.12.0 host/client versions are required when testing.

## Product rules

- The host owns multiplayer event policy, Discord enablement, destinations, upload budgets, grouping and the Discord username override (default Valheim Moments). Webhook credentials never go to clients.
- Players own capture performance, hotkeys, local retention, notification presentation/audio, and their personal gallery. Preserve the ability to disable local capture.
- Capture.Enabled controls recording; Discord.Enabled controls delivery; Capture.SaveLocalCopy controls permanent local retention. These are distinct decisions.
- Work on the Test profile only. Preserve existing config choices through explicit migrations. Do not delete historical footage during migration.
- Favor game events, saved progression, and configurable identifiers over maintained enemy/location lists. Inspect installed game APIs before committing to hook semantics.
- Keep memory, encoded files, transfers, retries, gallery storage, and pending event counts bounded. No continuous raw recording to disk.
- Keep current animated WebP output for this scope. Notification sounds play locally; WebP clips have no audio track.

## Confirmed user decisions

1. Barely survived requires surviving the entire 20-second follow-up. Death during it cancels the close call and uses death policy.
2. Discoveries count once per character per world. Use a versioned ledger where vanilla progression is global.
3. Keep Discord.Username, host-only, default Valheim Moments; it overrides the webhook integration name.
4. Gallery has a player-editable keybind, proposed default F8, with conflict guidance.
5. Server boost level affects upload allowances. Model destination/server budgets, not personal Nitro entitlement.
6. User is playing Valheim. Build and test separately; do not install or interrupt the running game. Prefer substantial milestones over frequent live-test requests.

Development is authorized through substantial reviewable milestones.

## Milestone 1: configuration, identity, and retention foundation

### Discord simplification

- Default Discord.Enabled to true for new configurations. Preserve an explicit existing false value; an empty URL must produce a clear setup status, not network traffic.
- Keep Discord.Username and the outgoing override, default Valheim Moments. Only host/single-player configuration applies; joining clients cannot rename posts. The webhook's avatar remains unchanged.
- Remove Discord.EnableClientRelay and Discord.UploadClips from active bindings and runtime branches. Relay and upload follow effective host Discord.Enabled and event eligibility.
- Expose the host delivery-enabled state to clients as nonsecret synchronized policy. Today all Discord entries are deliberately excluded from shared settings; removing the relay switch without fixing this would leave clients reading local delivery values.
- Retain session-bound routing and host-only delivery. A departing client must never send a former server's clip to a personal webhook.
- Version the changed settings/relay contract and give mismatched versions a clear status. Preserve bounded validation and headless initialization.
- Migration retires obsolete keys without printing webhook values. Update tests and active documentation; retain historical changelog entries as history.

### Independent local retention

- Move Discord.SaveLocalCopy to Capture.SaveLocalCopy, still player-owned and false by default.
- Migrate the old value only when the new key is absent; an explicit new value wins. Make migration idempotent.
- Discord off + SaveLocalCopy on: capture and save locally, no upload or relay.
- Discord on + SaveLocalCopy on: upload and keep a permanent local original.
- Discord on + SaveLocalCopy off: transient delivery file, then delete after confirmed delivery unless pinned by Keep this moment.
- Both off: no permanent footage. The later Keep feature may use a bounded temporary grace period; avoid needless encoding until that feature exists.
- Invalid webhook/network failure is not equivalent to intentional Discord off. Keep failures only in the bounded recovery policy introduced with the gallery, with clear status and expiry.
- Move deletion decisions out of the HTTP transport and into clip lifecycle management so upload completion cannot race Keep, gallery preview, or a retry.
- Local-only means host/single-player Discord is disabled. This does not reintroduce a client-controlled destination or a redundant relay toggle.

### Configuration Manager organization

- Player view: editable Capture settings first, then Notifications and Gallery; host-controlled event settings afterward in visibly disabled rows with a Host controlled label.
- Host view: Capture, Notifications, Discord delivery, event categories, Gallery, then Advanced troubleshooting. Group and order entries explicitly.
- Keep secrets hidden from clients. Show a nonsecret delivery status in their place.
- Confirm the installed Configuration Manager's ordering/category metadata and disabled drawing APIs before implementation; existing ReadOnly/CustomDrawer hooks already exist but need visual verification.
- Lock enforcement must remain in effective runtime policy, not just gray UI controls. Recompute presentation on join, disconnect, host changes, and policy refresh.
- Keep player performance controls, SaveLocalCopy, notification volume/display, gallery retention and hotkeys editable. Existing local timing diagnostics remain Advanced.
- Put custom Width and Height immediately below the new size picker; disable those fields unless Custom is selected. Show effective dimensions and FPS when budgets reduce the requested values.
- Remove stale help text (including the current single-person kill-credit description).

### Clear boss selection

The current combination already supports the requested behavior: FirstKillOnly=false, rarity filtering on, FirstKillBypassesRarity=true. Replace the confusing combination in the UI with one host-owned policy selector, retaining configurable minimum rarity:

- All kills.
- First kill only.
- Qualifying rarity only.
- First kill, then qualifying rarity (requested normal mode).

Map existing combinations without losing user intent. Preserve the existing per-character saved boss history and label the first-kill event clearly in the caption. Do not reset first-kill history on upgrade. First kills bypass rarity but still collect loot for the summary. Repeat kills without qualifying loot do not post.

Acceptance: old/new configs, new defaults, host-only username in outgoing payload, local-only capture, relay controlled by host enablement, host/client role changes, all boss modes, and successful-upload deletion/preservation. Live Configuration Manager check on both host and client.

## Milestone 2: aspect-aware sizes and upload guidance

- Six named size presets plus Custom. Proposed 16:9 equivalents: 480x270, 640x360, 854x480, 960x540, 1280x720, 1920x1080. Default 640x360 equivalent to retain current performance expectations.
- Use the actual capture surface aspect ratio, including windowed play. Derive dimensions from a preset pixel budget; retain the source ratio rather than stretching the image into a 16:9 shape.
- Respect practical minimums 480 wide/270 high and maximums 1920 wide/1080 high. For unusual ratios, adjacent presets may converge at the minimum/maximum; show actual dimensions instead of promising six distinct resolutions on every display.
- Preserve aspect ratio during memory fitting. The existing independent width/height clamps can distort it near a limit and must be revised.
- Ratios outside the supported interval need an explicit aspect-preserving fit with padding, never silent stretching. Reevaluate on window changes without reallocating every frame or interrupting an active encoder.
- Existing configs with custom dimensions migrate to Custom. Fresh installs use the default preset.
- Keep FPS 1-30, quality 1-100, and bounded memory/raw-encoder limits. A high preset is a request, not a guarantee that a long clip fits at full resolution.
- Add Capture help explaining duration = pre + post, motion/detail affects compression, and WebP quality is not a target bitrate. Distinguish capture RAM from encoded attachment size.
- Produce an empirical comparison table from representative gameplay (quiet, moving forest, combat/particles), recording resolution, effective FPS, duration, quality, bytes, encode time, and observed range. Until measured, label estimates as provisional; do not invent typical file sizes from synthetic encoder tests.
- Use actual completed file sizes for acceptance. Optionally show a conservative recent-history estimate; clearly mark no-data conditions.
- Add a host-owned upload budget per destination, defaulting conservatively until webhook behavior is verified. Keep a separate aggregate post budget and transport/resource cap.

### Discord evidence and limits

Checked 2026-09-10:

- Current official API source says default 20 MiB per file, with a calculated attachment_size_limit available for interactions: https://github.com/discord/discord-api-docs/blob/main/developers/reference.mdx#uploading-files
- User-facing FAQ says the free limit changed to 20 MB in August 2026, Nitro Basic 50 MB, Nitro 500 MB, and images over 20 MB may appear as downloads: https://support.discord.com/hc/en-us/articles/25444343291031-File-Attachments-FAQ
- Webhook execution's username is an optional override: https://docs.discord.com/developers/resources/webhook#execute-webhook

Some rendered/cached API pages still say 10 MiB. Personal Nitro limits are not proof of this incoming webhook's allowance. Incoming webhook credentials alone do not provide the interaction attachment_size_limit field; automatic tier discovery is not established. Do not add a bot token dependency solely to guess limits. Show host budget configuration and honest unknown/verified status. Document applicable server allowances only after checking current authoritative guidance and actual webhook behavior. Large accepted files may still have worse preview behavior.

The current relay is hard capped at 10 MiB and paced at 16 KiB per acknowledgement, approximately 20 Hz, with 120-second receive deadlines. Larger tier budgets require coordinated cap, disk streaming, pacing, timeout, queue, and cancellation changes, not just changing a configuration maximum. Use declared sizes and reject over-budget offers before accepting bytes.

Acceptance: standard/ultrawide/portrait/window resize, all presets, Custom, budget fallback preserving ratio, no pathological allocations, accurate help labels, oversize rejection and bounded adaptation. Validate preview behavior in Discord separately from HTTP success.

## Milestone 3: notifications and death personality

### On-screen feedback and sound

- Large, brief, Valheim-themed text with fade and restrained ornamentation.
- Accepted manual hotkey or automatic capture: Recording Memory. Rejected/busy/disabled requests must not show recording success.
- Encoding complete with a retained local clip: Memory Saved.
- Confirmed Discord response or final host delivery acknowledgement: Memory Uploaded. Merely handing bytes to the host is not upload success.
- Failure: brief actionable status, with gallery details when available. Coalesce queued notifications to avoid covering combat repeatedly.
- Short low-volume cue at the same time as the chosen start/completion notification. Local enable/volume options; use a suitable verified game sound or an original/licensed bundled cue.
- Avoid recording our own banner where practical by separating its rendering from capture; verify camera/render order rather than promising exclusion prematurely.
- Handle pause, UI scale, other HUD messages, scene transitions and headless hosts safely.

### Death rate limiting

- Host settings: DeathCaptureLimit (proposed default 1) and DeathWindowSeconds (proposed 60). Sliding window per character/session, not one shared server quota.
- Track all confirmed deaths even while suppressing repeated captures. On the next eligible death message include, for example, 3 additional deaths since the last shared death.
- Separate pending and acknowledged counts. Only consume the reported counter after confirmed delivery (or completed local save in local-only mode); failed uploads must not silently lose counts. Prevent overlapping pending clips from claiming the same deaths.
- Do not replay suppressed footage later. No delayed standalone message is required if the player stops dying. Reset session-scoped state on world changes and expose the count in the gallery when available.
- A manual clip does not reset the automatic death limit. Server-side rate controls still bound altered or outdated clients.
- Optional cheeky captions with no immediate repeat, retaining recorder and accurate cause. Existing custom templates take precedence; expose a flavor placeholder/toggle instead of overwriting user text.

Proposed 20 built-in lines:

1. A tactical donation to the local gravestone collection.
2. The corpse run has acquired a sequel.
3. Confidence: legendary. Survival: common.
4. Another successful test of gravity.
5. Valhalla put you on hold.
6. Your shield would like a word.
7. The bees are happy. You are not.
8. That was almost a plan.
9. Rested bonus expired. So did you.
10. A bold strategy with a very short conclusion.
11. Your inventory has become a destination.
12. The repair bill just got personal.
13. The floor remains undefeated.
14. One more reason to eat before leaving home.
15. The adventure continues. Slightly less clothed.
16. A memorable contribution to enemy morale.
17. You found the difficulty setting.
18. This shortcut includes a respawn.
19. The gravestone business is booming.
20. Odin saw that. Unfortunately.

Context-dependent lines (gravity, food, shield, falling) require supporting event information; choose a neutral line when facts are unavailable. Flavor must not replace or fabricate the death cause.

Acceptance: burst deaths, rolling-window boundaries, failed sends, disconnects, counter acknowledgements, hotkey rejection, true upload completion, muted audio, UI scaling and no notification storms.

## Milestone 4: first discovery and configurable special enemies

### Discoveries

- Host-controlled category switches for first biome and supported notable-location/trader discoveries.
- New DiscoveryWebhookURL: blank uses the main webhook; nonblank invalid URL reports an error rather than silently routing somewhere else. Host-only secret; no extra enable boolean is necessary.
- Verify Player.AddKnownBiome, Player.AddKnownLocationName and discovery RPC semantics in installed assemblies. Hook genuine newly discovered events, not save loading or repeated UI banners.
- Implement the selected character/world ledger scope with stable identifiers, versioned bounded persistence and atomic writes. On existing worlds, seed known history where valid; unknown history should be disclosed and must not generate a login burst.
- Avoid hardcoded English names; localize display separately from identity. Generic hooks may not cover every location mod; unsupported sources skip safely.
- Merge biome/location discoveries from the same brief encounter into one caption where appropriate. Personal first discovery remains personal even when other players know the area.

### Special enemies

- Host-owned enable switch, explicit prefab identifiers, optional carefully bounded wildcard patterns, first-only/repeat policy, cooldown and post-event duration.
- Clear config examples and an identifier inspection aid. Do not claim every enemy has a miniboss flag; none was found on current Character.
- Use confirmed credited kills; retain roster/final-blow distinctions where available. Deduplicate against a normal boss or qualifying loot event for the same death.
- Player-configurable means the person hosting/configuring their world can choose enemies. Joining clients cannot override host event policy.
- Use default delivery initially unless the host chooses an existing applicable route; no unsolicited new webhook category is required.

Acceptance: fresh/reloaded/existing characters, second world, repeated discoveries, simultaneous categories, localized names, absent mods, configured enemy matches, no nearby unrelated-player credit, and headless ownership.

## Milestone 5: shared event identity and multiplayer director

- Introduce structured event metadata and stable session-scoped event IDs. Correlate the same creature death/raid/discovery encounter; timestamp proximity alone is insufficient.
- Host groups incoming perspectives for a short bounded collection window. One recording per player per event; manual captures remain separate unless deliberately linked.
- Start with one Discord message containing multiple labeled WebP attachments, not a stitched cross-player movie. Keep Recorded by per attachment and distinguish shared facts from each character's first-time status.
- Default selection: deterministic primary perspective, then distinct eligible participants up to a configurable maximum (proposed 3). Do not claim visual quality ranking without a reliable scoring method.
- Have clients offer metadata and encoded sizes first; host accepts only perspectives that fit per-file, total-post, transfer and concurrency budgets. Larger individual tier allowance does not remove total network cost.
- If too large, use a small bounded encoding fallback ladder (quality then dimensions/frame sampling within configured floors), select fewer perspectives, or report omission. Never retry an unchanged oversized request indefinitely.
- Use disk-backed bounded transfers/queues where needed instead of multiplying large in-memory buffers. Timeout deadlines must reflect supported byte budgets and pacing.
- Use wait=true and retain returned message IDs/links; gallery entries point to the message rather than expiring attachment URLs. Send final per-clip success only for attachments actually included in a successful post.
- An omitted clip is not uploaded and cannot receive Memory Uploaded. Apply retention/recovery policy honestly.
- Keep host-owned destinations and authenticated connected-player names. Client metadata is still not anti-cheat proof.

Acceptance: two/three players, missing/late perspective, oversized attachments, separate simultaneous bosses, duplicate offers, aggregate budget, host disconnect, webhook rejection, omission status, correct per-client deletion and one grouped Discord post.

## Milestone 6: raid and barely-survived timelines

### Raid survival

- Inspect actual raid activation/deactivation and local participation semantics. A raid ending is not automatically a successful defense: timeout, cancellation, leaving, death and server shutdown need distinct treatment.
- Save a short opening panic segment and a short ending segment, then assemble one bounded animation. Retain only bounded compressed/intermediate segments between them, not the full raid in RAM or a continuous disk recording.
- Proposed final 10-second budget: 4 seconds of opening and 6 seconds of ending, configurable. If no legitimate ending is observed, expire the opening segment or retain it locally by policy.
- Default one combined upload after the raid. Optional separate start posting costs a second upload and must be explicit in the host setting.
- Scope perspectives to actual participants. Reuse the director's limits and grouping. A dedicated server coordinates but has no camera footage.
- Label unverified outcomes as raid ended rather than raid survived. Death and disconnect handling must be explicit.

### Barely survived

- Observe actual local damage transitions from above to at/below a percentage of current maximum health. Do not trigger on polling alone or health changes unrelated to damage.
- Proposed defaults: 5% threshold, 20% recovery threshold held for 10 seconds, and 120-second minimum cooldown. These are host-configurable proposed values, subject to playtesting.
- State progression: armed -> triggered/follow-up -> recovery required -> armed. Rearm requires both cooldown and sustained recovery. Poison oscillating 4%-6% cannot rearm.
- Accept the first crossing whether a large direct hit or a periodic tick; record known cause. A separate optional minimum damage fraction may narrow to huge hits, but must not be required by default.
- Starting below threshold does not immediately trigger. Repeated ticks while low do not schedule more clips. Handle max-health food changes, invulnerability, death, respawn and session changes.
- User example: source second around the threshold hit becomes 3 seconds of playback; following 20 source seconds become 7 seconds, for a 10-second output. Use the one second ending at the threshold hit as the slow segment, then the next 20 seconds as the fast follow-up. This makes survival validation exactly the 20 seconds after the hit and avoids overlapping frames.
- Use WebP frame timestamps for slowdown and selected-frame sampling for speedup. No game timescale changes and no promise of generated intermediate slow-motion frames. At 15 FPS, stretching one second to three gives visibly limited temporal detail.
- Sample the fast segment according to the output frame budget instead of storing all 20 seconds at full capture rate. Account for rolling history, selected frames, encoder limits and simultaneous normal events together.
- If the player dies during follow-up, apply the user's survival-policy answer. Recommended death priority releases the pending survival segment and uses the normal death capture with its cooldown rules.
- Add configurable source follow-up, playback duration, slow segment and cooldown with consistent validation. Preserve exact total output timing even when frames are missed.

Acceptance: direct hit, poison oscillation, sustained low health, recovery plus cooldown, food/max-health changes, death during follow-up, raid abort/end, missing segment, director grouping, exact decoded animation duration and measured capture/encoding performance.

## Milestone 7: personal gallery, Keep, and recovery

- In-game gallery with event, timestamp, thumbnail, recorder, local/pinned status, upload status, bytes, retry availability and Discord message link. Host secrets never appear.
- Player-editable GalleryKey, proposed default F8. Implement it with the gallery rather than exposing a nonfunctional key in earlier builds.
- Lazy-load previews and unload textures when closed; bounded index and thumbnail storage. Full animated local preview is available only while media exists; a deleted successful upload has a thumbnail/link, not a fictitious playable local copy.
- Keep this moment key/button pins the clip permanently. It must work during encoding, upload and a short post-completion grace period (proposed 30 seconds).
- Successful unpinned uploads may therefore remain temporarily until that grace period expires; explain the new cleanup timing clearly. Atomic pin/cleanup decisions prevent races.
- Separate permanent Saved clips, transient delivery/recovery files, and gallery metadata. Never auto-delete pinned clips under transient quota enforcement; surface disk-full failure honestly.
- Proposed transient recovery bounds: 20 clips, 250 MiB, 24-hour expiry; proposed gallery index 200 entries. Validate configurable limits and evict only mod-owned unpinned temporary files.
- Retry only while the original session/host authorization is valid. Never send one server's retained clip to another server or a newly local webhook. Cross-session retry needs an explicit future reauthorization design.
- Bounded retry count/backoff; honor Retry-After. Permanent invalid URL/auth/oversize errors need correction rather than repeated unchanged uploads.
- Ambiguous timeouts may already have posted: label delivery unknown and avoid automatic duplication. Webhook sends have no established generic idempotency guarantee. A deliberate user retry must explain possible duplication.
- Persist retry metadata without webhook credentials. Restart cleanup handles orphan partial files and crashes without touching historical unrelated files.

Acceptance: pin before/after upload, cleanup race, preview deletion, quota/expiry, disk full, crash/restart, expired Discord attachment link, known vs ambiguous failure, retry exhaustion and cross-server isolation.

## Implementation order and release gates

1. Configuration migration, host delivery policy and local file lifecycle.
2. Size presets and Configuration Manager presentation; measured upload guidance.
3. Notifications, sound and death rate limiting/personality.
4. Discoveries and configurable special enemies.
5. Shared event IDs, transfer budgets and director.
6. Segmented raid/close-call capture using the shared lifecycle.
7. Gallery UI and Keep/retry workflows; its underlying clip records/ownership should be introduced in milestone 1 so cleanup need not be rewritten twice.

Each milestone gets focused automated checks and an explicit live checklist. Run the existing full release/provenance checks before a distributable build, document changed settings, update both READMEs/changelog/version/manifest, and package a matching host/client version. Do not bundle all seven milestones into one untested release. Final live tests include single-player, listen host/client, dedicated owner, Epic Loot present/absent, scene transitions, and sustained performance.

The previously discussed achievement unlocks, crafting milestones, rescue detection, community challenges and burned-in cinematic overlays remain future ideas, not additions to this request.
