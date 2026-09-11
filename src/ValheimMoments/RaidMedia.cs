using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ValheimMoments
{
    // One raid's private intermediate workspace. Disposal cancels the worker and
    // waits for its file handles to close before cleaning known owned filenames.
    // Main thread owns Start/Dispose; workers receive only paths and a token.
    internal sealed class RaidMedia : IDisposable
    {
        internal readonly string Opening, Ending, Combined;
        private readonly string directory;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private Task worker;
        private bool disposed, created;
        internal Task Cleanup { get; private set; } = Task.CompletedTask;

        internal RaidMedia(string root)
        {
            directory = Path.Combine(Path.GetFullPath(root), Guid.NewGuid().ToString("N"));
            Opening = Path.Combine(directory, "opening.webp");
            Ending = Path.Combine(directory, "ending.webp");
            Combined = Path.Combine(directory, "combined.webp");
        }
        internal Task<string> Start(Func<CancellationToken, string> operation)
        {
            if (disposed) throw new ObjectDisposedException(nameof(RaidMedia));
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (worker != null && !worker.IsCompleted) throw new InvalidOperationException("Raid media worker already active");
            var task = Task.Run(() => {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!created)
                {
                    if (Directory.Exists(directory)) throw new IOException("Raid workspace collision");
                    Directory.CreateDirectory(directory); created = true;
                }
                return operation(cancellation.Token);
            });
            worker = task; return task;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; cancellation.Cancel();
            Cleanup = (worker ?? Task.CompletedTask).ContinueWith(task => {
                var ignored = task.Exception;
                try
                {
                    if (!created || !Directory.Exists(directory)) return;
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
                    foreach (string path in new[] { Opening, Ending, Combined })
                    {
                        Delete(path); Delete(path + ".partial");
                        // Composer uses output.<random-guid>.partial. Only these
                        // exact registered output prefixes belong to this workspace.
                        foreach (string partial in Directory.GetFiles(directory, Path.GetFileName(path) + ".*.partial"))
                        {
                            string suffix = Path.GetFileName(partial).Substring(Path.GetFileName(path).Length + 1);
                            Guid id;
                            if (suffix.Length == 40 && Guid.TryParseExact(suffix.Substring(0, 32), "N", out id)) Delete(partial);
                        }
                    }
                    // Never recurse: unknown files or directories preserve the folder.
                    if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                finally { cancellation.Dispose(); }
            }, TaskScheduler.Default);
        }
        private static void Delete(string path) { if (File.Exists(path)) File.Delete(path); }
    }
}
