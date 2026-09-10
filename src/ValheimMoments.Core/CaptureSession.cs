namespace ValheimMoments.Core
{
    // Main-thread session boundary. Outstanding GPU requests keep their revision;
    // a later session cannot accept their pixels even if the same session object returns.
    public sealed class CaptureSession
    {
        private readonly CaptureBuffer history;
        private object session;
        public long Revision { get; private set; }
        public bool HasSession { get { return session != null; } }

        public CaptureSession(CaptureBuffer history) { this.history = history; }

        public bool Observe(object current)
        {
            if (object.ReferenceEquals(session, current)) return false;
            history.ClearHistory(); // Does not release a completed worker-owned clip.
            session = current;
            Revision++;
            return true;
        }

        public bool Accepts(long revision) { return HasSession && revision == Revision; }
    }
}
