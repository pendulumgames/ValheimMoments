using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ValheimMoments;
using ValheimMoments.Core;

public class RandomEvent { public string Name; }
public class RandEventSystem
{
    public RandomEvent Current, Active;
    public RandomEvent GetCurrentRandomEvent() { return Current; }
    public RandomEvent GetActiveEvent() { return Active; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetActiveEvent(RandomEvent value, bool end)
    {
        // Inspected vanilla behavior: same-name active replacement returns early.
        if (value != null && Active != null && value.Name == Active.Name) return;
        Active = value;
    }
}
internal static class RaidTests
{
    private static int checks;
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    internal static void Run()
    {
        var session = new object(); var raid = new object(); var policy = new RaidMoment(60);
        policy.Reset(session);
        Check(policy.Enter(session, raid, 0, true, true) == RaidMomentResult.Started, "Actual participation starts");
        Check(policy.Enter(session, raid, 1, true, true) == RaidMomentResult.None, "Duplicate activation ignored");
        Check(policy.Observe(session, 2, true, false, true) == RaidMomentResult.Abandoned, "Leaving releases opening");
        Check(policy.Enter(session, raid, 3, true, true) == RaidMomentResult.None, "Reentry cannot replay abandoned opening");
        Check(policy.End(session, raid, 4, true) == RaidMomentResult.None, "End after leaving excluded");
        raid = new object();
        Check(policy.Enter(session, raid, 5, true, false) == RaidMomentResult.None, "Busy start skipped");
        Check(policy.Enter(session, raid, 6, true, true) == RaidMomentResult.None, "Busy start not replayed later");
        raid = new object(); policy.Enter(session, raid, 7, true, true);
        Check(policy.End(session, new object(), 8, true) == RaidMomentResult.None && policy.Pending, "Unrelated occurrence cannot complete");
        Check(policy.End(session, raid, 9, true) == RaidMomentResult.Ended, "Observed end is not victory");
        Check(policy.End(session, raid, 10, true) == RaidMomentResult.None, "One end only");
        raid = new object(); policy.Enter(session, raid, 11, true, true);
        Check(policy.End(session, raid, 12, false) == RaidMomentResult.Abandoned, "Death wins at end");
        raid = new object(); policy.Enter(session, raid, 13, true, true);
        Check(policy.Observe(session, 73, true, true, true) == RaidMomentResult.Abandoned, "Bounded lifetime expires at boundary");
        raid = new object(); policy.Enter(session, raid, 74, true, true);
        Check(policy.Observe(new object(), 75, true, true, true) == RaidMomentResult.Abandoned, "World change abandons");
        policy.Reset(session); raid = new object(); policy.Enter(session, raid, 0, true, true);
        Check(policy.Observe(session, 1, true, true, false) == RaidMomentResult.Abandoned, "Policy cancellation");

        var harmony = new Harmony("moments.tests.raids");
        var system = new RandEventSystem(); var first = new RandomEvent { Name = "raid" };
        var transitions = new List<RaidTransition>(); int errors = 0;
        RaidDetector.OnTransition = (r, t) => transitions.Add(t);
        RaidDetector.OnError = () => errors++;
        Player.m_localPlayer = new Player(); RaidDetector.Install(harmony);
        try
        {
            system.Current = first; system.SetActiveEvent(first, false);
            Check(transitions.Count == 1 && transitions[0] == RaidTransition.Entered, "Local random event entry");
            system.SetActiveEvent(first, false);
            Check(transitions.Count == 1, "Repeated game activation ignored");
            system.SetActiveEvent(new RandomEvent { Name = "raid" }, false);
            Check(transitions.Count == 1, "Skipped same-name original does not fabricate transition");
            system.SetActiveEvent(null, false);
            Check(transitions.Count == 2 && transitions[1] == RaidTransition.Left, "Area leave distinguished");
            system.SetActiveEvent(first, false); system.SetActiveEvent(null, true);
            Check(transitions.Count == 4 && transitions[3] == RaidTransition.Ended, "Reset or timeout reports only ended");
            system.SetActiveEvent(new RandomEvent { Name = "boss_music" }, false);
            Check(transitions.Count == 4, "Forced boss ambience excluded");
            system.SetActiveEvent(null, false); Player.m_localPlayer = null;
            system.SetActiveEvent(first, false);
            Check(transitions.Count == 4, "Headless server has no local participation");
            Player.m_localPlayer = new Player(); system.Active = null;
            RaidDetector.OnTransition = (r, t) => { throw new Exception("observer"); };
            system.SetActiveEvent(first, false);
            Check(ReferenceEquals(system.Active, first) && errors == 1, "Observer failure cannot break raid activation");
        }
        finally { harmony.UnpatchSelf(); RaidDetector.OnTransition = null; RaidDetector.OnError = null; Player.m_localPlayer = null; }
        Console.WriteLine("Raid policy/observer: " + checks + " assertions passed.");
    }
}
