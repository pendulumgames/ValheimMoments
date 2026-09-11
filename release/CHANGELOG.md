# Changelog

## 0.17.0

- Personal raid moments capture a four-second opening and six-second ending, combined through the default host Discord route. Host-owned Raids.Enabled defaults true.
- Local random-event participation excludes forced boss ambience. Captions say Raid ended because timer expiry and administrative reset share ending semantics; no victory claim or multiplayer raid grouping.
- Raw opening frames are released after encoding. Owned intermediate cleanup waits for workers to close and preserves unrelated files. Pause, death, leaving, policy loss, session/capture changes and thirty-minute expiry abandon the attempt. Busy/missing endings are skipped.
- Composition enforces file, dimension, frame and timing budgets. Automated segment decode/timing, cancellation and cleanup checks pass. Live raid quality/performance remains pending. Restart orphan sweeping and gallery are still planned. Matching 0.17.0 host and clients required.

## 0.16.0

- Close-call clips require surviving twenty seconds after an actual damage crossing. Death cancels the attempt and frees capture for the normal death event.
- Bounded sampling produces a ten-second WebP: one source second slowed to three, then twenty source seconds compressed to seven. Existing memory fitting accounts for selected frames; no full twenty-second raw recording is retained.
- Host controls enable, health threshold, recovery threshold/hold and cooldown. Periodic oscillation cannot repeatedly rearm. Pausing, policy loss, session/character changes and reconfiguration abandon pending captures.
- Close calls remain personal and use the default host webhook. Matching 0.16.0 host/clients required. Automated timing/encoding and event checks pass; in-game acceptance remains pending. Raid clips and gallery are still planned.

## 0.15.0

- Multiplayer director groups compatible boss, special-enemy and kill-loot recordings into one post with up to three labeled perspectives. Shared owner-generated death IDs distinguish simultaneous kills; first-kill status remains personal.
- Host controls collection duration, perspective count and combined post budget. Offers reserve bounded capacity before selected client transfers start; local host footage uses the same selection path. Late, duplicate and over-budget perspectives are omitted without being marked uploaded.
- Disk-backed relay v2 uses bounded chunks and carries event metadata. Final outcomes distinguish uploaded, omitted, failed and unknown; originals are removed only after confirmed inclusion. Unknown delivery is not automatically retried.
- Uploader validates bounded Discord receipts and retains message identifiers. Stale callbacks and interrupted transfers cannot acknowledge a newer clip. Per-file relay/director cap remains 10 MiB; tier detection, automatic fallback encoding and gallery links remain future work.
- Matching 0.15.0 on host and recording clients is required. Automated three-player simulation passes; live co-op, notification/padding and earlier discovery acceptance checks remain pending. Nothing is installed automatically.

## 0.14.0

- First tracked biome, labeled-location and trader discoveries, once per character per world. Bounded local history uses atomic asynchronous saves; startup visits are silent and suppressed visits are remembered.
- Host-owned discovery category switches, cooldown, post-event duration and message. DiscoveryWebhookURL falls back to the main route only when blank.
- Configurable special-enemy captures using exact confirmed kill-credit keys, with a diagnostic key logger, first-kill option and per-enemy cooldown. Bosses remain exclusive; accepted special captures replace ordinary kill-loot captures and use the main route.
- Added discovery persistence, Harmony observation, routing and host-policy checks. Updated installer to preserve State during explicit folder migration and let the plugin generate current config defaults.
- Matching 0.14.0 host/client settings required. Preserve State/Discoveries across upgrades. Old per-world exploration cannot be reconstructed; live discovery/co-op acceptance remains pending.

## 0.13.0

- Brief themed recording, saved, captured and uploaded feedback. Optional original quiet cue with player-owned mode/volume; no audio is added to WebP files.
- Host-owned death quota defaults to one capture per 60 seconds. Per-player counters preserve intervening/unshared deaths until confirmed delivery or local-only save. Session changes clear counters.
- Twenty neutral cheeky death lines without immediate repeats. Custom captions opt in with {flavor}; {extra_deaths} places the additional-death count.
- Structured client relay completion only reports success after final host delivery acknowledgement. Host admission also limits incoming death offers per connection.
- Notification preferences apply without cancelling the current capture. Existing 0.12.0 settings, migration and sizing changes are included.
- Matching 0.13.0 host/client settings schema required. Live banner/audio and co-op acceptance remain pending.

## 0.12.0

- Six aspect-aware size presets plus Custom, preserving aspect under memory fitting and padding mismatched canvases.
- Player capture settings appear before visibly locked host policy; custom dimensions follow the picker.
- Discord defaults enabled for fresh configs. Relay/upload follow host enablement; redundant switches removed. Host Username remains Valheim Moments by default.
- Capture.SaveLocalCopy supports local-only saving and migrates the old Discord value. Both delivery and local saving off suppress captures.
- One boss CaptureMode supports first kill then qualifying rarity; all legacy combinations migrate. First boss kills receive an explicit caption line.
- Settings protocol v2 requires matching host/client versions. This milestone does not increase the 10 MiB relay cap.
- New UI and padded rendering require live testing; no gallery/discovery/director feature is included yet.

## 0.11.1 - complete boss kill credit

* Boss Kill Credit now lists all players in Valheim's credited-attacker records, instead of only the recording character. Nearby spectators are excluded.
* Recorded by, Final Blow and personal first-kill history remain independent. Missing roster metadata is explicitly marked unavailable rather than implying solo credit.
* Send the bounded roster alongside vanilla credit; attribution hooks also initialize on headless hosts.
* Updated player/source documentation and multiplayer acceptance checks. Added a Future Roadmap favoring game-driven progression events and host configuration over hardcoded content lists.
* Natural-loot testing was reported successful on 0.11.0. The new roster fix has automated coverage; final live co-op confirmation remains pending.
* Update the host and all recording clients to 0.11.1 for complete attribution metadata.

## 0.11.0 - natural loot acquisitions

* Added host-controlled natural chest and world pickup highlights, sharing loot rarity/display/routing settings.
* Track verified generation, including Epic Loot 0.14.2 deferred chest rolls; trigger on successful acquisition with Collected by attribution.
* Exclude player storage, gravestones, drops, crafted items, deposits, mixed stacks and unknown provenance. Consume eligibility once, including partial pickups.
* Group quick acquisitions for Take all summaries. Vanilla ordinary/world loot can qualify with MinimumRarity=None without Epic Loot.
* Provenance hooks also run on headless hosts; markers persist in ordinary item custom data.
* Host and recording clients must all update to 0.11.0. New natural-loot behavior has automated coverage; live validation remains pending.

## 0.10.1 - practical capture minimums

* Minimum capture dimensions are now 480 x 270; defaults remain 640 x 360.
* Memory fitting respects that floor and reduces effective FPS when necessary.
* Minimum frame-pool budget is 48 MiB so maximum clip durations fit at 1 FPS.

## 0.10.0 — host rules and bounded client settings

* Host controls event switches, rarity/first-kill rules, timing, attribution and post
  formatting. Connected clients see those options as read-only in Configuration Manager.
* Clients keep hotkeys, capture/relay opt-out, resolution/FPS/quality, memory budget,
  image flip, local-copy retention and performance diagnostics editable.
* Host policies sync over the connected server's direct RPC channel. Webhook URLs and
  Discord identity never enter the packet; client config files retain local preferences.
* Host and clients need 0.10.0 or compatible newer versions. Capture waits for host
  settings; outdated clients without the settings exchange cannot relay clips.
* Added UI/config-file ranges, aspect-ratio limits (1:2 through 3:1), and automatic
  resolution reduction to fit memory limits. Defaults remain 640x360 at 15 FPS.
* Debug timing is under Configuration Manager's Advanced filter and defaults off.
* Automated policy and bounds checks pass; live settings UI/co-op checks remain pending.

## 0.9.5 — compact posts and successful-upload cleanup

* Removed the extra blank line before automatically appended generated loot.
* SaveLocalCopy now defaults to false: successful uploads delete the local clip.
  Joining clients also delete after the host confirms successful Discord delivery.
* Existing installs keep their setting; use SaveLocalCopy=false to enable cleanup.
  Failed/skipped uploads and older saved files remain local. No session-wide purge.

## 0.9.4 — loot formatting

* Avoid repeating rarity words already at the beginning of item names, including
  Magic and Legendary. Matching uses the localized rarity and whole-word boundaries.
* Format each modifier and socket detail with two spaces followed by `* -# `.
* Applies to both boss and ordinary-loot summaries; hidden unidentified details remain hidden.

## 0.9.3 — capture session isolation

* Clear buffered footage and cancel a collecting clip when joining, leaving or
  switching sessions. Pending GPU readbacks from the old session are discarded.
* Keep frames owned by an active encoder intact until it finishes; existing upload
  guards continue to prevent sending the old clip through a different session.
* Require an active session and local player before recording or triggering a new clip.
* Added automated tests for session changes, late readbacks and encoder ownership.

## 0.9.2 — periodic final-blow attribution

* Track actual Spirit/fire/poison status-effect sources on the boss owner, without
  changing game damage or kill credit. A tick from one known player can now supply
  the final-blow name; mixed or unknown sources remain unavailable.
* Respect fire/Spirit stacking and poison replacement instead of assuming the last
  direct attacker caused the periodic kill.
* Added Boss Kill.TrackPeriodicDamage (default true, restart required).
* Added automated status-effect attribution checks. Spirit-damage final-blow naming
  passed a live boss-kill test. Fire/poison and remaining automatic co-op checks are pending.

## 0.9.1 — recorder attribution for everyone

* Added **Recorded by:** to host and solo posts for all clip types, using the
  recording character's name at the time of the trigger.
* Joining-player posts retain host-supplied recorder attribution with the same formatting.
* Confirmed live co-op F10 Discord delivery for both host and joining player, and
  visual playback of the WebP-only encoder's clips.

## 0.9.0 — consistent naming

* Renamed source projects, namespaces, plugin DLL, encoder executable and install
  folder to ValheimMoments.
* Plugin GUID/config identifier is now local.valheimmoments. Upgrading requires
  migrating the previous config and removing the older active plugin.
* Added explicit development-installer migration options that preserve settings,
  recordings and the previous plugin outside the active profile.
* Added the project's MIT license and public GitHub source link.

## 0.8.1 — first Thunderstore beta

* Manual, death, boss and ordinary-loot animated WebP captures.
* Optional Epic Loot details, rarity filtering and first-kill priority.
* Markdown post formatting and independent loot display settings.
* Host-owned Discord destinations and bot name, with bounded client clip relay.
* New 256×256 Valheim Moments icon and mod-manager packaging.
* Replaced the prototype's ImageMagick bundle with a smaller WebP-only encoder.
* Multiplayer relay and the new encoder still need fresh live-game validation.
