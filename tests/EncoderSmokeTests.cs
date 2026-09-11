using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using ValheimMoments;
using ValheimMoments.Core;

internal static class EncoderSmokeTests
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
    public static int Main(string[] args)
    {
        string exe = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);
        var buffer = new CaptureBuffer(640, 360, 15, 5, 2, 192L * 1024 * 1024, 4);
        byte[] pixels = new byte[640 * 360 * 4];
        for (int frame = 0; frame < 135; frame++)
        {
            if (frame == 75) Check(buffer.TryTrigger(5, 4), "Trigger failed");
            for (int y = 0; y < 360; y++)
                for (int x = 0; x < 640; x++)
                {
                    int i = (y * 640 + x) * 4;
                    pixels[i] = (byte)(x * 255 / 639);
                    pixels[i + 1] = (byte)(y * 255 / 359);
                    pixels[i + 2] = (byte)(frame * 2);
                    pixels[i + 3] = 255;
                    if (x >= (frame * 5) % 580 && x < (frame * 5) % 580 + 60 && y >= 130 && y < 230)
                    { pixels[i] = 255; pixels[i + 1] = 255; pixels[i + 2] = 255; }
                }
            buffer.AddFrame(pixels, frame / 15.0);
        }
        var clip = buffer.TryComplete(9);
        Check(clip.Count == 135, "Wrong input count");
        Console.WriteLine(EncoderClient.Encode(clip, exe, output, 640, 360, 80, false, CancellationToken.None));
        string cancelledOutput = output + ".cancelled.webp";
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            bool failed = false;
            try { EncoderClient.Encode(clip, exe, cancelledOutput, 640, 360, 80, false, cancelled.Token); }
            catch (Exception) { failed = true; }
            Check(failed && !File.Exists(cancelledOutput), "Cancellation did not stop helper");
            Console.WriteLine("PASS: cancelled encoding safely terminated");
        }
        clip.Release();
        // Independent RIFF structure/timestamp verification (does not call ImageMagick).
        using (var reader = new BinaryReader(File.OpenRead(output)))
        {
            Check(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "RIFF", "Missing RIFF");
            Check(reader.ReadUInt32() + 8 == reader.BaseStream.Length, "RIFF length mismatch");
            Check(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "WEBP", "Missing WEBP");
            int count = 0, duration = 0;
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                string tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
                uint length = reader.ReadUInt32();
                long next = reader.BaseStream.Position + length + (length & 1);
                if (tag == "ANMF")
                {
                    byte[] header = reader.ReadBytes(16);
                    duration += header[12] | (header[13] << 8) | (header[14] << 16);
                    count++;
                }
                reader.BaseStream.Position = next;
            }
            Check(count == 135, "Expected 135 animation frames, got " + count);
            Check(duration == 9000, "Expected 9000ms, got " + duration);
            Console.WriteLine("PASS: independent RIFF check: 135 frames, 9000ms");
        }
        var info = new ProcessStartInfo(exe, "--verify \"" + output + "\"")
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true };
        using (var process = Process.Start(info))
        {
            string decoded = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            // ImageMagick's reader rounds WebP frame delays to centiseconds.
            // Exact millisecond timing is asserted from the RIFF container above.
            Check(process.ExitCode == 0 && decoded.Contains("frames=135 width=640 height=360"), "Decode failed: " + decoded);
            Console.WriteLine("PASS: full decode: " + decoded.Trim());
        }
        // Truncated input must fail without producing a WebP.
        string invalidOutput = output + ".invalid.webp";
        info = new ProcessStartInfo(exe, "\"" + invalidOutput + "\"")
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardError = true };
        using (var process = Process.Start(info))
        {
            process.StandardInput.Close();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Check(process.ExitCode != 0 && !File.Exists(invalidOutput), "Truncated input was accepted");
            Console.WriteLine("PASS: truncated input safely rejected: " + error.Trim());
        }
        var sampled = new CaptureBuffer(16, 16, 15, 5, 3, 1024 * 1024);
        var sample = new byte[16 * 16 * 4];
        for (int f = 0; f < 450; f++)
        {
            if (f == 150) Check(sampled.TryTriggerCloseCall(10, new CloseCallTimeline(), 15), "Sampled trigger failed");
            for (int p = 0; p < sample.Length; p += 4)
            { sample[p] = (byte)f; sample[p + 1] = (byte)(f / 256 * 120); sample[p + 3] = 255; }
            sampled.AddFrame(sample, f / 15.0);
        }
        var sampledClip = sampled.TryComplete(30);
        string sampledOutput = output + ".close-call.webp";
        try { EncoderClient.Encode(sampledClip, exe, sampledOutput, 16, 16, 100, false, CancellationToken.None); }
        finally { sampledClip.Release(); }
        using (var reader = new BinaryReader(File.OpenRead(sampledOutput)))
        {
            reader.BaseStream.Position = 12;
            int duration = 0;
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                string tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
                uint length = reader.ReadUInt32();
                long next = reader.BaseStream.Position + length + (length & 1);
                if (tag == "ANMF")
                { byte[] header = reader.ReadBytes(16); duration += header[12] | header[13] << 8 | header[14] << 16; }
                reader.BaseStream.Position = next;
            }
            Check(duration == 10000, "Sampled close-call duration was " + duration);
        }
        info = new ProcessStartInfo(exe, "--verify \"" + sampledOutput + "\"")
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true };
        using (var process = Process.Start(info))
        {
            string decoded = process.StandardOutput.ReadToEnd(); process.WaitForExit();
            Check(process.ExitCode == 0 && decoded.Contains("width=16 height=16"), "Sampled decode failed: " + decoded);
        }
        Console.WriteLine("PASS: sampled close-call WebP fully decoded, exact 10000ms RIFF duration");
        string joinedOutput = output + ".joined.webp";
        string joined = EncoderClient.Compose(exe, sampledOutput, sampledOutput, joinedOutput, 100, CancellationToken.None);
        Check(joined.Contains("duration_ms=20000") && File.Exists(joinedOutput), "Composition worker handoff failed");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); bool rejected = false;
            try { EncoderClient.Compose(exe, sampledOutput, sampledOutput, output + ".cancelled-join.webp", 100, cancelled.Token); }
            catch (OperationCanceledException) { rejected = true; }
            Check(rejected && !File.Exists(output + ".cancelled-join.webp"), "Cancelled composition created output");
        }
        Console.WriteLine("PASS: composition client handoff, exact 20000ms duration and pre-cancelled worker exclusion");
        return 0;
    }
}
