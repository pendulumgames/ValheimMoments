using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ValheimMoments.Core;

namespace ValheimMoments
{
    internal static class EncoderClient
    {
        internal static string Compose(string exe, string opening, string ending, string output, int quality, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var info = new ProcessStartInfo(exe, "--compose \"" + opening + "\" \"" + ending + "\" \"" + output + "\" " + quality)
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(exe) };
            using (var process = new Process { StartInfo = info })
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeout.Token))
            {
                process.Start();
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                using (linked.Token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch { } }))
                try
                {
                    process.WaitForExit(); linked.Token.ThrowIfCancellationRequested();
                    string error = stderr.GetAwaiter().GetResult();
                    if (process.ExitCode != 0 || !File.Exists(output)) throw new IOException("Segment composition failed: " + error.Trim());
                    return stdout.GetAwaiter().GetResult().Trim();
                }
                finally
                {
                    try { if (!process.HasExited) process.Kill(); } catch { }
                    // RaidMedia cleanup must not run while this child still owns files.
                    process.WaitForExit();
                }
            }
        }
        // This entire method runs on a worker. Only owned managed frame arrays cross
        // the process boundary. Unity state and logging are handled by the caller.
        internal static string Encode(CaptureBuffer.Clip clip, string exe, string output,
            int width, int height, int quality, bool flip, CancellationToken cancellation)
        {
            var info = new ProcessStartInfo(exe, "\"" + output + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(exe)
            };
            using (var process = new Process { StartInfo = info })
            {
                process.Start();
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeout.Token))
                using (linked.Token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch { } }))
                {
                    try
                    {
                        using (var writer = new BinaryWriter(process.StandardInput.BaseStream))
                        {
                            writer.Write(0x31434556);
                            writer.Write(width); writer.Write(height); writer.Write(clip.Count);
                            writer.Write(quality); writer.Write(flip);
                            double start = clip.GetTimestamp(0);
                            int previousEnd = 0;
                            for (int i = 0; i < clip.Count; i++)
                            {
                                linked.Token.ThrowIfCancellationRequested();
                                double end = i + 1 < clip.Count ? clip.GetTimestamp(i + 1) : clip.EndTime;
                                int endMs = Math.Max(previousEnd + 1, (int)Math.Round((end - start) * 1000));
                                writer.Write(endMs - previousEnd);
                                writer.Write(clip.GetPixels(i));
                                previousEnd = endMs;
                            }
                        }
                        process.WaitForExit();
                        linked.Token.ThrowIfCancellationRequested();
                        string error = stderr.GetAwaiter().GetResult();
                        if (process.ExitCode != 0) throw new IOException("Encoder exit " + process.ExitCode + ": " + error.Trim());
                        if (!File.Exists(output)) throw new IOException("Encoder produced no output");
                        return stdout.GetAwaiter().GetResult().Trim();
                    }
                    finally
                    {
                        try { if (!process.HasExited) process.Kill(); } catch { }
                        try { process.WaitForExit(5000); } catch { }
                        try { if (File.Exists(output + ".partial")) File.Delete(output + ".partial"); } catch { }
                    }
                }
            }
        }
    }
}
