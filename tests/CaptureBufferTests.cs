using System;
using ValheimMoments.Core;

internal static class CaptureBufferTests
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
    }
    private static void Throws(Action action, string name)
    {
        try { action(); } catch (ArgumentException) { checks++; return; }
        throw new Exception("FAIL: " + name);
    }
    public static void Main()
    {
        var buffer = new CaptureBuffer(1, 1, 15, 5, 2, 4096);
        byte[] pixels = new byte[4];
        for (int i = 0; i < 150; i++)
        {
            pixels[0] = (byte)i;
            Check(buffer.AddFrame(pixels, i / 15.0), "feed history");
        }
        Check(buffer.BufferedFrames == 75, "five-second history");
        Check(buffer.TryTrigger(10), "trigger");
        Check(!buffer.TryTrigger(10), "overlap rejected");
        for (int i = 150; i < 180; i++)
        {
            pixels[0] = (byte)i;
            buffer.AddFrame(pixels, i / 15.0);
        }
        Check(buffer.TryComplete(11.99) == null, "wait for post window watermark");
        var clip = buffer.TryComplete(12);
        Check(clip.Count == 105, "75 pre plus 30 post");
        Check(clip.GetPixels(0)[0] == 75 && clip.GetPixels(104)[0] == 179, "chronological clip");
        for (int i = 180; i < 1000; i++)
        {
            pixels[0] = 255;
            Check(buffer.AddFrame(pixels, i / 15.0), "record during encoding");
        }
        Check(clip.GetPixels(0)[0] == 75, "worker pixels survive ring overwrite");
        clip.Release();
        clip.Release();
        Check(!buffer.IsBusy, "release is idempotent");
        Check(buffer.FreeFrames == 106, "no leaked frames after release");
        buffer.ClearHistory();
        Check(buffer.FreeFrames == 181, "all frames reclaimed");
        Check(buffer.TryTrigger(100), "empty warmup trigger");
        buffer.AddFrame(pixels, 100.1);
        var shortClip = buffer.TryComplete(102);
        Check(shortClip.Count == 1, "warmup uses only real frames");
        shortClip.Release();
        buffer.ClearHistory();
        buffer.AddFrame(pixels, 0);
        buffer.TryTrigger(0.1);
        buffer.AddFrame(pixels, 0.05); // Submitted before trigger, completed later.
        var late = buffer.TryComplete(2.1);
        Check(late.Count == 2, "late pre-event readback retained");
        late.Release();
        buffer.TryTrigger(3);
        buffer.CancelPending();
        Check(!buffer.IsBusy, "disconnect cancellation");
        Throws(delegate { buffer.AddFrame(pixels, 0.05); }, "out of order rejected");
        Throws(delegate { buffer.AddFrame(new byte[1], 4); }, "wrong frame size rejected");
        Throws(delegate { new CaptureBuffer(640, 360, 15, 5, 2, 1024); }, "budget enforced before allocation");
        Throws(delegate { new CaptureBuffer(1, 1, 15, double.NaN, 2, 1024); }, "invalid settings rejected");
        var extended = new CaptureBuffer(1, 1, 15, 5, 2, 4096, 4);
        for (int i = 0; i < 75; i++) extended.AddFrame(pixels, i / 15.0);
        Check(extended.TryTrigger(5, 4), "boss duration override");
        for (int i = 75; i < 135; i++) extended.AddFrame(pixels, i / 15.0);
        Check(extended.TryComplete(7) == null, "boss continues past ordinary end");
        Check(extended.TryComplete(8.99) == null, "boss awaits final readback watermark");
        var bossClip = extended.TryComplete(9);
        Check(bossClip.Count == 135 && bossClip.EndTime == 9, "five pre plus four post seconds");
        for (int i = 135; i < 240; i++) Check(extended.AddFrame(pixels, i / 15.0), "record while longer clip retained");
        Check(bossClip.GetTimestamp(0) == 0 && bossClip.GetTimestamp(134) == 134 / 15.0, "extended clip retains endpoints");
        bossClip.Release();
        Check(extended.TryTrigger(16), "ordinary event after boss");
        var ordinary = extended.TryComplete(18);
        Check(ordinary != null && ordinary.EndTime == 18, "ordinary default duration preserved");
        ordinary.Release(); extended.ClearHistory();
        Check(extended.FreeFrames == 211, "extended pool fully reclaimed");
        Throws(delegate { extended.TryTrigger(20, 4.01); }, "oversized override rejected");
        Throws(delegate { extended.TryTrigger(20, double.NaN); }, "NaN override rejected");
        Throws(delegate { extended.TryTrigger(20, -1); }, "negative override rejected");
        Throws(delegate { new CaptureBuffer(1, 1, 15, 5, 2, 4096, 1); }, "maximum must cover default");
        Throws(delegate { new CaptureBuffer(640, 360, 15, 5, 2, 192L * 1024 * 1024, 5); }, "extended allocation obeys memory budget");
        Console.WriteLine("PASS: " + checks + " assertions (capture ownership, timing, bounded memory, cancellation)");
    }
}
