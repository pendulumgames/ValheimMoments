# Changelog

## 0.9.2 — periodic final-blow attribution

* Track actual Spirit/fire/poison status-effect sources on the boss owner, without
  changing game damage or kill credit. A tick from one known player can now supply
  the final-blow name; mixed or unknown sources remain unavailable.
* Respect fire/Spirit stacking and poison replacement instead of assuming the last
  direct attacker caused the periodic kill.
* Added Boss Kill.TrackPeriodicDamage (default true, restart required).
* Added automated status-effect attribution checks. Live periodic-kill and remaining
  automatic co-op checks are deferred to final testing.

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
