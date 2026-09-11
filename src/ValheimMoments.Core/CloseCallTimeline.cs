using System;

namespace ValheimMoments.Core
{
    // Coordinates relative to the threshold hit. No game timescale changes.
    // The final encoder timestamp is DurationMilliseconds, not a source timestamp.
    public sealed class CloseCallTimeline
    {
        public readonly double SlowSourceSeconds, FollowUpSeconds;
        public readonly int SlowMilliseconds, DurationMilliseconds;
        public CloseCallTimeline(double slowSourceSeconds = 1, double followUpSeconds = 20,
            int slowMilliseconds = 3000, int durationMilliseconds = 10000)
        {
            if (!Finite(slowSourceSeconds) || slowSourceSeconds <= 0 || slowSourceSeconds > 10 ||
                !Finite(followUpSeconds) || followUpSeconds <= 0 || followUpSeconds > 60 ||
                slowMilliseconds < 1 || durationMilliseconds <= slowMilliseconds || durationMilliseconds > 60000)
                throw new ArgumentOutOfRangeException("Invalid close-call timeline");
            SlowSourceSeconds = slowSourceSeconds; FollowUpSeconds = followUpSeconds;
            SlowMilliseconds = slowMilliseconds; DurationMilliseconds = durationMilliseconds;
        }

        public int PlaybackMilliseconds(double sourceOffset)
        {
            if (!Finite(sourceOffset) || sourceOffset < -SlowSourceSeconds || sourceOffset > FollowUpSeconds)
                throw new ArgumentOutOfRangeException("sourceOffset");
            double value = sourceOffset <= 0
                ? (sourceOffset + SlowSourceSeconds) / SlowSourceSeconds * SlowMilliseconds
                : SlowMilliseconds + sourceOffset / FollowUpSeconds * (DurationMilliseconds - SlowMilliseconds);
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        // Choose a bounded source sampling schedule from an output FPS. Slow footage
        // retains its native detail; fast footage only needs this many source samples.
        public double FastSampleInterval(int outputFps)
        {
            if (outputFps < 1 || outputFps > 120) throw new ArgumentOutOfRangeException("outputFps");
            return FollowUpSeconds / Math.Ceiling((DurationMilliseconds - SlowMilliseconds) * outputFps / 1000.0);
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
