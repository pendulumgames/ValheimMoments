# Natural loot acceptance — 0.14.0

Automated provenance/transfer tests pass, and the user confirmed the broader loot
feature works in 0.11.0. Individual edge cases below still require explicit confirmation.
Install 0.14.0 on the host and every recording client. Use a separate test profile.
The source owner needs this version when loot is generated; an update cannot prove
the origin of old chest contents. Use newly explored treasure or a fresh test world.

On the host, enable Triggers.LootDrop and Loot Capture.Enabled. Leave
CaptureChestPickups and CaptureWorldPickups enabled. Temporarily set MinimumRarity
to None for vanilla items; restore the desired rarity after testing. Keep the usual
recording warm-up of at least five seconds and wait for one clip to finish before
the next case. New pickup clips share the Good Loot channel configuration.

## First single-player checks

1. Open a newly generated natural chest. Opening alone should not capture.
2. Take an item. Expect a loot clip with **Recorded by:** and **Collected by:**,
   the actual acquired quantity and the normal item formatting. No kill credit.
3. Use Take all in another fresh chest. Quick transfers should share one summary;
   the post-event recording should show the pickup/inventory view.
4. Drop the acquired item and pick it up again, then store/retrieve it in a player
   chest. Neither should capture. Depositing/retrieving it in a dungeon chest should
   not capture either. Recovering a gravestone should not capture.
5. Acquire natural pickable loot (including a loose PickableItem) and loot from a
   natural breakable using DropOnDestroyed. Each should qualify at None. Player-built
   refunds and planted crops should not. Arbitrary unknown mod sources are skipped.
6. With Epic Loot 0.14.2, acquire a newly rolled chest item at the configured rarity.
   Confirm item rarity, modifiers, sockets and unidentified-item privacy. Set the
   threshold above a second item's rarity and verify that acquisition does not post.
7. Kill a mob or boss and then pick up its drops. The existing kill rules should
   work, with no additional natural-pickup clip for the same drops.

## Transfer and persistence checks

1. With no inventory space, try a qualifying transfer: no clip. Free a slot and
   retry: one clip. For a partial stack acquisition, report only the amount actually
   added; picking up the remainder should not make another clip.
2. Leave tracked natural treasure unclaimed, save/reload, then acquire it: one clip.
   Reloading the player inventory itself must never capture. Repeat with world loot
   across an unload/reload and after switching the owning peer.
3. Mix player-deposited items into a tagged chest stack or let ground stacks combine.
   The resulting ambiguous stack should not produce a natural-loot highlight.
4. Disable capture, acquire tracked loot, then re-enable and drop/retrieve it: no
   delayed replay. Repeat while another clip is busy; extra events remain skipped.
5. Toggle each new source setting independently on the host. Clients should follow
   the host's setting, display it read-only, and keep their own hotkeys editable.

## Co-op and dedicated hosting

1. Have the host generate a new chest, then let a joining client collect its loot.
   Confirm the client's clip reaches the host-selected Good Loot destination and
   identifies that client as collector and recorder.
2. Reverse host/client generation and collection. Repeat with a ground pickable.
3. Use a Windows dedicated host with matching versions. Verify provenance generation,
   settings sync and Discord delivery without graphics capture on the server.
4. Verify successful uploads delete new clips with SaveLocalCopy=false on both host
   and client; failed delivery should retain the original.

## Remaining release acceptance

This feature does not by itself establish completion. Final acceptance also includes
the [co-op/settings/routing checklist](RELAY-TEST.md), repeated session transitions,
periodic final-blow edge cases, and a sustained play session checking memory, frametime,
encoder behavior and output size. Earlier stationary FPS observations and synthetic
encoder measurements are useful baselines, not a long-session performance result.
Thunderstore moderation/public availability remains a separate release step.
