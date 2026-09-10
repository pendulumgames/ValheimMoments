# Changelog

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
