using System;
using System.Collections.Generic;
using ValheimMoments;

internal static class DeathMomentTests
{
    private static int checks;
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
    internal static void Run()
    {
        var deaths = new DeathMoments();
        Check(deaths.Reserve() == null, "No invented death before observation");
        deaths.Observe(); var first = deaths.Reserve();
        Check(first.Additional == 0 && deaths.TakeSlot(0, 1, 60), "First death may capture immediately");
        deaths.Observe(); deaths.Observe();
        Check(deaths.Reserve() == null, "Pending death blocks overlapping claim on the same counters");
        deaths.Complete(first, true);
        var suppressed = deaths.Reserve();
        Check(suppressed.Additional == 1 && !deaths.TakeSlot(59.999, 1, 60), "Deaths during pending upload survive acknowledgement; sliding window enforced");
        deaths.Complete(suppressed, false);
        deaths.Observe(); var next = deaths.Reserve();
        Check(next.Additional == 2 && deaths.TakeSlot(60, 1, 60), "Next eligible death summarizes intervening deaths at exact window boundary");
        deaths.Complete(first, true);
        Check(deaths.Reserve() == null, "Duplicate old acknowledgement cannot release current reservation");
        deaths.Complete(next, false);
        deaths.Observe(); var retry = deaths.Reserve();
        Check(retry.Additional == 3, "Failed upload does not lose unreported deaths");
        deaths.Clear(); deaths.Observe(); var fresh = deaths.Reserve();
        deaths.Complete(retry, true);
        Check(fresh.Additional == 0 && deaths.Reserve() == null && deaths.TakeSlot(61, 1, 60), "Old-world acknowledgement cannot alter new-world death history/quota");
        deaths.Complete(fresh, true);
        deaths.Observe(); var saved = deaths.Reserve();
        Check(saved.Additional == 0, "Successful local-only save consumes its death snapshot");
        deaths.Complete(saved, true);
        deaths.Observe(); var failedRetry = deaths.Reserve(); deaths.Complete(failedRetry, false);
        deaths.Observe();
        Check(deaths.Reopen(failedRetry) && deaths.Reserve() == null, "Gallery retry reserves original death snapshot without overlapping claims");
        deaths.Complete(failedRetry, true);
        var afterRetry = deaths.Reserve();
        Check(afterRetry.Additional == 0 && !deaths.Reopen(failedRetry), "Retry acknowledgement consumes only its original death snapshot");
        deaths.Complete(afterRetry, false); deaths.Clear();
        Check(!deaths.Reopen(afterRetry), "Old-session retry ticket cannot reopen");
        var quota = new MomentRateLimit();
        Check(quota.TryTake(0, 2, 60) && quota.TryTake(10, 2, 60) && !quota.TryTake(59, 2, 60), "Configurable per-player quota allows two captures");
        Check(quota.TryTake(60, 2, 60) && !quota.TryTake(61, 2, 60) && quota.TryTake(70, 2, 60), "Sliding window differs from fixed minute buckets");
        quota.Clear();
        Check(!quota.TryTake(double.NaN, 20, 60) && !quota.TryTake(double.PositiveInfinity, 20, 60), "Invalid event time rejected");
        for (int i = 0; i < 20; i++) Check(quota.TryTake(i, int.MaxValue, 3600), "Quota memory bounded to allowed maximum");
        Check(!quota.TryTake(20, int.MaxValue, 3600), "Excessive configured maximum clamped");
        var flavor = new DeathFlavor(); var random = new Random(3);
        var lines = new HashSet<string>(DeathFlavor.Lines);
        Check(lines.Count == 20, "Twenty distinct neutral captions");
        string previous = null;
        for (int i = 0; i < 100; i++)
        { string current = flavor.Next(random); Check(lines.Contains(current) && current != previous, "No immediate cheeky caption repeat"); previous = current; }
        string text = EventMessages.Death("\uD83D\uDC80 {player} died!", true, "", "Ragnar", true, "poison", "That was almost a plan.", 3);
        Check(text.Contains("**Cause:** poison") && text.Contains("That was almost a plan.") && text.EndsWith("**Additional deaths since last shared death:** 3"), "Flavor and suppressed deaths preserve actual cause");
        text = EventMessages.Death("Farewell {player}", true, "", "Ragnar", flavor: "cheeky");
        Check(text == "Farewell Ragnar", "Existing custom template does not gain unsolicited flavor");
        text = EventMessages.Death("{player}: {flavor} / {extra_deaths}", true, "", "Ragnar", flavor: "cheeky", additional: 2);
        Check(text == "Ragnar: cheeky / 2", "Custom templates place flavor and count without duplication");
        text = EventMessages.Death(new string('x', 1999) + "\uD83C\uDFC6", true, "", "Ragnar", additional: 3);
        Check(text.Length <= 2000 && text.EndsWith(":** 3") && !text.Contains("\uD83C"), "Long captions leave space for death summary without splitting surrogate");
        Check(EventMessages.RecordedPost(text, new string('n', 80)).EndsWith(":** 3"), "Final recorder attribution cannot truncate the intervening-death count");
        Console.WriteLine("PASS: " + checks + " death cooldown, acknowledgement and caption assertions.");
    }
}
