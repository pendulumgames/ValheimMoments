using System;
using System.Collections.Generic;

namespace ValheimMoments.Core
{
    // Single producer/main-thread owner. Workers may READ a completed Clip's
    // pixels until it is released on the owner thread. Never expose ring memory
    // to a worker without retaining it in a Clip.
    public sealed class CaptureBuffer
    {
        internal sealed class Frame
        {
            internal readonly byte[] Pixels;
            internal double Time;
            internal int References;
            internal Frame(int bytes) { Pixels = new byte[bytes]; }
        }

        public sealed class Clip
        {
            private readonly Frame[] frames;
            private readonly CaptureBuffer owner;
            private bool released;
            private readonly double[] playback;
            public int Count { get { Check(); return frames.Length; } }
            public double TriggerTime { get; private set; }
            public double EndTime { get; private set; }

            internal Clip(CaptureBuffer owner, Frame[] frames, double trigger, double end, double[] playback = null)
            {
                this.owner = owner;
                this.frames = frames;
                TriggerTime = trigger;
                EndTime = end;
                this.playback = playback;
            }

            // Borrowed array, read-only by contract. Do not retain after Release.
            public byte[] GetPixels(int index) { Check(); return frames[index].Pixels; }
            public double GetTimestamp(int index) { Check(); return playback == null ? frames[index].Time : playback[index]; }
            private void Check()
            {
                if (released) throw new ObjectDisposedException("Clip");
            }

            public void Release()
            {
                owner.CheckThread();
                if (released) return;
                foreach (Frame frame in frames) owner.Release(frame);
                released = true;
                owner.busy = false;
            }
        }

        private readonly Queue<Frame> free;
        private readonly Queue<Frame> ring;
        private readonly List<Frame> pending;
        private readonly int ownerThread;
        private readonly int preFrames;
        private readonly int maxClipFrames;
        private readonly int bytesPerFrame;
        private readonly int framesPerSecond;
        private readonly double preSeconds;
        private readonly double postSeconds;
        private readonly double maxPostSeconds;
        private double activePostSeconds;
        private double lastTime = double.NegativeInfinity;
        private double triggerTime;
        private bool collecting;
        private bool busy;
        private CloseCallTimeline timeline;
        private double nextSample;
        private double sampleInterval;

        public int BufferedFrames { get { return ring.Count; } }
        public int FreeFrames { get { return free.Count; } }
        public long AllocatedPixelBytes { get; private set; }
        public bool IsBusy { get { return busy; } }

        public CaptureBuffer(int width, int height, int fps, double pre, double post,
            long memoryBudgetBytes, double? maximumPostSeconds = null)
        {
            double maximumPost = maximumPostSeconds ?? post;
            if (width < 1 || height < 1 || fps < 1 || fps > 120 ||
                !Finite(pre) || !Finite(post) || !Finite(maximumPost) || pre <= 0 || post < 0 || maximumPost < post || pre + maximumPost > 60)
                throw new ArgumentOutOfRangeException("Invalid capture settings");
            bytesPerFrame = checked(width * height * 4);
            framesPerSecond = fps;
            preFrames = checked((int)Math.Ceiling(pre * fps));
            maxClipFrames = checked(preFrames + (int)Math.Ceiling(maximumPost * fps));
            int capacity = checked(preFrames + maxClipFrames + 1);
            AllocatedPixelBytes = checked((long)capacity * bytesPerFrame);
            if (AllocatedPixelBytes > memoryBudgetBytes)
                throw new ArgumentOutOfRangeException("memoryBudgetBytes", "Capture pool exceeds memory budget");
            preSeconds = pre;
            postSeconds = post;
            maxPostSeconds = maximumPost;
            ownerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            free = new Queue<Frame>(capacity);
            ring = new Queue<Frame>(preFrames);
            pending = new List<Frame>(maxClipFrames);
            for (int i = 0; i < capacity; i++) free.Enqueue(new Frame(bytesPerFrame));
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private void CheckThread()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Capture state must be accessed on its owner thread");
        }
        private void Release(Frame frame)
        {
            if (--frame.References == 0) free.Enqueue(frame);
        }

        // Timestamp is the capture SUBMISSION time, never GPU callback time.
        // Adapter must deliver readbacks in submission order, at <= configured FPS.
        // Copies into reusable storage; the caller may immediately reuse its input.
        public bool AddFrame(byte[] rgba, double timestamp)
        {
            CheckThread();
            if (rgba == null || rgba.Length != bytesPerFrame)
                throw new ArgumentException("Expected one tightly packed RGBA frame", "rgba");
            if (!Finite(timestamp) || timestamp <= lastTime)
                throw new ArgumentOutOfRangeException("timestamp", "Frames must be strictly ordered");
            lastTime = timestamp;
            while (ring.Count > 0 && (ring.Count >= preFrames || ring.Peek().Time < timestamp - preSeconds))
                Release(ring.Dequeue());
            if (free.Count == 0) return false;
            Frame frame = free.Dequeue();
            Buffer.BlockCopy(rgba, 0, frame.Pixels, 0, bytesPerFrame);
            frame.Time = timestamp;
            frame.References = 1;
            ring.Enqueue(frame);
            // Includes readbacks submitted before the trigger that complete after it.
            if (collecting && timestamp >= triggerTime - (timeline == null ? preSeconds : timeline.SlowSourceSeconds) && timestamp < triggerTime + activePostSeconds)
            {
                if (pending.Count < maxClipFrames && (timeline == null || timestamp < triggerTime || timestamp >= nextSample))
                {
                    frame.References++;
                    pending.Add(frame);
                    if (timeline != null && timestamp >= triggerTime)
                        nextSample = triggerTime + (Math.Floor((timestamp - triggerTime) / sampleInterval) + 1) * sampleInterval;
                }
            }
            return true;
        }

        // Simple bounded policy: reject additional triggers until encoding releases
        // the completed clip. Recording itself continues throughout encoding.
        public bool TryTrigger(double timestamp, double? postOverride = null)
        {
            CheckThread();
            double duration = postOverride ?? postSeconds;
            if (!Finite(duration) || duration < 0 || duration > maxPostSeconds)
                throw new ArgumentOutOfRangeException("postOverride");
            if (!Finite(timestamp) || timestamp < lastTime)
                throw new ArgumentOutOfRangeException("timestamp");
            if (busy) return false;
            timeline = null;
            triggerTime = timestamp;
            activePostSeconds = duration;
            pending.Clear();
            foreach (Frame frame in ring)
            {
                if (frame.Time >= timestamp - preSeconds && frame.Time < timestamp + activePostSeconds)
                {
                    frame.References++;
                    pending.Add(frame);
                }
            }
            busy = collecting = true;
            return true;
        }

        // Uses the existing fixed pool. Admission reserves worst-case selected frames;
        // a 20-second source window does not allocate 20 seconds of full-rate pixels.
        public bool TryTriggerCloseCall(double timestamp, CloseCallTimeline mapping, int captureFps)
        {
            CheckThread();
            if (mapping == null) throw new ArgumentNullException("mapping");
            if (captureFps != framesPerSecond) throw new ArgumentOutOfRangeException("captureFps");
            double interval = mapping.FastSampleInterval(captureFps);
            if (!Finite(timestamp) || timestamp < lastTime) throw new ArgumentOutOfRangeException("timestamp");
            int required = (int)Math.Ceiling(mapping.SlowSourceSeconds * captureFps) +
                (int)Math.Ceiling(mapping.FollowUpSeconds / interval);
            if (busy || mapping.SlowSourceSeconds > preSeconds || required > maxClipFrames) return false;
            timeline = mapping; sampleInterval = interval; nextSample = timestamp;
            triggerTime = timestamp; activePostSeconds = mapping.FollowUpSeconds;
            pending.Clear();
            foreach (Frame frame in ring)
                if (frame.Time >= timestamp - mapping.SlowSourceSeconds && frame.Time < timestamp)
                { frame.References++; pending.Add(frame); }
            busy = collecting = true;
            return true;
        }

        // A watermark asserts that every submission BEFORE this time has either
        // completed or failed. Wall-clock time alone is not a valid watermark.
        public Clip TryComplete(double completedThrough)
        {
            CheckThread();
            if (!Finite(completedThrough)) throw new ArgumentOutOfRangeException("completedThrough");
            if (!collecting || completedThrough < triggerTime + activePostSeconds) return null;
            double[] playback = null;
            double end = triggerTime + activePostSeconds;
            if (timeline != null)
            {
                playback = new double[pending.Count];
                for (int i = 0; i < pending.Count; i++)
                    playback[i] = triggerTime + timeline.PlaybackMilliseconds(pending[i].Time - triggerTime) / 1000.0;
                end = triggerTime + timeline.DurationMilliseconds / 1000.0;
                // Hold the earliest available image across a missed initial readback.
                if (playback.Length > 0) playback[0] = triggerTime;
            }
            Clip clip = new Clip(this, pending.ToArray(), triggerTime, end, playback);
            pending.Clear();
            collecting = false;
            return clip;
        }

        public void CancelPending()
        {
            CheckThread();
            if (!collecting) return; // A worker-owned clip must be released by its owner.
            foreach (Frame frame in pending) Release(frame);
            pending.Clear();
            busy = collecting = false;
        }

        public void ClearHistory()
        {
            CheckThread();
            CancelPending();
            while (ring.Count > 0) Release(ring.Dequeue());
            lastTime = double.NegativeInfinity;
        }
    }
}
