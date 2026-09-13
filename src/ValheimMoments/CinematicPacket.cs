using System;
using System.Collections.Generic;
using System.IO;

namespace ValheimMoments
{
    // Optional, private RIFF chunks carry extra camera footage through the existing
    // bounded relay. Ordinary WebP players ignore them. Strip them before Discord.
    // Each nested file is flat: recursive packets and caller-controlled paths are forbidden.
    internal static class CinematicPacket
    {
        internal const uint Chunk = 0x49434d56; // VMCI
        internal const int Limit = 10 * 1048576;
        internal sealed class Shot { internal byte Kind; internal byte[] Pixels; }
        internal static byte[] Strip(byte[] input, out List<Shot> shots)
        {
            shots = new List<Shot>();
            if (input.Length < 20 || input.Length > Limit) throw new InvalidDataException("Camera packet size");
            using (var stream = new MemoryStream(input, false))
            using (var reader = new BinaryReader(stream))
            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output))
            {
                if (reader.ReadUInt32() != 0x46464952 || reader.ReadUInt32() != input.Length - 8 || reader.ReadUInt32() != 0x50424557)
                    throw new InvalidDataException("Camera packet header");
                writer.Write(0x46464952); writer.Write(0); writer.Write(0x50424557);
                var seen = new HashSet<byte>();
                while (stream.Position < stream.Length)
                {
                    if (stream.Length - stream.Position < 8) throw new InvalidDataException("Camera packet chunk");
                    uint tag = reader.ReadUInt32(), size = reader.ReadUInt32();
                    if ((long)size + (size & 1) > stream.Length - stream.Position) throw new InvalidDataException("Camera packet length");
                    if (tag == Chunk)
                    {
                        if (size < 22 || shots.Count == 2) throw new InvalidDataException("Camera packet count");
                        byte version = reader.ReadByte(), kind = reader.ReadByte();
                        if (version != 1 || kind < 1 || kind > 4 || !seen.Add(kind)) throw new InvalidDataException("Camera packet role");
                        byte[] data = reader.ReadBytes((int)size - 2);
                        // Validate without allowing nested cinematic chunks.
                        List<Shot> nested;
                        if (ContainsCamera(data)) throw new InvalidDataException("Nested camera packet");
                        Strip(data, out nested);
                        shots.Add(new Shot { Kind = kind, Pixels = data });
                    }
                    else { writer.Write(tag); writer.Write(size); writer.Write(reader.ReadBytes((int)size)); if ((size & 1) != 0) writer.Write((byte)0); }
                    if ((size & 1) != 0 && reader.ReadByte() != 0) throw new InvalidDataException("Camera packet padding");
                }
                writer.Flush(); output.Position = 4; writer.Write((int)output.Length - 8); return output.ToArray();
            }
        }
        private static bool ContainsCamera(byte[] data)
        {
            if (data.Length < 12) return false;
            using (var s = new MemoryStream(data, false)) using (var r = new BinaryReader(s))
            {
                s.Position = 12;
                while (s.Length - s.Position >= 8)
                {
                    uint tag = r.ReadUInt32(), n = r.ReadUInt32();
                    if (tag == Chunk) return true;
                    long next = s.Position + n + (n & 1); if (next > s.Length) return false; s.Position = next;
                }
                return false;
            }
        }
        internal static void Attach(string file, IList<Shot> shots)
        {
            if (shots.Count == 0) return;
            List<Shot> ignored;
            byte[] primary = Strip(File.ReadAllBytes(file), out ignored);
            using (var output = new MemoryStream()) using (var writer = new BinaryWriter(output))
            {
                writer.Write(primary); var seen = new HashSet<byte>();
                foreach (var shot in shots)
                {
                    if (seen.Count == 2 || !seen.Add(shot.Kind) || shot.Kind < 1 || shot.Kind > 4) continue;
                    if (shot.Pixels == null || output.Length + shot.Pixels.Length + 10 + (shot.Pixels.Length & 1) > Limit) continue;
                    if (ContainsCamera(shot.Pixels)) continue;
                    Strip(shot.Pixels, out ignored);
                    writer.Write(Chunk); writer.Write(shot.Pixels.Length + 2); writer.Write((byte)1); writer.Write(shot.Kind);
                    writer.Write(shot.Pixels); if ((shot.Pixels.Length & 1) != 0) writer.Write((byte)0);
                }
                output.Position = 4; writer.Write((int)output.Length - 8);
                // Encoder output is not yet offered to relay or gallery cleanup.
                string temporary = file + ".partial";
                try { File.WriteAllBytes(temporary, output.ToArray()); File.Replace(temporary, file, null); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        internal static string Label(byte kind)
        { return kind == 1 ? "Enemy arrival" : kind == 2 ? "Enemy aftermath" : kind == 3 ? "Raid opening" : "Biome discovery"; }
    }
}
