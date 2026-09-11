using System;
using System.Diagnostics;
using System.IO;
using Imazen.WebP;
using System.Reflection;

internal static class Program
{
    public static int Main(string[] args)
    {
        string temporary = null;
        try
        {
            if (args.Length == 5 && args[0] == "--compose")
            {
                SegmentComposer.Compose(args[1], args[2], args[3], int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture));
                return 0;
            }
            if (args.Length == 2 && args[0] == "--verify")
            {
                using (var decoded = new AnimDecoder(File.ReadAllBytes(args[1])))
                {
                    int count = 0, duration = 0;
                    while (decoded.HasMoreFrames()) { var frame = decoded.GetNextFrame(); count++; duration += frame.DurationMs; }
                    Console.WriteLine("frames={0} width={1} height={2} decoder_duration_ms={3:F0}",
                        count, decoded.Info.Width, decoded.Info.Height, duration);
                }
                return 0;
            }
            if (args.Length != 1) throw new ArgumentException("Expected output WebP path");
            var watch = Stopwatch.StartNew();
            using (var input = new BinaryReader(Console.OpenStandardInput()))
            {
                if (input.ReadInt32() != 0x31434556) throw new InvalidDataException("Bad VEC1 header");
                int width = input.ReadInt32(), height = input.ReadInt32(), count = input.ReadInt32();
                int quality = input.ReadInt32();
                bool flip = input.ReadBoolean();
                if (width < 16 || width > 1920 || height < 16 || height > 1080 || count < 1 || count > 1800 || quality < 1 || quality > 100)
                    throw new InvalidDataException("Invalid capture dimensions/count/quality");
                int bytes = checked(width * height * 4);
                if ((long)bytes * count > 256L * 1024 * 1024)
                    throw new InvalidDataException("Clip exceeds encoder's 256 MiB raw-frame limit");
                var pixels = new byte[bytes];
                var settings = new WebPEncoderConfig().SetQuality(quality).SetMethod(3).SetMultiThreaded(false);
                using (var images = new AnimEncoder(width, height, loopCount: 0))
                {
                    int totalMs = 0, lastDuration = 0;
                    var encodeWatch = Stopwatch.StartNew();
                    for (int i = 0; i < count; i++)
                    {
                        int durationMs = input.ReadInt32();
                        if (durationMs < 1 || durationMs > 60000 || (totalMs += durationMs) > 60000)
                            throw new InvalidDataException("Invalid frame timing");
                        int offset = 0;
                        while (offset < bytes)
                        {
                            int n = input.Read(pixels, offset, bytes - offset);
                            if (n == 0) throw new EndOfStreamException("Incomplete frame");
                            offset += n;
                        }
                        if (flip)
                        {
                            int stride = width * 4;
                            for (int y = 0; y < height / 2; y++)
                                for (int x = 0; x < stride; x++) { int a = y * stride + x, b = (height - y - 1) * stride + x; byte value = pixels[a]; pixels[a] = pixels[b]; pixels[b] = value; }
                        }
                        images.AddFrame(pixels, width * 4, WebPPixelFormat.Rgba, totalMs - durationMs, settings);
                        lastDuration = durationMs;
                    }
                    string output = Path.GetFullPath(args[0]);
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    temporary = output + ".partial";
                    // Pinned 11.0.0 guesses the final duration from the preceding interval.
                    // Supply the actual final timestamp interval; tested against RIFF/decode.
                    var last = typeof(AnimEncoder).GetField("_lastDuration", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (last == null) throw new MissingFieldException("Pinned animation duration contract changed");
                    last.SetValue(images, lastDuration);
                    File.WriteAllBytes(temporary, images.Assemble());
                    encodeWatch.Stop();
                    File.Move(temporary, output);
                    temporary = null;
                    Console.WriteLine("frames={0} duration_ms={1} elapsed_ms={2} encode_ms={3} bytes={4} helper_peak_mib={5:F1}",
                        count, totalMs, watch.ElapsedMilliseconds, encodeWatch.ElapsedMilliseconds,
                        new FileInfo(output).Length, Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0);
                }
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 1;
        }
        finally
        {
            if (temporary != null) { try { File.Delete(temporary); } catch { } }
        }
    }
}
