# Announcement persistence review (0.23.0)

Discovery records live on each recording client, per character/world, in BepInEx/config/ValheimMoments/Discoveries. Main biomes, distinct named sub-biomes, traders and named locations each have their own record. Discoveries.SubBiomes is host-controlled and defaults off; the record is updated before eligibility checks, so disabled visits remain silent if enabled later. Main biome observations remain independent of the sub-biome toggle. A sub-biome identity uses the game's untranslated name override/prefix/suffix rules, not hidden decoration modifiers or localized display text. Existing plugin-folder journals migrate when loaded; exact legacy variant matches seed new identities silently. Legacy keys are retained because their names cannot be reconstructed safely without the original biome definitions. Preserve old journals through the first upgraded launch. Missing/deleted records cannot be recovered from Discord by this implementation.

| Announcement | Persistence / replay behavior |
| --- | --- |
| Boss and special enemy first kills | Read Valheim's saved character kill statistics. Requires a new credited kill; reconnecting does not generate one. AllKills and rarity modes intentionally permit later qualifying kills. |
| Ordinary kill loot | Requires a new credited kill and observed qualifying loot. Pending batches are cleared on session change. |
| Natural chest/world loot | Item provenance uses saved custom data; consumed on acquisition. Inventory loading is explicitly excluded. Regenerated loot can legitimately qualify again. |
| Player deaths | Requires an actual death hook. Rate window and suppressed-death counters reset on session change; this permits a new death sooner but does not replay an old death. |
| Close calls | Requires a new damage threshold crossing and survival. Pending attempts/cooldown reset on session change; low health on login alone cannot trigger. |
| Raids | Requires observed raid participation and ending footage. Session changes abandon pending footage. Rejoining an ongoing raid can start a fresh participation recording; a later ending is not treated as a once-ever event. |
| Experimental arrival cameras | Fresh-spawn timestamps exclude loaded old creatures. Cached optional footage is session-local and is not automatically reposted on reconnect. |
| Gallery / delivery | Saved history does not automatically retry Discord posts after restart. Retries require an explicit action and existing session/authority checks; unknown delivery requires confirmation. |

No analogous automatic replay path was found in this code review outside discovery identity/history handling. This is not live server-restart verification. Character/world save rollback or a mod regenerating an item/creature can still create apparently repeated events. Discovery writes run asynchronously, but announcements wait for successful durable commit. Failed writes and interrupted uncommitted observations cannot create already-posted discoveries without history. Run Preserve-DiscoveryHistory.ps1 before replacing a legacy plugin folder; the manual installer does this automatically.
