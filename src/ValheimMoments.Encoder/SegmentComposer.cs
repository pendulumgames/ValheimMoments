using System;
using System.IO;
using System.Reflection;
using System.Text;
using Imazen.WebP;

internal static class SegmentComposer
{
    private sealed class Segment
    {
        internal byte[] Bytes;
        internal int Width, Height, Frames, Duration;
    }
    // Worker process only. Decode one frame at a time; never collect raw segments.
    internal static void Compose(string opening, string ending, string output, int quality)
    {
        if (quality < 1 || quality > 100) throw new ArgumentOutOfRangeException("quality");
        string target = Path.GetFullPath(output);
        if (string.Equals(target, Path.GetFullPath(opening), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, Path.GetFullPath(ending), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Output must differ from inputs");
        var first = Read(opening); var second = Read(ending);
        int count = first.Frames + second.Frames;
        if (first.Width != second.Width || first.Height != second.Height || count > 1800 ||
            first.Duration + second.Duration > 60000 || (long)first.Width * first.Height * 4 * count > 256L * 1024 * 1024)
            throw new InvalidDataException("Incompatible or oversized segments");
        var settings = new WebPEncoderConfig().SetQuality(quality).SetMethod(3).SetMultiThreaded(false);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        bool owned = false;
        try
        {
            using (var images = new AnimEncoder(first.Width, first.Height, loopCount: 0))
            {
                int timestamp = 0, lastDuration = 0;
                foreach (var segment in new[] { first, second })
                using (var decoder = new AnimDecoder(segment.Bytes))
                {
                    if (decoder.Info.Width != segment.Width || decoder.Info.Height != segment.Height)
                        throw new InvalidDataException("Decoded canvas differs from header");
                    int frames = 0, elapsed = 0;
                    while (decoder.HasMoreFrames())
                    {
                        var frame = decoder.GetNextFrame();
                        if (++frames > segment.Frames || frame.DurationMs < 1 || frame.DurationMs > 60000 ||
                            frame.Pixels.Length != segment.Width * segment.Height * 4)
                            throw new InvalidDataException("Decoded segment exceeds declared budget");
                        // AnimDecoder returns BGRA, while the capture pipe supplies RGBA.
                        images.AddFrame(frame.Pixels, first.Width * 4, WebPPixelFormat.Bgra, timestamp, settings);
                        lastDuration = frame.DurationMs; timestamp += lastDuration; elapsed += lastDuration;
                        if (elapsed > segment.Duration) throw new InvalidDataException("Decoded timing exceeds segment");
                    }
                    if (frames != segment.Frames || elapsed != segment.Duration)
                        throw new InvalidDataException("Decoded timing/count differs from segment");
                }
                var last = typeof(AnimEncoder).GetField("_lastDuration", BindingFlags.NonPublic | BindingFlags.Instance);
                if (last == null) throw new MissingFieldException("Pinned animation duration contract changed");
                last.SetValue(images, lastDuration);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { owned = true; byte[] bytes = images.Assemble(); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                File.Move(temporary, target); owned = false;
                Console.WriteLine("segments=2 frames={0} duration_ms={1} bytes={2}", count, timestamp, new FileInfo(target).Length);
            }
        }
        finally { if (owned) { try { File.Delete(temporary); } catch { } } }
    }

    private static Segment Read(string path)
    {
        var segment = new Segment();
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length < 30 || stream.Length > 10 * 1048576) throw new InvalidDataException("Segment file size out of range");
            using (var reader = new BinaryReader(stream))
            {
                segment.Bytes = reader.ReadBytes((int)stream.Length);
                if (segment.Bytes.Length != stream.Length) throw new EndOfStreamException();
            }
        }
        using (var reader = new BinaryReader(new MemoryStream(segment.Bytes)))
        {
            if (Text(reader, 4) != "RIFF" || reader.ReadUInt32() != segment.Bytes.Length - 8 || Text(reader, 4) != "WEBP")
                throw new InvalidDataException("Invalid segment RIFF");
            bool canvas = false;
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                if (reader.BaseStream.Length - reader.BaseStream.Position < 8) throw new InvalidDataException("Truncated chunk");
                string tag = Text(reader, 4); uint size = reader.ReadUInt32();
                long next = reader.BaseStream.Position + size + (size & 1);
                if (next > reader.BaseStream.Length) throw new InvalidDataException("Truncated chunk body");
                if (tag == "VP8X")
                {
                    if (canvas || size != 10) throw new InvalidDataException("Invalid canvas");
                    byte[] data = reader.ReadBytes(10);
                    segment.Width = 1 + Number(data, 4); segment.Height = 1 + Number(data, 7); canvas = true;
                    if (segment.Width < 16 || segment.Width > 1920 || segment.Height < 16 || segment.Height > 1080)
                        throw new InvalidDataException("Canvas exceeds capture bounds");
                }
                if (tag == "ANMF")
                {
                    if (!canvas || size < 16) throw new InvalidDataException("Invalid frame header");
                    byte[] data = reader.ReadBytes(16); int duration = Number(data, 12);
                    if (Number(data, 0) * 2L + Number(data, 6) + 1 > segment.Width ||
                        Number(data, 3) * 2L + Number(data, 9) + 1 > segment.Height)
                        throw new InvalidDataException("Frame rectangle exceeds canvas");
                    if (duration < 1 || duration > 60000 || ++segment.Frames > 1800 ||
                        (segment.Duration += duration) > 60000 ||
                        (long)segment.Width * segment.Height * 4 * segment.Frames > 256L * 1024 * 1024)
                        throw new InvalidDataException("Segment exceeds frame/timing budget");
                }
                reader.BaseStream.Position = next;
            }
            if (!canvas || segment.Frames == 0) throw new InvalidDataException("Expected animated segment");
        }
        return segment;
    }
    private static string Text(BinaryReader reader, int count) { return Encoding.ASCII.GetString(reader.ReadBytes(count)); }
    private static int Number(byte[] bytes, int offset) { return bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16; }
}
