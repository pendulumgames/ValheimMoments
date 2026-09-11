using System;
using System.Text;

namespace ValheimMoments
{
    internal static class RelayProtocol
    {
        internal const int ChunkBytes = 16384, MaxBytes = 10 * 1024 * 1024, MaxPacketChars = 24000;
        internal static bool ValidId(string id) { Guid parsed; return id != null && id.Length == 32 && Guid.TryParseExact(id, "N", out parsed); }
        internal static bool ValidKind(string kind) { return kind == "manual" || kind == "boss" || kind == "loot" || kind == "death" || kind == "discovery" || kind == "special"; }
        internal static string Text(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)); }
        internal static string ReadText(string value)
        {
            if (value == null || value.Length > 12000) return null;
            try { string text = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value)); return text.Length <= 2000 ? text : null; }
            catch { return null; }
        }
    }

    // One fixed-size reservation. Exact next offsets make duplicate chunks harmless,
    // and out-of-order/oversized data cannot grow storage or complete a partial file.
    internal sealed class RelayBuffer
    {
        internal readonly string Id, Kind, Message;
        internal readonly byte[] Bytes;
        internal int Received { get; private set; }
        internal RelayBuffer(string id, string kind, string message, int size, int limit)
        {
            if (!RelayProtocol.ValidId(id) || !RelayProtocol.ValidKind(kind) || message == null || message.Length > 2000 ||
                size < 20 || size > Math.Min(limit, RelayProtocol.MaxBytes)) throw new ArgumentException("Invalid relay offer");
            Id = id; Kind = kind; Message = message; Bytes = new byte[size];
        }
        internal bool Add(int offset, byte[] chunk)
        {
            if (chunk == null || chunk.Length == 0 || chunk.Length > RelayProtocol.ChunkBytes || offset != Received || chunk.Length > Bytes.Length - Received) return false;
            Buffer.BlockCopy(chunk, 0, Bytes, Received, chunk.Length); Received += chunk.Length; return true;
        }
        internal bool Complete { get { return Received == Bytes.Length; } }
        internal bool ValidWebP()
        {
            if (!Complete || Encoding.ASCII.GetString(Bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(Bytes, 8, 4) != "WEBP") return false;
            uint length = (uint)(Bytes[4] | Bytes[5] << 8 | Bytes[6] << 16 | Bytes[7] << 24);
            if ((long)length + 8 != Bytes.Length) return false;
            long offset = 12;
            while (offset < Bytes.Length)
            {
                if (offset + 8 > Bytes.Length) return false;
                int index = (int)offset + 4;
                uint size = (uint)(Bytes[index] | Bytes[index + 1] << 8 | Bytes[index + 2] << 16 | Bytes[index + 3] << 24);
                offset += 8L + size + (size & 1);
            }
            return offset == Bytes.Length;
        }
    }
}
