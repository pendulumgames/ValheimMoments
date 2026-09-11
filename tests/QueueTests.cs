using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ValheimMoments;

internal static class QueueTests
{
    private static int checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    private static string Id(int value) { return value.ToString("x32"); }
    internal static void Run()
    {
        var session = new object(); int sends = 0, released = 0;
        QueuedPerspective[] sent = null;
        var receipt = new TaskCompletionSource<UploadResult>();
        var queue = new HostHighlightQueue(new HighlightDirector(2, 10, 8, 2), 20, (batch, token) => { sends++; sent = batch; return receipt.Task; });
        queue.Reset(session);
        bool transportFree = false; queue.CanUpload = () => transportFree;
        var results = new Dictionary<int, RelayOutcome>(); var starts = new List<int>();
        var transfers = new Dictionary<int, Action<string>>();
        Func<int, QueuedPerspective> remote = peer => new QueuedPerspective {
            Offer = new HighlightOffer(Id(peer), Id(100), "boss", peer, "Player " + peer, 4, peer == 2), Message = "Boss", Keep = true,
            Eligible = () => true, Transfer = done => { starts.Add(peer); transfers[peer] = done; return true; },
            Complete = result => results.Add(peer, result), Receipt = result => { if (result == null) throw new Exception("Missing receipt"); }, Release = () => released++
        };
        Check(queue.Offer(session, remote(3), 0) == HighlightAdmission.Accepted, "First remote metadata reserved");
        Check(queue.Offer(session, remote(2), 0) == HighlightAdmission.Accepted, "Second remote metadata reserved");
        var local = remote(1); local.File = "host.webp"; local.Transfer = null;
        Check(queue.Offer(session, local, 0) == HighlightAdmission.Accepted, "Host perspective shares queue with clients");
        queue.Tick(session, 1);
        Check(starts.Count == 0 && sends == 0, "No bytes requested during collection window");
        queue.Tick(session, 2);
        Check(starts.Count == 1 && starts[0] == 2 && results[3] == RelayOutcome.Omitted, "Only selected remote requested; unselected explicitly omitted");
        Check(results.Count == 1 && sends == 0, "Selection and transfer request are not upload success");
        transfers[2]("remote.webp"); queue.Tick(session, 3);
        Check(sends == 0, "Existing uploader reservation prevents concurrent HTTP sends");
        transportFree = true; queue.Tick(session, 3);
        Check(sends == 1 && sent.Length == 2 && sent[0].File == "host.webp" && sent[1].File == "remote.webp", "One upload contains host and selected remote");
        Check(!sent[0].Offer.PersonalFirst && sent[1].Offer.PersonalFirst, "Grouped upload retains personal first status");
        queue.Tick(session, 4); Check(results.Count == 1, "No premature final acknowledgement while HTTP pending");
        receipt.SetResult(new UploadResult { Success = true }); queue.Tick(session, 5);
        Check(results[1] == RelayOutcome.Uploaded && results[2] == RelayOutcome.Uploaded && released == 3 && !queue.Busy, "Exactly selected clips receive success and every reservation releases");
        queue.Tick(session, 6); Check(sends == 1 && released == 3, "Completed group cannot post or release twice");

        foreach (var outcome in new[] { RelayOutcome.Failed, RelayOutcome.Unknown })
        {
            var finish = new List<RelayOutcome>();
            queue = new HostHighlightQueue(new HighlightDirector(3, 10, 20, 1), 20,
                (batch, token) => Task.FromResult(outcome == RelayOutcome.Unknown ? UploadResult.Unknown("Unknown") : UploadResult.Fail("Rejected")));
            queue.Reset(session); var item = remote(1); item.File = "local.webp"; item.Complete = finish.Add;
            queue.Offer(session, item, 0); queue.Tick(session, 1); queue.Tick(session, 2);
            Check(finish.Count == 1 && finish[0] == outcome, "Non-success outcome is preserved through collection queue");
        }
        receipt = new TaskCompletionSource<UploadResult>(); bool cancelled = false; int cleaned = 0;
        queue = new HostHighlightQueue(new HighlightDirector(3, 10, 20, 1), 20,
            (batch, token) => { token.Register(() => cancelled = true); return receipt.Task; });
        queue.Reset(session); var delayed = remote(1); delayed.File = "local.webp"; delayed.Complete = _ => { }; delayed.Release = () => cleaned++;
        queue.Offer(session, delayed, 0); queue.Tick(session, 1); var nextWorld = new object(); queue.Tick(nextWorld, 2);
        Check(cancelled && cleaned == 0, "World switch cancels HTTP but preserves files while reader is active");
        receipt.SetResult(UploadResult.Unknown("Cancelled")); queue.Tick(nextWorld, 3);
        Check(cleaned == 1 && !queue.Busy, "Old upload cleanup waits for actual HTTP completion");
        Check(queue.Offer(session, remote(4), 4) == HighlightAdmission.WrongSession, "Old capture cannot enter new-world queue");

        queue = new HostHighlightQueue(new HighlightDirector(3, 10, 20, 1), 4, (batch, token) => Task.FromResult(new UploadResult { Success = true }));
        queue.Reset(session); var disconnected = remote(1); bool live = true; disconnected.Eligible = () => live; disconnected.Complete = _ => { };
        Check(queue.Offer(session, disconnected, 0) == HighlightAdmission.Accepted && queue.Offer(session, remote(2), 0) == HighlightAdmission.Capacity, "Aggregate transfer reservation bounded before bytes");
        live = false; queue.Tick(session, 1);
        Check(!queue.Busy && queue.Offer(session, remote(2), 1) == HighlightAdmission.Closed, "Disconnected reservation releases without reopening closed encounter");
        Console.WriteLine("PASS: " + checks + " host collection, transfer selection and grouped completion assertions.");
    }
}
