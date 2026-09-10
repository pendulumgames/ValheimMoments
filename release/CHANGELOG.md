# Changelog

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
