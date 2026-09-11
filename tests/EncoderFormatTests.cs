using System;
using System.IO;
using System.Diagnostics;
using Imazen.WebP;

internal static class EncoderFormatTests
{
    public static void Main(string[] args)
    {
        string output = Path.Combine(args[1], Guid.NewGuid().ToString("N") + ".webp");
        var info = new ProcessStartInfo(args[0], "\"" + output + "\"") {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
        using (var process = Process.Start(info))
        {
            using (var writer = new BinaryWriter(process.StandardInput.BaseStream))
            {
                writer.Write(0x31434556); writer.Write(16); writer.Write(16); writer.Write(2); writer.Write(100); writer.Write(true);
                foreach (int duration in new[] { 37, 113 })
                {
                    writer.Write(duration);
                    for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                    {
                        writer.Write((byte)(duration == 37 && y < 8 ? 255 : 0));
                        writer.Write((byte)(duration == 113 ? 255 : 0));
                        writer.Write((byte)(duration == 37 && y >= 8 ? 255 : 0)); writer.Write((byte)255);
                    }
                }
            }
            string error = process.StandardError.ReadToEnd(); process.StandardOutput.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new Exception(error);
        }
        try
        {
            using (var decoder = new AnimDecoder(File.ReadAllBytes(output)))
            {
                var first = decoder.GetNextFrame(); var second = decoder.GetNextFrame();
                if (first.DurationMs != 37 || second.DurationMs != 113 || decoder.HasMoreFrames()) throw new Exception("Irregular final duration not preserved");
                // Decoder returns BGRA: flipped top is blue and bottom is red.
                int bottom = (15 * 16) * 4;
                if (first.Pixels[0] < 180 || first.Pixels[2] > 80 || first.Pixels[bottom + 2] < 180 || first.Pixels[bottom] > 80 || second.Pixels[1] < 180)
                    throw new Exception("RGBA channel mapping or vertical flip incorrect");
            }
            Console.WriteLine("PASS: RGBA colors, vertical flip, 37ms/113ms frame durations and full animation decode.");
            string composed = output + ".composed.webp";
            try
            {
                if (Compose(args[0], output, output, composed) != 0) throw new Exception("Composition failed");
                using (var decoder = new AnimDecoder(File.ReadAllBytes(composed)))
                {
                    int count = 0, total = 0;
                    while (decoder.HasMoreFrames())
                    {
                        var frame = decoder.GetNextFrame();
                        int expected = count % 2 == 0 ? 37 : 113;
                        if (frame.DurationMs != expected) throw new Exception("Segment boundary timing incorrect");
                        if (count % 2 == 0 ? frame.Pixels[0] < 180 || frame.Pixels[2] > 80 : frame.Pixels[1] < 180)
                            throw new Exception("Segment boundary colors/BGRA incorrect");
                        count++; total += frame.DurationMs;
                    }
                    if (count != 4 || total != 300) throw new Exception("Combined count/duration incorrect");
                }
                byte[] original = File.ReadAllBytes(composed);
                if (Compose(args[0], output, output, composed) == 0 || !Equal(original, File.ReadAllBytes(composed)))
                    throw new Exception("Existing output was overwritten");
                if (Compose(args[0], output, output, output) == 0) throw new Exception("Input overwrite accepted");
                string bad = output + ".bad"; string rejected = output + ".rejected.webp";
                try
                {
                    byte[] malicious = File.ReadAllBytes(output);
                    // Encoder's leading VP8X width field: declare a pathological canvas.
                    malicious[24] = 255; malicious[25] = 255; malicious[26] = 255;
                    File.WriteAllBytes(bad, malicious);
                    if (Compose(args[0], bad, output, rejected) == 0 || File.Exists(rejected))
                        throw new Exception("Oversized canvas accepted");
                    File.WriteAllBytes(bad, new byte[] { 1, 2, 3 });
                    if (Compose(args[0], output, bad, rejected) == 0 || File.Exists(rejected))
                        throw new Exception("Truncated segment accepted");
                    if (Directory.GetFiles(args[1], "*.partial").Length != 0) throw new Exception("Composition leaked partial output");
                }
                finally { if (File.Exists(bad)) File.Delete(bad); }
                Console.WriteLine("PASS: segment composition timing/colors, full decode, overwrite protection, malformed/canvas rejection and partial cleanup.");
            }
            finally { if (File.Exists(composed)) File.Delete(composed); }
        }
        finally { File.Delete(output); }
    }
    private static int Compose(string helper, string first, string second, string output)
    {
        var start = new ProcessStartInfo(helper, "--compose \"" + first + "\" \"" + second + "\" \"" + output + "\" 100")
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(start))
        {
            var errors = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEnd(); process.WaitForExit(); errors.GetAwaiter().GetResult();
            return process.ExitCode;
        }
    }
    private static bool Equal(byte[] a, byte[] b)
    { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
}
