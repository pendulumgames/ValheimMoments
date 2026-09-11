using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ValheimMoments
{
    internal sealed class QueuedPerspective
    {
        internal HighlightOffer Offer;
        internal string Message, File;
        internal bool Keep;
        internal Func<bool> Eligible;
        internal Func<Action<string>, bool> Transfer;
        internal Action<RelayOutcome> Complete;
        internal Action<UploadResult> Receipt;
        internal Action Release;
    }

    // Host-thread orchestration. Only the supplied uploader runs asynchronously.
    // Admission reserves encoded bytes before any remote file transfer begins.
    internal sealed class HostHighlightQueue
    {
        private sealed class Entry { internal QueuedPerspective Item; internal double Deadline; internal bool Requested, Done; }
        private readonly Dictionary<HighlightOffer, Entry> entries = new Dictionary<HighlightOffer, Entry>();
        private readonly Queue<HighlightSelection> ready = new Queue<HighlightSelection>();
        private readonly HighlightDirector director;
        private readonly Func<QueuedPerspective[], CancellationToken, Task<UploadResult>> send;
        private readonly long reservationLimit;
        private long reserved;
        private object session;
        private HighlightSelection active;
        private Task<UploadResult> upload;
        private QueuedPerspective[] uploading;
        private CancellationTokenSource cancellation;
        private bool resetPending;
        internal bool Busy { get { return entries.Count != 0 || upload != null; } }
        internal Func<bool> CanUpload;

        internal HostHighlightQueue(HighlightDirector director, long reservationLimit,
            Func<QueuedPerspective[], CancellationToken, Task<UploadResult>> send)
        { this.director = director; this.reservationLimit = reservationLimit; this.send = send; }

        internal void Reset(object current)
        {
            session = current; director.Reset(current); ready.Clear(); active = null;
            cancellation?.Cancel(); resetPending = upload != null;
            foreach (var entry in new List<Entry>(entries.Values))
                if (uploading == null || Array.IndexOf(uploading, entry.Item) < 0) Finish(entry, RelayOutcome.Failed);
        }

        internal HighlightAdmission Offer(object origin, QueuedPerspective item, double now)
        {
            if (resetPending || item == null || item.Offer == null || item.Eligible == null || item.Complete == null ||
                (item.File == null && item.Transfer == null)) return HighlightAdmission.Invalid;
            if (entries.Count >= 16 || item.Offer.Bytes > reservationLimit - reserved) return HighlightAdmission.Capacity;
            var accepted = director.Offer(origin, item.Offer, now);
            if (accepted != HighlightAdmission.Accepted) return accepted;
            entries.Add(item.Offer, new Entry { Item = item, Deadline = now + 1200 }); reserved += item.Offer.Bytes;
            return accepted;
        }

        internal void Tick(object current, double now)
        {
            if (!ReferenceEquals(current, session)) Reset(current);
            if (upload != null)
            {
                if (!upload.IsCompleted)
                {
                    foreach (var item in uploading) if (!Eligible(item)) cancellation.Cancel();
                    return;
                }
                UploadResult result;
                try { result = upload.GetAwaiter().GetResult(); } catch { result = UploadResult.Unknown("Group upload confirmation failed."); }
                foreach (var item in uploading) { try { item.Receipt?.Invoke(result); } catch { } }
                foreach (var item in uploading)
                    if (entries.TryGetValue(item.Offer, out var entry)) Finish(entry,
                        result.Success ? RelayOutcome.Uploaded : result.DeliveryUnknown ? RelayOutcome.Unknown : RelayOutcome.Failed);
                upload = null; uploading = null; cancellation.Dispose(); cancellation = null; resetPending = false; active = null;
            }
            foreach (var entry in new List<Entry>(entries.Values))
                if (now >= entry.Deadline || !Eligible(entry.Item)) Finish(entry, RelayOutcome.Failed);
            foreach (var selection in director.Drain(current, now))
            {
                if (ready.Count < 8) ready.Enqueue(selection);
                else foreach (var offer in selection.Selected) if (entries.TryGetValue(offer, out var excess)) Finish(excess, RelayOutcome.Omitted);
                foreach (var offer in selection.Omitted) if (entries.TryGetValue(offer, out var omitted)) Finish(omitted, RelayOutcome.Omitted);
            }
            if (active == null && ready.Count != 0) active = ready.Dequeue();
            if (active == null) return;
            foreach (var offer in active.Omitted) if (entries.TryGetValue(offer, out var omitted)) Finish(omitted, RelayOutcome.Omitted);
            foreach (var offer in active.Selected)
            {
                if (!entries.TryGetValue(offer, out var entry) || entry.Item.File != null) continue;
                if (!entry.Requested)
                {
                    entry.Requested = true;
                    bool started = false;
                    try { started = entry.Item.Transfer(path => { if (!entry.Done) { entry.Item.File = path; if (path == null) Finish(entry, RelayOutcome.Failed); } }); }
                    catch { }
                    if (!started) { entry.Requested = false; return; }
                }
                return; // One transfer at a time; other selected peers wait without sending bytes.
            }
            var batch = new List<QueuedPerspective>();
            foreach (var offer in active.Selected) if (entries.TryGetValue(offer, out var entry) && entry.Item.File != null) batch.Add(entry.Item);
            if (batch.Count == 0) { active = null; return; }
            if (CanUpload != null && !CanUpload()) return;
            uploading = batch.ToArray(); cancellation = new CancellationTokenSource();
            try { upload = send(uploading, cancellation.Token); }
            catch { upload = Task.FromResult(UploadResult.Fail("Group upload could not start.")); }
        }

        private static bool Eligible(QueuedPerspective item) { try { return item.Eligible(); } catch { return false; } }
        private void Finish(Entry entry, RelayOutcome result)
        {
            if (entry.Done) return;
            entry.Done = true; entries.Remove(entry.Item.Offer); reserved -= entry.Item.Offer.Bytes;
            try { entry.Item.Complete(result); } catch { }
            try { entry.Item.Release?.Invoke(); } catch { }
        }
        internal void Stop()
        {
            Reset(null);
            if (upload == null) return;
            var items = uploading;
            _ = upload.ContinueWith(task => {
                var ignored = task.Exception;
                foreach (var item in items) try { item.Release?.Invoke(); } catch { }
            }, TaskScheduler.Default);
        }
    }
}
