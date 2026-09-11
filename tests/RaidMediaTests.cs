using System;
using System.IO;
using System.Threading;
using ValheimMoments;

internal static class RaidMediaTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "moments-raid-" + Guid.NewGuid().ToString("N"));
        var media = new RaidMedia(root);
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            var task = media.Start(token => {
                File.WriteAllText(media.Opening, "opening");
                using (var reader = File.Open(media.Opening, FileMode.Open, FileAccess.Read, FileShare.None))
                { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); }
                return "done";
            });
            if (!entered.Wait(5000)) throw new TimeoutException();
            bool busy = false;
            try { media.Start(token => "overlap"); } catch (InvalidOperationException) { busy = true; }
            if (!busy) throw new Exception("Concurrent raid worker accepted");
            media.Dispose();
            if (media.Cleanup.IsCompleted || !File.Exists(media.Opening)) throw new Exception("Cleanup raced worker");
            release.Set(); task.GetAwaiter().GetResult(); media.Cleanup.GetAwaiter().GetResult();
            if (File.Exists(media.Opening)) throw new Exception("Opening not reclaimed");
            bool stopped = false;
            try { media.Start(token => "late"); } catch (ObjectDisposedException) { stopped = true; }
            if (!stopped) throw new Exception("Disposed workspace restarted");
        }
        var second = new RaidMedia(root);
        second.Start(token => {
            File.WriteAllText(second.Ending, "ending");
            File.WriteAllText(second.Combined + "." + Guid.NewGuid().ToString("N") + ".partial", "interrupted");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(second.Ending), "unrelated.txt"), "keep");
            return "done";
        }).GetAwaiter().GetResult();
        second.Dispose(); second.Cleanup.GetAwaiter().GetResult(); second.Dispose();
        string folder = Path.GetDirectoryName(second.Ending);
        if (File.Exists(second.Ending) || Directory.GetFiles(folder, "*.partial").Length != 0 || !File.Exists(Path.Combine(folder, "unrelated.txt")))
            throw new Exception("Owned cleanup or unrelated-file protection failed");
        File.Delete(Path.Combine(folder, "unrelated.txt")); Directory.Delete(folder); Directory.Delete(root);
        Console.WriteLine("Raid media: 5 worker ownership/cleanup assertions passed.");
    }
}
