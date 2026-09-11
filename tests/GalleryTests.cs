using System;
using System.IO;
using ValheimMoments;

internal static class GalleryTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "moments-gallery-" + Guid.NewGuid().ToString("N"));
        var gallery = new MomentGallery(root); var session = new object();
        var kept = gallery.Add("manual", "Moment", "Recorder", session, true, false);
        kept.Acknowledgement = new object();
        string original = gallery.PathFor(kept); File.WriteAllText(original, "animation");
        if (!gallery.Pin(kept.Id)) throw new Exception("Cannot pin active encoding");
        gallery.Flush().GetAwaiter().GetResult();
        if (!File.Exists(original)) throw new Exception("Pin moved active file");
        gallery.Complete(kept, "Uploaded"); gallery.Flush().GetAwaiter().GetResult();
        if (File.Exists(original) || !File.Exists(gallery.PathFor(kept)) || !kept.Saved) throw new Exception("Completed pin not moved");
        var expired = gallery.Add("death", "Death", "Recorder", session, true, false);
        File.WriteAllText(gallery.PathFor(expired), "animation"); gallery.Complete(expired, "Uploaded");
        gallery.Flush().GetAwaiter().GetResult();
        if (!File.Exists(gallery.PathFor(expired))) throw new Exception("Keep grace missing");
        expired.Completed = DateTime.UtcNow.AddSeconds(-31); gallery.Flush().GetAwaiter().GetResult();
        if (File.Exists(gallery.PathFor(expired)) || gallery.Pin(expired.Id)) throw new Exception("Expired clip available");
        var retry = gallery.Add("manual", "Moment", "Recorder", session, true, false);
        File.WriteAllText(gallery.PathFor(retry), "animation"); gallery.Complete(retry, "Unknown");
        retry.Completed = DateTime.UtcNow.AddMinutes(-10);
        if (gallery.BeginRetry(retry.Id, new object(), true, true) != null || gallery.BeginRetry(retry.Id, session, false, true) != null ||
            gallery.BeginRetry(retry.Id, session, true, false) != null) throw new Exception("Retry authority or duplicate guard missing");
        for (int i = 0; i < 3; i++)
        {
            retry.Completed = DateTime.UtcNow.AddMinutes(-10);
            if (gallery.BeginRetry(retry.Id, session, true, true) == null) throw new Exception("Valid retry rejected");
            if (gallery.BeginRetry(retry.Id, session, true, true) != null) throw new Exception("Concurrent retry accepted");
            gallery.Complete(retry, "Unknown");
            if (gallery.BeginRetry(retry.Id, session, true, true) != null) throw new Exception("Backoff ignored");
        }
        retry.Completed = DateTime.UtcNow.AddMinutes(-10);
        if (gallery.BeginRetry(retry.Id, session, true, true) != null) throw new Exception("Unbounded retry");
        gallery.Flush().GetAwaiter().GetResult();
        var reloaded = new MomentGallery(root);
        if (reloaded.Snapshot().Length != 3 || !reloaded.Snapshot()[2].Pinned || reloaded.Snapshot()[0].Origin != null)
            throw new Exception("Persistence lost pin or retained session authority");
        if (MomentGallery.ValidLink("https://discord.com.evil.test/channels/1/2/3") || MomentGallery.ValidLink("file:///secret"))
            throw new Exception("Unsafe link accepted");
        var preview = MomentGallery.Preview(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, 1, 2, false);
        if (preview.Length != 256 * 144 * 4) throw new Exception("Unbounded preview");
        if (preview[(92 * 4)] != 4 || preview[((143 * 256 + 92) * 4)] != 1) throw new Exception("Preview row orientation differs from WebP");
        gallery.WritePreview(kept, preview);
        File.Delete(gallery.PreviewPath(kept.Id));
        string relayRoot = Path.Combine(root, "RelayTemp"), raidsRoot = Path.Combine(root, "RaidTemp");
        string raidDirectory = Path.Combine(raidsRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(relayRoot); Directory.CreateDirectory(raidDirectory);
        string stale = Path.Combine(relayRoot, Guid.NewGuid().ToString("N") + ".webp"), unknown = Path.Combine(raidDirectory, "user-file.txt");
        string opening = Path.Combine(raidDirectory, "opening.webp"), recent = Path.Combine(raidDirectory, "ending.webp");
        foreach (string path in new[] { stale, unknown, opening, recent }) File.WriteAllText(path, "data");
        foreach (string path in new[] { stale, unknown, opening }) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        TemporaryCleanup.Start(root).GetAwaiter().GetResult();
        if (File.Exists(stale) || File.Exists(opening) || !File.Exists(unknown) || !File.Exists(recent) || !File.Exists(gallery.PathFor(kept)))
            throw new Exception("Restart cleanup ownership/age failure");
        File.Delete(unknown); File.Delete(recent); Directory.Delete(raidDirectory); Directory.Delete(raidsRoot); Directory.Delete(relayRoot);
        foreach (var entry in gallery.Snapshot()) { string path = gallery.PathFor(entry); if (File.Exists(path)) File.Delete(path); }
        File.Delete(Path.Combine(root, "index.bin")); Directory.Delete(Path.Combine(root, "Recovery")); Directory.Delete(Path.Combine(root, "Saved")); Directory.Delete(root);
        Console.WriteLine("Gallery: pin/cleanup ownership, grace, bounded retries, authority, persistence and link tests passed.");
    }
}
