using System;
using System.Collections.Generic;
using HarmonyLib;
using ValheimMoments;

internal static class DirectorTests
{
    private static int checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    private static string Id(int value) { return value.ToString("x32"); }
    private static HighlightOffer Offer(int clip, int occurrence, long peer, long bytes = 4, string kind = "boss", bool first = false)
    { return new HighlightOffer(Id(clip), occurrence == 0 ? null : Id(occurrence), kind, peer, "Viking " + peer, bytes, first); }
    internal static void Run()
    {
        var session = new object();
        var director = new HighlightDirector(3, 10, 10, 2);
        director.Reset(session);
        Check(director.Offer(new object(), Offer(1, 100, 1), 0) == HighlightAdmission.WrongSession, "Old host session rejected");
        Check(director.Offer(session, Offer(1, 100, 3, 4, first: true), 0) == HighlightAdmission.Accepted, "First perspective offered");
        Check(director.Offer(session, Offer(2, 100, 1), 0) == HighlightAdmission.Accepted, "Second recorder same occurrence");
        Check(director.Offer(session, Offer(3, 100, 2), 0) == HighlightAdmission.Accepted, "Third recorder same occurrence");
        Check(director.Offer(session, Offer(4, 100, 1), 1) == HighlightAdmission.Duplicate, "One perspective per peer per event");
        Check(director.Offer(session, Offer(1, 101, 3), 1) == HighlightAdmission.Duplicate, "Same pending clip cannot enter two events");
        Check(director.Drain(session, 1.999).Length == 0, "Bounded window waits");
        Check(director.Offer(session, Offer(5, 100, 5), 2) == HighlightAdmission.Closed, "Late offer cannot extend window before drain");
        var result = director.Drain(session, 2);
        Check(result.Length == 1 && result[0].Selected.Length == 2 && result[0].Omitted.Length == 1, "Aggregate limit selects fewer perspectives");
        Check(result[0].Selected[0].PeerId == 1 && result[0].Selected[1].PeerId == 2 && result[0].Omitted[0].PeerId == 3, "Deterministic primary independent of arrival order");
        Check(result[0].Omitted[0].PersonalFirst && !result[0].Selected[0].PersonalFirst, "Personal first status never becomes shared fact");
        Check(director.Drain(session, 2).Length == 0, "Selection delivered once");
        Check(director.Offer(session, Offer(6, 100, 6), 3) == HighlightAdmission.Closed, "Recently closed event cannot repost with late perspective");
        Check(director.Offer(session, Offer(7, 101, 1, 11), 3) == HighlightAdmission.Oversized, "Oversize rejected before transfer");
        Check(director.Offer(session, Offer(7, 101, 1), double.NaN) == HighlightAdmission.Invalid, "NaN clock rejected");
        Check(director.Offer(session, Offer(7, 101, 1), 2) == HighlightAdmission.Invalid, "Clock rollback rejected");
        director.Reset(session);
        director.Offer(session, Offer(1, 100, 1), 0); director.Offer(session, Offer(2, 101, 2), 0);
        Check(director.Drain(session, 2).Length == 2, "Simultaneous identical enemy deaths stay separate by ID");
        foreach (string kind in new[] { "manual", "discovery", "death" })
        {
            director.Reset(session);
            director.Offer(session, Offer(1, 100, 1, kind: kind), 0); director.Offer(session, Offer(2, 100, 2, kind: kind), 0);
            Check(director.Drain(session, 2).Length == 2, "Personal category never merges from claimed shared ID: " + kind);
        }
        director.Reset(session);
        director.Offer(session, Offer(1, 0, 1), 0); director.Offer(session, Offer(2, 0, 2), 0);
        Check(director.Drain(session, 2).Length == 2, "Missing owner identity stays independent");
        director.Reset(session);
        for (int i = 1; i <= 8; i++) Check(director.Offer(session, Offer(i, 100, i), 0) == HighlightAdmission.Accepted, "Bounded offer slot " + i);
        Check(director.Offer(session, Offer(9, 100, 9), 0) == HighlightAdmission.Capacity, "Per-event offer capacity");
        director.Reset(session);
        for (int i = 1; i <= 8; i++) Check(director.Offer(session, Offer(i, 100 + i, i), 0) == HighlightAdmission.Accepted, "Bounded group slot " + i);
        Check(director.Offer(session, Offer(9, 109, 9), 0) == HighlightAdmission.Capacity, "Pending group capacity");
        director.Reset(new object());
        Check(director.Drain(session, 2).Length == 0, "World switch discards pending selections");
        Check(!HighlightDirector.ValidId(new string('0', 32)) && !HighlightDirector.ValidId("../path") && !HighlightDirector.ValidId(new string('G', 32)), "Event IDs constrained");
        foreach (var order in new[] { new[] { 1, 2, 3 }, new[] { 3, 2, 1 }, new[] { 2, 3, 1 } })
        {
            var generous = new HighlightDirector(3, 10, 30, 2); generous.Reset(session);
            foreach (int peer in order) generous.Offer(session, Offer(peer, 100, peer), 0);
            var picked = generous.Drain(session, 2)[0];
            Check(picked.Selected.Length == 3 && picked.Selected[0].PeerId == 1 && picked.Selected[2].PeerId == 3 && picked.Omitted.Length == 0, "Three perspectives stable across arrival permutations");
        }
        var fitting = new HighlightDirector(3, 10, 10, 2); fitting.Reset(session);
        fitting.Offer(session, Offer(1, 100, 1, 6), 0); fitting.Offer(session, Offer(2, 100, 2, 5), 0); fitting.Offer(session, Offer(3, 100, 3, 4), 0);
        var fit = fitting.Drain(session, 2)[0];
        Check(fit.Selected.Length == 2 && fit.Selected[1].PeerId == 3 && fit.Omitted[0].PeerId == 2, "Oversized second perspective does not block smaller third");
        var overflow = new HighlightDirector(3, long.MaxValue, long.MaxValue, 2); overflow.Reset(session);
        overflow.Offer(session, Offer(1, 100, 1, long.MaxValue), 0); overflow.Offer(session, Offer(2, 100, 2, long.MaxValue), 0);
        Check(overflow.Drain(session, 2)[0].Selected.Length == 1, "Combined-byte accounting cannot overflow");
        var history = new HighlightDirector(3, 10, 10, 0.5); history.Reset(session);
        for (int i = 1; i <= HighlightDirector.MaximumRecent + 1; i++)
        { history.Offer(session, Offer(i, i, 1), i); history.Drain(session, i + 0.5); }
        Check(history.Offer(session, Offer(999, 1, 1), 258) == HighlightAdmission.Accepted, "Recent dedup storage evicts oldest at documented bound");
        Check(history.Offer(session, Offer(1000, 257, 2), 258) == HighlightAdmission.Closed, "Recent dedup retains newest entries");
        Check(history.Offer(session, Offer(1001, 257, 2), 2058) == HighlightAdmission.Accepted, "Recent dedup expires after bounded lifetime");

        var harmony = new Harmony("valheimmoments.director.tests");
        BossAttribution.Install(harmony); BossKillDetector.Install(harmony);
        var recorded = new List<BossKill>();
        BossKillDetector.OnKill = recorded.Add; BossKillDetector.OnLootKill = recorded.Add;
        BossKillDetector.ObserveOrdinary = () => true;
        try
        {
            new ZRoutedRpc();
            var game = new Game();
            var victim = new Character { Boss = true, m_name = "boss", Owner = true };
            victim.DeathAction = () => {
                string local = BossAttribution.TakeEvent(0, "boss");
                Check(HighlightDirector.ValidId(local), "Death owner creates identity inside exact death scope");
                foreach (long peer in new long[] { 10, 20 })
                {
                    game.RegisterKill(peer, "boss", 1, KillModifiers.None, 2, false);
                    game.RPC_RegisterKill(42, "boss", 1, 0, 2, false);
                    Check(recorded[recorded.Count - 1].EventId == local, "Each credited recipient gets same owner occurrence");
                }
            };
            victim.OnDeath(); string first = recorded[0].EventId;
            Check(recorded.Count == 2 && recorded[0].EventId == recorded[1].EventId, "Same kill grouped across recipients");
            victim.OnDeath();
            Check(recorded[2].EventId != first, "Second same-name death receives different identity");
            Check(BossAttribution.TakeEvent(0, "boss") == null && BossAttribution.TakeEvent(42, "boss") == null, "Death context restored and remote ID consumed");
            victim.Boss = false;
            victim.DeathAction = () => { game.RegisterKill(10, "boss", 0, KillModifiers.None, 1, false); game.RPC_RegisterKill(42, "boss", 0, 0, 1, false); };
            victim.OnDeath();
            Check(HighlightDirector.ValidId(recorded[recorded.Count - 1].EventId) && recorded[recorded.Count - 1].BossNumber == 0, "Ordinary kill carries ID for special and loot highlights");
            BossKillDetector.ObserveOrdinary = () => false;
            victim.OnDeath();
            Check(BossAttribution.TakeEvent(42, "boss") == null, "Disabled ordinary observer still drains event metadata");
            victim.Boss = true;
            string outer = null;
            var nested = new Character { Boss = true, Owner = true, m_name = "other" };
            nested.DeathAction = () => Check(BossAttribution.TakeEvent(0, "other") != outer && BossAttribution.TakeEvent(0, "boss") == null, "Nested death gets independent context");
            victim.DeathAction = () => { outer = BossAttribution.TakeEvent(0, "boss"); nested.OnDeath(); Check(BossAttribution.TakeEvent(0, "boss") == outer, "Outer death ID restored after nested death"); };
            victim.OnDeath();
            victim.DeathAction = () => { throw new InvalidOperationException("test"); };
            try { victim.OnDeath(); } catch (InvalidOperationException) { }
            Check(BossAttribution.TakeEvent(0, "boss") == null, "Exception restores identity context");
            victim.Owner = false;
            victim.DeathAction = () => Check(BossAttribution.TakeEvent(0, "boss") == null, "Non-owner cannot author death ID");
            victim.OnDeath();
        }
        finally
        {
            harmony.UnpatchSelf(); BossAttribution.Clear(); BossKillDetector.OnKill = null;
            BossKillDetector.OnLootKill = null; BossKillDetector.ObserveOrdinary = null;
        }
        Console.WriteLine("PASS: " + checks + " director admission, selection and owner-identity assertions.");
    }
}
