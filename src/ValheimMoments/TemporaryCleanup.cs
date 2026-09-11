using System;
using System.IO;
using System.Threading.Tasks;

namespace ValheimMoments
{
    internal static class TemporaryCleanup
    {
        // Only old, exact mod-generated names in the two private transient areas.
        // No recursion, no Saved/Clips access, and no traversal through junctions.
        internal static Task Start(string pluginDirectory)
        {
            return Task.Run(() => {
                try
                {
                    DateTime cutoff = DateTime.UtcNow.AddHours(-24);
                    string relay = Path.Combine(pluginDirectory, "RelayTemp");
                    if (SafeDirectory(relay)) foreach (var path in Directory.EnumerateFiles(relay))
                    {
                        Guid id;
                        if (Path.GetExtension(path) == ".webp" && Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out id)) DeleteOld(path, cutoff);
                    }
                    string raids = Path.Combine(pluginDirectory, "RaidTemp");
                    if (!SafeDirectory(raids)) return;
                    foreach (var directory in Directory.EnumerateDirectories(raids))
                    {
                        Guid id;
                        if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out id) || !SafeDirectory(directory)) continue;
                        foreach (var path in Directory.EnumerateFiles(directory))
                        {
                            string name = Path.GetFileName(path);
                            foreach (string prefix in new[] { "opening.webp", "ending.webp", "combined.webp" })
                            {
                                bool owned = name == prefix || name == prefix + ".partial";
                                if (name.StartsWith(prefix + ".", StringComparison.Ordinal) && name.EndsWith(".partial", StringComparison.Ordinal))
                                {
                                    string suffix = name.Substring(prefix.Length + 1);
                                    owned |= suffix.Length == 40 && Guid.TryParseExact(suffix.Substring(0, 32), "N", out id);
                                }
                                if (owned) DeleteOld(path, cutoff);
                            }
                        }
                        if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            });
        }
        private static bool SafeDirectory(string path) { return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0; }
        private static void DeleteOld(string path, DateTime cutoff)
        {
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
        }
    }
}
