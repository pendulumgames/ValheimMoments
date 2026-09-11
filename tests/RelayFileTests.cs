using System;
using System.IO;
using System.Threading;
using ValheimMoments;

internal static class RelayFileTests
{
    private static int checks;
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
    private static byte[] Container(int size)
    {
        var data = new byte[size];
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, data, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(size - 8), 0, data, 4, 4);
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("WEBPJUNK"), 0, data, 8, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(size - 20), 0, data, 16, 4);
        return data;
    }
    private static int Wait(RelayFile file)
    {
        int result = 0;
        if (!SpinWait.SpinUntil(() => (result = file.Poll()) != 0, 5000)) throw new Exception("Disk worker timed out");
        return result;
    }
    private static void Cleanup(RelayFile file)
    {
        file.Dispose();
        Check(file.Cleanup.Wait(5000) && !File.Exists(file.FilePath), "Owned temporary file cleaned after workers release it");
    }
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "ValheimMoments-DiskTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string id = Guid.NewGuid().ToString("N"), eventId = Guid.NewGuid().ToString("N");
        string original = Path.Combine(root, "original.webp");
        File.WriteAllBytes(original, Container(40000));
        try
        {
            bool rejected = false;
            try { new RelayFile(root, id, "boss", "Boss", RelayProtocol.MaxBytes + 1, int.MaxValue); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Disk reservation retains hard transfer limit");
            rejected = false;
            try { new RelayFile(root, id, "boss", "Boss", 20, 20, "../../invalid"); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Malformed shared event identity rejected before disk work");
            Check(RelayFile.Inspect(original).GetAwaiter().GetResult() == 40000, "Offer inspection returns size without reserving full clip");
            Check(RelayFile.Inspect(Path.Combine(root, "missing")).GetAwaiter().GetResult() == 0, "Unreadable source fails safely");
            var file = new RelayFile(root, id, "boss", "Boss", 40000, 40000, eventId, true);
            Check(file.EventId == eventId && file.PersonalFirst && Path.GetFileNameWithoutExtension(file.FilePath) != id, "Metadata retained; received ID cannot choose disk filename");
            Check(!File.Exists(file.FilePath), "Offer alone does not touch disk");
            int offset = 0;
            while (offset < 40000)
            {
                var bytes = RelayFile.ReadChunk(original, offset, 40000).GetAwaiter().GetResult();
                Check(bytes != null && bytes.Length <= RelayProtocol.ChunkBytes, "Source reader has one bounded chunk");
                Check(!file.BeginAdd(offset + 1, bytes), "Out-of-order disk chunk rejected");
                Check(file.BeginAdd(offset, bytes), "Next disk chunk admitted");
                Check(!file.BeginAdd(offset, bytes) && file.Received == offset, "No second worker or ACK advancement before polling");
                bytes[0] ^= 255; // Caller mutation cannot corrupt the owned worker buffer.
                int result = Wait(file); offset += bytes.Length;
                Check(file.Received == offset && result == (offset == 40000 ? 2 : 1), "Only persisted validated bytes advance ACK");
            }
            Check(file.Complete && File.ReadAllBytes(file.FilePath)[0] == (byte)'R', "Worker snapshot survived caller mutation");
            Check(!file.BeginAdd(40000, new byte[1]), "Complete transfer cannot append");
            Cleanup(file);
            foreach (int corrupt in new[] { 0, 4, 8, 16 })
            {
                var bad = Container(20); bad[corrupt] = 255;
                file = new RelayFile(root, id, "manual", "Manual", 20, 20);
                Check(file.BeginAdd(0, bad) && Wait(file) == -1 && !file.Complete, "Malformed RIFF never completes");
                Check(!file.BeginAdd(0, Container(20)), "Failed transfer is terminal"); Cleanup(file);
            }
            file = new RelayFile(root, id, "loot", "Loot", 20, 20);
            Check(file.BeginAdd(0, Container(20)), "Cancellation race begins worker"); Cleanup(file);
            Check(file.Poll() == 0 && !file.BeginAdd(0, Container(20)), "Disposed transfer cannot resume");
            file = new RelayFile(root, id, "loot", "Loot", 20, 20);
            File.WriteAllText(file.FilePath, "unrelated");
            Check(file.BeginAdd(0, Container(20)) && Wait(file) == -1 && File.ReadAllText(file.FilePath) == "unrelated", "CreateNew refuses to overwrite existing path");
            file.Dispose();
            Check(file.Cleanup.Wait(5000) && File.ReadAllText(file.FilePath) == "unrelated", "Cleanup preserves a path this transfer did not create");
            File.Delete(file.FilePath);
            Check(RelayFile.ReadChunk(original, -1, 40000).GetAwaiter().GetResult() == null, "Negative offset fails closed");
            Check(RelayFile.ReadChunk(original, 40000, 40000).GetAwaiter().GetResult() == null, "EOF does not produce a transfer chunk");
            File.WriteAllBytes(original, Container(20));
            Check(RelayFile.ReadChunk(original, 0, 40000).GetAwaiter().GetResult() == null, "Changed source length aborts transfer");
        }
        finally { File.Delete(original); Directory.Delete(root); }
        Console.WriteLine("PASS: " + checks + " bounded disk relay, container validation and cleanup assertions.");
    }
}
