using System;
using ValheimMoments.Core;

internal static class CloseCallTests
{
    private static int checks;
    private static void Check(bool value, string label)
    { if (!value) throw new Exception(label); checks++; }
    private static void Throws(Action action)
    { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Expected rejection"); }
    public static void Run()
    {
        var call = new CloseCall();
        Check(call.Observe(0, 4, 100, true) == CloseCallResult.None, "Login low health does not trigger");
        Check(call.Damage(1, 4, 3, 100, 100, true) == CloseCallResult.None, "Already low does not cross");
        Check(call.Damage(2, 10, 5, 100, 200, true) == CloseCallResult.None, "Changing maximum excluded");
        Check(call.Damage(3, 10, 5, 100, 100, true) == CloseCallResult.Started, "Exact threshold direct hit starts");
        for (int i = 4; i < 23; i++)
        {
            Check(call.Observe(i, 6, 100, true) == CloseCallResult.None, "Regeneration not completion");
            Check(call.Damage(i, 6, 4, 100, 100, true) == CloseCallResult.None, "Periodic oscillation suppressed");
        }
        Check(call.Observe(23, 4, 100, true) == CloseCallResult.Survived, "Exactly twenty seconds alive qualifies even low");
        Check(call.Observe(24, 4, 100, true) == CloseCallResult.None, "Completion emitted once");
        Check(call.Damage(124, 6, 4, 100, 100, true) == CloseCallResult.None, "Cooldown alone insufficient");
        call.Observe(125, 20, 100, true);
        call.Observe(134, 19, 100, true);
        call.Observe(135, 20, 100, true);
        Check(call.Damage(144, 6, 4, 100, 100, true) == CloseCallResult.None, "Interrupted recovery hold resets");
        call.Observe(144, 20, 100, true);
        Check(call.Damage(144, 6, 4, 100, 100, true) == CloseCallResult.None, "Damage itself interrupts recovery between observations");
        call.Observe(145, 20, 100, true);
        call.Observe(155, 20, 100, true);
        Check(call.Damage(155, 6, 4, 100, 100, true) == CloseCallResult.Started, "Sustained recovery plus cooldown rearms");
        Check(call.Observe(175, 0, 100, false) == CloseCallResult.Cancelled, "Death at completion boundary wins");
        Check(call.Observe(176, 100, 100, true) == CloseCallResult.None, "Respawn does not complete abandoned attempt");
        call.Reset();
        Check(call.Damage(0, 6, 4, 100, 100, true) == CloseCallResult.Started, "Session resets clock and state");
        Check(call.Cancel() == CloseCallResult.Cancelled && !call.Pending, "Busy capture cancellation");
        Check(call.Damage(1, 6, 4, 100, 100, true) == CloseCallResult.None, "Cancellation cannot bypass cooldown");
        Throws(() => call.Observe(-1, 100, 100, true));
        Throws(() => call.Observe(double.NaN, 100, 100, true));
        call.Reset(); call.Damage(0, 6, 4, 100, 100, true);
        Check(call.Observe(20, double.NaN, 100, true) == CloseCallResult.Cancelled, "Invalid health cannot prove survival");
        Throws(() => new CloseCall(recovery: .04));
        Throws(() => new CloseCall(followUpSeconds: double.PositiveInfinity));

        var timeline = new CloseCallTimeline();
        Check(timeline.PlaybackMilliseconds(-1) == 0, "Slow source starts at zero");
        Check(timeline.PlaybackMilliseconds(-.5) == 1500, "Slowdown mapping");
        Check(timeline.PlaybackMilliseconds(0) == 3000, "One shared segment boundary");
        Check(timeline.PlaybackMilliseconds(10) == 6500, "Fast mapping");
        Check(timeline.PlaybackMilliseconds(20) == 10000, "Exact ten second endpoint");
        Check(Math.Abs(timeline.FastSampleInterval(15) - 20.0 / 105) < 1e-10, "Only 105 fast samples at 15 FPS");
        int previous = -1;
        for (int i = 0; i <= 210; i++)
        { int mapped = timeline.PlaybackMilliseconds(-1 + i / 10.0); Check(mapped >= previous, "Ordered playback mapping"); previous = mapped; }
        Throws(() => timeline.PlaybackMilliseconds(-1.01));
        Throws(() => timeline.PlaybackMilliseconds(double.NaN));
        Throws(() => timeline.FastSampleInterval(500));
        Throws(() => new CloseCallTimeline(durationMilliseconds: 3000));
        var buffer = new CaptureBuffer(1, 1, 15, 5, 3, 4096);
        var pixel = new byte[4];
        for (int i = 0; i < 150; i++) buffer.AddFrame(pixel, i / 15.0);
        long allocation = buffer.AllocatedPixelBytes;
        Check(buffer.TryTriggerCloseCall(10, timeline, 15), "Sampled clip admitted in existing eight-second pool");
        for (int i = 150; i < 450; i++) buffer.AddFrame(pixel, i / 15.0);
        Check(buffer.TryComplete(29.99) == null, "Source watermark must reach twenty seconds");
        var clip = buffer.TryComplete(30);
        Check(clip.Count <= 120 && clip.Count >= 115, "Selected frames bounded by playback budget");
        Check(clip.GetTimestamp(0) == 10 && clip.EndTime == 20, "Mapped output has exact ten-second duration");
        Check(buffer.AllocatedPixelBytes == allocation, "No extra pixel allocation");
        for (int i = 1; i < clip.Count; i++)
            Check(clip.GetTimestamp(i) > clip.GetTimestamp(i - 1), "Selected timestamps strictly increase");
        clip.Release(); buffer.ClearHistory();
        Check(buffer.FreeFrames == 196, "Sampled references all reclaimed");
        Check(buffer.TryTriggerCloseCall(0, timeline, 15), "Empty history can start");
        buffer.AddFrame(pixel, 2);
        buffer.CancelPending();
        Check(buffer.TryTrigger(3), "Death capture can immediately replace cancelled close call");
        buffer.CancelPending(); buffer.ClearHistory();
        var small = new CaptureBuffer(1, 1, 15, 1, 1, 4096);
        Check(!small.TryTriggerCloseCall(0, timeline, 15) && !small.IsBusy, "Insufficient fixed capacity rejects without allocation");
        Throws(() => buffer.TryTriggerCloseCall(0, timeline, 30));
        var segments = new CaptureBuffer(1, 1, 15, 1, 6, 4096);
        segments.AddFrame(pixel, 0);
        Check(segments.TryTriggerSegment(1, 4), "Opening segment admitted");
        segments.AddFrame(pixel, .5); // delayed readback submitted before raid entry
        segments.AddFrame(pixel, 1.2);
        Check(segments.TryComplete(4.99) == null, "Opening waits for watermark");
        var opening = segments.TryComplete(5);
        Check(opening.Count == 1 && opening.GetTimestamp(0) == 1 && opening.EndTime == 5, "No pre-event frame; missed initial readback holds first frame for exact duration");
        opening.Release();
        Check(segments.TryTriggerSegment(6, 6), "Ending can use released capture slot");
        segments.CancelPending();
        Check(segments.TryTrigger(7), "Death can replace pending segment");
        segments.CancelPending(); segments.ClearHistory();
        foreach (double source in new[] { .5, 3.0 }) foreach (double follow in new[] { 5.0, 60.0 })
        {
            var custom = new CloseCallTimeline(source, follow, 1000, 20000);
            var customBuffer = new CaptureBuffer(1, 1, 30, 5, 20, 20000);
            for (int i = 0; i < 300; i++) customBuffer.AddFrame(pixel, i / 30.0);
            Check(customBuffer.TryTriggerCloseCall(10, custom, 30), "Custom extreme timeline fits reserved frames");
            for (int i = 300; i < (10 + follow) * 30; i++) customBuffer.AddFrame(pixel, i / 30.0);
            var customClip = customBuffer.TryComplete(10 + follow);
            Check(customClip != null && customClip.EndTime == 30 && customClip.GetTimestamp(0) == 10, "Custom exact total playback");
            customClip.Release(); customBuffer.ClearHistory();
            var customCall = new CloseCall(followUpSeconds: follow);
            customCall.Damage(0, 10, 4, 100, 100, true);
            Check(customCall.Observe(follow - .001, 4, 100, true) == CloseCallResult.None && customCall.Observe(follow, 4, 100, true) == CloseCallResult.Survived, "Custom follow-up survival boundary");
        }
        Console.WriteLine("Close-call policy/timeline: " + checks + " assertions passed.");
    }
}
