using System;
using System.Collections.Generic;

namespace ValheimMoments
{
    // Metadata only: no file bytes, webhook secrets, Unity calls or background work.
    // Caller authenticates the peer and supplies the current host session object.
    internal sealed class HighlightOffer
    {
        internal readonly string ClipId, EventId, Kind, Recorder;
        internal readonly long PeerId, Bytes;
        internal readonly bool PersonalFirst;
        internal HighlightOffer(string clipId, string eventId, string kind, long peerId, string recorder, long bytes, bool personalFirst)
        { ClipId = clipId; EventId = eventId; Kind = kind; PeerId = peerId; Recorder = recorder; Bytes = bytes; PersonalFirst = personalFirst; }
    }

    internal enum HighlightAdmission { Accepted, Invalid, WrongSession, Duplicate, Closed, Capacity, Oversized }

    internal sealed class HighlightSelection
    {
        internal readonly string EventId, Kind;
        internal readonly HighlightOffer[] Selected, Omitted;
        internal HighlightSelection(string eventId, string kind, HighlightOffer[] selected, HighlightOffer[] omitted)
        { EventId = eventId; Kind = kind; Selected = selected; Omitted = omitted; }
    }

    internal sealed class HighlightDirector
    {
        internal const int MaximumGroups = 8, MaximumOffers = 8, MaximumRecent = 256;
        private sealed class Group
        {
            internal string Key, EventId, Kind;
            internal double Deadline;
            internal readonly List<HighlightOffer> Offers = new List<HighlightOffer>();
        }
        private sealed class ClosedGroup { internal string Key; internal double Until; }
        private readonly List<Group> groups = new List<Group>();
        private readonly List<ClosedGroup> recent = new List<ClosedGroup>();
        private readonly int maximumPerspectives;
        private readonly long perFileBytes, perPostBytes;
        private readonly double collectionSeconds;
        private object session;
        private double lastTime;

        internal HighlightDirector(int maximumPerspectives, long perFileBytes, long perPostBytes, double collectionSeconds)
        {
            if (maximumPerspectives < 1 || maximumPerspectives > 3 || perFileBytes < 1 || perPostBytes < 1 ||
                !Finite(collectionSeconds) || collectionSeconds < 0.5 || collectionSeconds > 30) throw new ArgumentOutOfRangeException();
            this.maximumPerspectives = maximumPerspectives; this.perFileBytes = perFileBytes;
            this.perPostBytes = perPostBytes; this.collectionSeconds = collectionSeconds;
        }

        internal void Reset(object currentSession)
        { session = currentSession; groups.Clear(); recent.Clear(); lastTime = 0; }

        internal static bool ValidId(string id)
        {
            if (id == null || id.Length != 32) return false;
            bool nonzero = false;
            foreach (char c in id)
            {
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
                if (c != '0') nonzero = true;
            }
            return nonzero;
        }

        private static bool Finite(double time) { return !double.IsNaN(time) && !double.IsInfinity(time); }
        private bool Advance(double now)
        {
            if (!Finite(now) || now < lastTime) return false;
            lastTime = now;
            recent.RemoveAll(item => now >= item.Until);
            return true;
        }

        internal HighlightAdmission Offer(object capturedSession, HighlightOffer offer, double now)
        {
            if (session == null || !ReferenceEquals(session, capturedSession)) return HighlightAdmission.WrongSession;
            if (!Advance(now) || offer == null || !ValidId(offer.ClipId) || offer.PeerId == 0 ||
                string.IsNullOrWhiteSpace(offer.Recorder) || offer.Recorder.Length > 128 ||
                offer.Recorder.IndexOfAny(new[] { '\r', '\n' }) >= 0 || !RelayProtocol.ValidKind(offer.Kind) ||
                (offer.EventId != null && !ValidId(offer.EventId)) || offer.Bytes < 1) return HighlightAdmission.Invalid;
            if (offer.Bytes > perFileBytes || offer.Bytes > perPostBytes) return HighlightAdmission.Oversized;
            // Only creature-death categories currently carry shared owner identity.
            // Personal/manual discoveries and missing metadata cannot merge by coincidence.
            bool shared = offer.EventId != null && (offer.Kind == "boss" || offer.Kind == "special" || offer.Kind == "loot");
            string key = offer.Kind + ":" + (shared ? "event:" + offer.EventId : "clip:" + offer.PeerId + ":" + offer.ClipId);
            if (recent.Exists(item => item.Key == key)) return HighlightAdmission.Closed;
            var group = groups.Find(item => item.Key == key);
            if (group != null && now >= group.Deadline) return HighlightAdmission.Closed;
            foreach (var pending in groups)
                if (pending.Offers.Exists(item => item.PeerId == offer.PeerId && item.ClipId == offer.ClipId)) return HighlightAdmission.Duplicate;
            if (group == null)
            {
                if (groups.Count >= MaximumGroups) return HighlightAdmission.Capacity;
                group = new Group { Key = key, EventId = shared ? offer.EventId : null, Kind = offer.Kind, Deadline = shared ? now + collectionSeconds : now };
                groups.Add(group);
            }
            if (group.Offers.Exists(item => item.PeerId == offer.PeerId)) return HighlightAdmission.Duplicate;
            if (group.Offers.Count >= MaximumOffers) return HighlightAdmission.Capacity;
            group.Offers.Add(offer);
            return HighlightAdmission.Accepted;
        }

        // Selection is not an upload acknowledgement. Omitted offers must receive
        // an explicit omission result; selected offers still need transfer and receipt.
        internal HighlightSelection[] Drain(object currentSession, double now)
        {
            if (session == null || !ReferenceEquals(session, currentSession) || !Advance(now)) return new HighlightSelection[0];
            var ready = groups.FindAll(item => now >= item.Deadline);
            ready.Sort((a, b) => { int order = a.Deadline.CompareTo(b.Deadline); return order != 0 ? order : string.CompareOrdinal(a.Key, b.Key); });
            var results = new List<HighlightSelection>();
            foreach (var group in ready)
            {
                group.Offers.Sort((a, b) => a.PeerId.CompareTo(b.PeerId));
                var selected = new List<HighlightOffer>(); var omitted = new List<HighlightOffer>();
                long total = 0;
                foreach (var offer in group.Offers)
                {
                    if (selected.Count < maximumPerspectives && offer.Bytes <= perPostBytes - total)
                    { selected.Add(offer); total += offer.Bytes; }
                    else omitted.Add(offer);
                }
                results.Add(new HighlightSelection(group.EventId, group.Kind, selected.ToArray(), omitted.ToArray()));
                groups.Remove(group);
                if (recent.Count >= MaximumRecent) recent.RemoveAt(0);
                recent.Add(new ClosedGroup { Key = group.Key, Until = now + 1800 });
            }
            return results.ToArray();
        }
    }
}
