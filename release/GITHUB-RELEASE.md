# Valheim Moments 0.23.1

Barely Survived now stays in slow motion through the immediate aftermath of the hit. With default settings, impact lands at playback second two and slow motion continues through second three; the complete clip remains ten seconds.

- Discovery history now lives outside the replaceable plugin folder and migrates existing records. Main biomes and distinct named sub-biomes each retain their own saved discovery identity, ignoring hidden modifiers and display language changes. Traders and named locations use the same persistent journal. The host can enable Discoveries.SubBiomes for named sub-biome announcements; it defaults off and does not replay visits recorded while disabled.
- Adds personal RenderCinematics, local LogEnemyKeys, and optional DiscordUserID mentions beside character names in recorder/kill-credit labels. Obsolete FirstKillOnly and PlayerNameOverride controls are removed.
- Discovery announcements wait for durable storage. Update normally through your mod manager; no scripts or manual history migration are required. After upgrading from an older version, you may see discoveries one more time if the updater removed their old history. The mod now saves discoveries outside the plugin folder, so recorded discoveries will no longer repeat when the server restarts. This applies to biomes, enabled sub-biomes, traders and named locations for each character and world.
- Special enemies now have independent boss-equivalent capture modes, rarity filtering, loot display, player attribution and Discord routing. Explicit CaptureMode choices remain intact.
- Special enemy experimental cameras support separate arrival/aftermath toggles, movement selection and spawn delay.
- Biome discovery cameras offer RiseAndReveal, Orbit and ZoomIn, with an independent default-on BiomeLetterbox toggle for animated black bars and a fading biome title.
- Includes corrected GitHub and Thunderstore READMEs with Mec/Ren animated gameplay examples.
- Preserves the existing 3x camera distance cap, wider obstruction checks, boss spawn timing and boss/raid movement options.

Install matching **0.23.1** on the host and every recording client. Experimental event switches remain off by default. Enable BiomeDiscovery for biome camera footage; BiomeLetterbox controls its bars independently. Normal player footage remains the fallback when optional cinematic capture cannot complete.

Validation: automated capture/event/relay tests, cinematic presentation tests and encoder/package checks pass. The separate Configuration Manager contract check could not run because that mod is absent from the configured test profile. Live camera framing, modded summon animations and co-op acceptance remain pending.

Release assets:

- Valheim_Moments-0.23.1.zip: Thunderstore-ready package.
- ValheimMoments-0.23.1-test.zip: manual/test package with configuration and acceptance documentation.

The README example images are included in the source repository.
