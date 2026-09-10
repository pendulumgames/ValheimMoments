using System;
using ValheimEventClips;
internal static class FilterTests
{
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    internal static void Run()
    {
        int rank;
        Check(BossLootFilter.TryRank("None", out rank) && rank == -1, "None supported without Epic Loot");
        Check(!BossLootFilter.TryRank("Legendary", out rank), "Missing Epic Loot cannot infer ranks");
        BossLootFilter.SetRarities(typeof(EpicLoot.ItemRarity));
        Check(BossLootFilter.TryRank(" legendary ", out rank) && rank == 3, "Actual enum rank with case/whitespace handling");
        Check(!BossLootFilter.TryRank("Typo", out rank), "Unknown rarity fails closed");
        Check(BossLootFilter.TryRank("Ancient", out rank) && rank == 5, "New Ancient tier discovered");
        Check(BossLootFilter.Decide(null, -1, false) == LootDecision.Accept, "None accepts regardless of loot availability");
        Check(BossLootFilter.Decide(null, 3, false) == LootDecision.Wait, "Missing metadata waits bounded deadline");
        Check(BossLootFilter.Decide(null, 3, true) == LootDecision.Reject, "Missing metadata rejects at deadline");
        var loot = new BossLoot { Observed = true, Pending = true };
        loot.Add("Antler", "Antler", 3);
        loot.AddEpic(new LootItem { Id = "Sword", Name = "Sword", Quantity = 1, Rank = 2 });
        Check(BossLootFilter.Decide(loot, 3, false) == LootDecision.Wait, "Vanilla and lower rarity wait for pending loot");
        Check(BossLootFilter.Decide(loot, 3, true) == LootDecision.Reject, "Unfinished lower rarity rejects at deadline");
        loot.Pending = false;
        Check(BossLootFilter.Decide(loot, 3, false) == LootDecision.Reject, "Completed lower rarity rejects immediately");
        loot.AddEpic(new LootItem { Id = "Axe", Name = "Axe", Quantity = 1, Rank = 3 });
        Check(BossLootFilter.Decide(loot, 3, false) == LootDecision.Accept, "One qualifying item enough despite lower items");
        Check(BossLootFilter.Decide(loot, 2, false) == LootDecision.Accept, "Higher than threshold qualifies");
        Check(BossLootFilter.Decide(loot, 5, false) == LootDecision.Reject, "Below Ancient does not qualify");
        loot.Pending = true;
        Check(BossLootFilter.Decide(loot, 3, false) == LootDecision.Accept, "Known qualifying item can proceed while other drops pending");
        string reason;
        var epicOnly = new BossLoot { Observed = true };
        epicOnly.AddEpic(new LootItem { Id = "Sword", Name = "Sword", Quantity = 1, Rank = 2 });
        Check(BossLootFilter.Evaluate(epicOnly, false, false, true, "Legendary", false, out reason) == LootDecision.Accept, "Disabled filter ignores configured minimum (reported scenario)");
        Check(BossLootFilter.Evaluate(epicOnly, true, false, true, "Legendary", false, out reason) == LootDecision.Reject, "Repeat Epic-only kill rejected when Legendary filter enabled");
        Check(BossLootFilter.Evaluate(epicOnly, true, true, true, "Legendary", false, out reason) == LootDecision.Accept, "First kill outranks rarity when configured");
        Check(BossLootFilter.Evaluate(epicOnly, true, true, false, "Legendary", false, out reason) == LootDecision.Reject, "First kill respects rarity when bypass disabled");
        Check(BossLootFilter.Evaluate(null, true, true, true, "Unknown", false, out reason) == LootDecision.Accept, "First kill priority independent of missing loot or rarity API");
        Check(BossLootFilter.Evaluate(null, true, false, true, "Unknown", true, out reason) == LootDecision.Reject, "First kill bypass never admits repeat unknown loot");
        var highlights = new LootHighlights();
        int accepted = 0;
        Action<BossKill> accept = k => accepted++;
        highlights.Add(new BossKill { BossNumber = 1, Loot = loot }, 0, 12);
        Check(highlights.Count == 0, "Bosses excluded from ordinary loot route");
        var candidate = new BossKill { Loot = new BossLoot { Observed = true, Pending = true } };
        highlights.Add(candidate, 0, 12); highlights.Add(candidate, 0, 12);
        highlights.Poll(1, "Legendary", accept);
        Check(highlights.Count == 1 && accepted == 0, "Pending drop retains metadata once without capture");
        candidate.Loot.AddEpic(new LootItem { Quantity = 1, Rank = 3, Name = "Sword", Id = "Sword" });
        highlights.Poll(2, "Legendary", accept); highlights.Poll(3, "Legendary", accept);
        Check(accepted == 1 && highlights.Count == 0, "Qualifying delayed drop triggers once");
        highlights.Add(new BossKill { Loot = epicOnly }, 0, 12);
        highlights.Poll(1, "Legendary", accept);
        Check(accepted == 1 && highlights.Count == 0, "Lower rarity discarded without capture");
        highlights.Add(new BossKill { Loot = candidate.Loot }, 0, 1);
        highlights.Poll(2, "Legendary", accept);
        Check(accepted == 1 && highlights.Count == 0, "Expired qualifying metadata never creates stale highlight");
        highlights.Add(new BossKill { Loot = new BossLoot { Observed = true } }, 0, 12);
        highlights.Poll(1, "None", accept);
        Check(accepted == 1 && highlights.Count == 0, "None still excludes empty drops");
        highlights.Add(new BossKill { Loot = candidate.Loot }, 0, 12);
        highlights.Poll(1, "Typo", accept);
        Check(accepted == 1 && highlights.Count == 0, "Invalid highlight threshold fails closed");
        for (int i = 0; i < 65; i++) highlights.Add(new BossKill(), 0, 12);
        Check(highlights.Count == 64, "Highlight metadata bounded");
        highlights.Poll(13, "Legendary", accept);
        Check(highlights.Count == 0, "Missing co-op loot expires");
        Check(EventMessages.Loot("{enemy} / {player} / {loot} / {item_count}", "Troll", "Ragnar", "Sword", "1") == "Troll / Ragnar / Sword / 1", "Independent loot message placeholders");
        Console.WriteLine("PASS: " + checks + " boss rarity filter assertions.");
    }
}
