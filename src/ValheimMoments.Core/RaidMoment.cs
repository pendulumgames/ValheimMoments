using System;

namespace ValheimMoments.Core
{
    public enum RaidMomentResult { None, Started, Ended, Abandoned }

    // One opening segment per locally observed occurrence. Its owner must release
    // media on Abandoned/Reset. This class owns metadata only, no camera or files.
    public sealed class RaidMoment
    {
        private object session, occurrence;
        private bool eligible;
        private double started, lastTime = double.NegativeInfinity;
        private readonly double lifetime;
        public bool Pending { get { return eligible; } }
        public RaidMoment(double maximumSeconds = 1800)
        {
            if (!Finite(maximumSeconds) || maximumSeconds < 1 || maximumSeconds > 7200)
                throw new ArgumentOutOfRangeException("maximumSeconds");
            lifetime = maximumSeconds;
        }
        public void Reset(object currentSession)
        { session = currentSession; occurrence = null; eligible = false; lastTime = double.NegativeInfinity; }

        public RaidMomentResult Enter(object currentSession, object currentOccurrence, double now, bool alive, bool canCapture)
        {
            Time(now);
            if (session == null || !ReferenceEquals(session, currentSession) || currentOccurrence == null)
                return Abandon();
            if (ReferenceEquals(occurrence, currentOccurrence)) return RaidMomentResult.None;
            // Reject replacement while a previous segment is held; caller must first
            // consume Abandoned, then observe the new occurrence separately.
            if (eligible) return Abandon();
            occurrence = currentOccurrence; started = now;
            eligible = alive && canCapture;
            return eligible ? RaidMomentResult.Started : RaidMomentResult.None;
        }
        public RaidMomentResult Observe(object currentSession, double now, bool alive, bool participating, bool enabled)
        {
            Time(now);
            if (!ReferenceEquals(session, currentSession) || !alive || !participating || !enabled || now - started >= lifetime)
                return Abandon();
            return RaidMomentResult.None;
        }
        public RaidMomentResult End(object currentSession, object endedOccurrence, double now, bool alive)
        {
            Time(now);
            if (!ReferenceEquals(session, currentSession)) return Abandon();
            if (!ReferenceEquals(occurrence, endedOccurrence)) return RaidMomentResult.None;
            if (!eligible) return RaidMomentResult.None;
            if (!alive || now - started >= lifetime) return Abandon();
            eligible = false;
            return RaidMomentResult.Ended; // No claim about kills, victory or full-raid survival.
        }
        public RaidMomentResult Abandon()
        { bool wasEligible = eligible; eligible = false; return wasEligible ? RaidMomentResult.Abandoned : RaidMomentResult.None; }
        private void Time(double now)
        { if (!Finite(now) || now < lastTime) throw new ArgumentOutOfRangeException("now"); lastTime = now; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
