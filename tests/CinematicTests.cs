using System;
using System.Collections.Generic;
using System.IO;
using ValheimMoments;

internal static class CinematicTests
{
    private static int checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    private static byte[] WebP()
    { return new byte[] { 82,73,70,70,12,0,0,0,87,69,66,80,84,69,83,84,0,0,0,0 }; }
    private static void Reject(byte[] bytes, string name)
    { try { List<CinematicPacket.Shot> shots; CinematicPacket.Strip(bytes, out shots); } catch (InvalidDataException) { checks++; return; } throw new Exception(name); }
    public static void Main(string[] args)
    {
        string file = Path.Combine(args[0], "camera-packet-test.webp");
        File.WriteAllBytes(file, WebP());
        var supplied = new List<CinematicPacket.Shot> {
            new CinematicPacket.Shot { Kind = 1, Pixels = WebP() }, new CinematicPacket.Shot { Kind = 2, Pixels = WebP() } };
        CinematicPacket.Attach(file, supplied);
        List<CinematicPacket.Shot> shots;
        byte[] clean = CinematicPacket.Strip(File.ReadAllBytes(file), out shots);
        Check(Convert.ToBase64String(clean) == Convert.ToBase64String(WebP()), "Primary footage restored exactly");
        Check(shots.Count == 2 && shots[0].Kind == 1 && shots[1].Kind == 2, "Distinct arrival and aftermath retained");
        Check(shots[0].Pixels.Length == 20, "Nested footage intact");
        Check(!File.Exists(file + ".partial"), "No partial after atomic update");
        byte[] packet = File.ReadAllBytes(file);
        supplied[0].Pixels = packet; supplied.RemoveAt(1); File.WriteAllBytes(file, WebP());
        CinematicPacket.Attach(file, supplied);
        CinematicPacket.Strip(File.ReadAllBytes(file), out shots); Check(shots.Count == 0, "Cannot attach nested packets");
        Reject(new byte[0], "Empty"); Reject(new byte[CinematicPacket.Limit + 1], "Oversize");
        var bad = (byte[])packet.Clone(); bad[4]++; Reject(bad, "Mismatched outer length");
        bad = (byte[])packet.Clone(); bad[24] = 255; Reject(bad, "Oversize chunk");
        bad = (byte[])packet.Clone(); bad[28] = 2; Reject(bad, "Unknown version");
        bad = (byte[])packet.Clone(); bad[29] = 0; Reject(bad, "Unknown role");
        bad = (byte[])packet.Clone(); bad[59] = 1; Reject(bad, "Duplicate role");
        for (int n = 0; n < packet.Length; n++)
        {
            var truncated = new byte[n]; Array.Copy(packet, truncated, n);
            Reject(truncated, "Truncated packet " + n);
        }
        supplied = new List<CinematicPacket.Shot> { new CinematicPacket.Shot { Kind = 1, Pixels = WebP() }, new CinematicPacket.Shot { Kind = 1, Pixels = WebP() } };
        CinematicPacket.Attach(file, supplied); CinematicPacket.Strip(File.ReadAllBytes(file), out shots);
        Check(shots.Count == 1, "Duplicate local shots not packed twice");
        File.Delete(file);
        Console.WriteLine("PASS: " + checks + " cinematic packet checks.");
    }
}
