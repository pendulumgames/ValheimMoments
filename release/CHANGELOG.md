# Changelog

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
