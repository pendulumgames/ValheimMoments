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
        }
        finally { File.Delete(output); }
    }
}
