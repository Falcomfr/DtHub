namespace DtHub.Mac.Platform;

/// <summary>
/// One DT Hub at a time on Linux, as the PC's mutex and the Mac's Dock
/// give. Linux has neither a Dock nor a tray to bring back a hidden panel,
/// so the player starts DT Hub again: that second copy would open every
/// account a second time. It finds the first one's lock taken, leaves a
/// wake file the first one watches, and quits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string WakeFile = "wake";

    private readonly FileStream _lock;
    private readonly FileSystemWatcher _watcher;

    private SingleInstance(FileStream held, string folder)
    {
        _lock = held;
        _watcher = new FileSystemWatcher(folder, WakeFile);
        _watcher.Created += (_, e) =>
        {
            // Deleted so the next start creates it again: only a creation wakes.
            TryDelete(e.FullPath);
            Woken?.Invoke(this, EventArgs.Empty);
        };
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>A later start asked for the panel. Raised off the UI thread.</summary>
    public event EventHandler? Woken;

    /// <summary>
    /// This copy's claim, or null when another copy holds it, which has
    /// then been woken.
    /// </summary>
    public static SingleInstance? TryClaim(string folder)
    {
        _ = Directory.CreateDirectory(folder);
        var wake = Path.Combine(folder, WakeFile);

        try
        {
            // FileShare.None is an exclusive flock on Unix, released by the system if the holder dies.
            var held = new FileStream(
                Path.Combine(folder, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            TryDelete(wake);

            return new SingleInstance(held, folder);
        }
        catch (IOException)
        {
            // Another copy holds the lock: it is woken, and this one leaves.
            TryDelete(wake);
            File.Create(wake).Dispose();

            return null;
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _lock.Dispose();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Left for the next wake, which deletes it first.
        }
    }
}
