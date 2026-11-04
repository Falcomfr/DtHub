using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace DtHub.Mac.Pc;

/// <summary>
/// WPF's blocking ShowDialog, which the PC's dialog contract is built on:
/// the caller gets its answer on the next line. Avalonia only offers it
/// asynchronously, so the UI thread runs a nested loop until the window
/// closes, which is what WPF does underneath.
/// </summary>
internal static class Modal
{
    public static Window? Owner =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .FirstOrDefault(w => w.IsActive && w.IsVisible)
        ?? (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static T Wait<T>(Func<Task<T>> show)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread.Invoke(() => Wait(show));
        }

        var task = show();

        if (!task.IsCompleted)
        {
            using var done = new CancellationTokenSource();
            _ = task.ContinueWith(_ => done.Cancel(), TaskScheduler.Default);
            Dispatcher.UIThread.MainLoop(done.Token);
        }

        return task.GetAwaiter().GetResult();
    }

    /// <summary>Shows a window modally over the active one and waits for it.</summary>
    public static T? Show<T>(Window window) =>
        Wait(() => Owner is { IsVisible: true } owner ? window.ShowDialog<T?>(owner) : ShowAlone<T>(window));

    private static Task<T?> ShowAlone<T>(Window window)
    {
        var closed = new TaskCompletionSource<T?>();
        window.Closed += (_, _) => closed.TrySetResult(default);
        window.Show();
        return closed.Task;
    }
}
