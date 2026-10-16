using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

using DtHub.App.Windows;

using DtHub.Core;
using DtHub.Core.Localization;
using DtHub.Core.Storage;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace DtHub.App.Services;

/// <summary>
/// What we do with a fault that nobody caught.
///
/// Four doors lead here: the interface thread, the domain, a task
/// whose result nobody was waiting for, and the startup safety net.
/// They used to live in the middle of the application's entry point,
/// along with the startup sequence, the shutdown sequence and the
/// single-instance guard, four unrelated concerns that do not talk to
/// each other. Here, they can be read as one block.
///
/// The class knows neither the host nor the windows: it asks for them
/// at the moment it needs them. That is what lets it also serve when
/// the container's construction has failed, a case where the message
/// it shows is the only one the person will see.
/// </summary>
internal sealed class FaultReporting
{
    private readonly Func<IServiceProvider?> _services;
    private readonly Func<Window?> _owner;

    /// <param name="services">
    /// The container, or <c>null</c> for as long as it does not exist.
    /// </param>
    /// <param name="owner">
    /// The window that must carry the box, or <c>null</c> if there is
    /// no visible one.
    /// </param>
    public FaultReporting(Func<IServiceProvider?> services, Func<Window?> owner)
    {
        _services = services;
        _owner = owner;
    }

    /// <summary>
    /// Wires up the three guards that do not go through startup.
    /// </summary>
    public void Arm(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        application.DispatcherUnhandledException += OnDispatcherException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // The interface stays alive: a display error must not close
        // the game windows.
        e.Handled = true;

        // Where it was thrown rather than what it says: a message carrying
        // an index, a handle or a path made each occurrence a new fault.
        // Three frames, not the throwing method alone, which is often a
        // shared helper that two unrelated faults go through.
        var signature = $"{e.Exception.GetType().FullName} @ "
            + (e.Exception.StackTrace is { } trace
                ? string.Join(" | ", trace.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(3))
                : e.Exception.Message);

        // Once per fault and per session. A fault that comes back on every
        // layout pass or every tick of a timer opened its report again as
        // soon as it was closed, and wrote its whole trace twice a second
        // for as long as the report stayed open, until the log rolled over
        // and took the previous days with it. Same during shutdown: a
        // report there held Windows' own shutdown behind it.
        if (_pending || Quiet || !_reported.Add(signature))
        {
            var count = _repeats[signature] = _repeats.GetValueOrDefault(signature) + 1;

            if (count <= RepeatsLogged)
            {
                Log.Error(e.Exception, "Faute pendant un signalement ou répétée ({Count}) : {Signature}", count, signature);
                Note(e.Exception, Strings.Get("UnexpectedError"));
            }

            return;
        }

        // Never opened from here. The fault can arrive while WPF has
        // suspended its dispatcher, in a layout pass: the window then
        // fails, and the system box's loop raises the same fault again.
        // In 0.8.19 that stacked one window every half second. One
        // report at a time, once the dispatcher is free.
        _pending = true;
        var exception = e.Exception;

        // The exception's own trace stops at WPF's window procedure;
        // the one of this moment goes on to the code that pumped, the
        // only name a report can carry for that fault.
        Log.Error("Pile au moment de la faute :\n{Stack}", Environment.StackTrace);

        var report = e.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Queued just before the application began closing: a
                // modal report now would hold the shutdown behind it.
                if (!Quiet)
                {
                    Show(exception, Strings.Get("UnexpectedError"));
                }
                else
                {
                    // Show is what logs it: skipped, the fault itself would
                    // leave only the stack line above.
                    Log.Fatal(exception, "{Headline}", Strings.Get("UnexpectedError"));
                    Note(exception, Strings.Get("UnexpectedError"));
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // Caught here: escaping, it came back through this very
                // handler, which opened a new report, which failed again.
                Log.Error(failure, "Le signalement lui-même a échoué.");
            }
            finally
            {
                _pending = false;
            }
        });

        // The same fault swallows the message that makes the dispatcher
        // run its queue, and WPF never posts it again: nothing would
        // run, this report included, until a key is pressed. We post it
        // ourselves until the report has started.
        var thread = GetCurrentThreadId();

        _ = Task.Run(async () =>
        {
            while (report.Status == DispatcherOperationStatus.Pending)
            {
                await Task.Delay(500).ConfigureAwait(false);
                RunQueue(thread);
            }
        });
    }

    private volatile bool _pending;

    /// <summary>
    /// True once the application is closing: faults are then only logged,
    /// a report would hold the shutdown behind it.
    /// </summary>
    public bool Quiet { get; set; }

    /// <summary>The faults that have had their report, this session.</summary>
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _repeats = new(StringComparer.Ordinal);

    /// <summary>How many repeats of one fault the log keeps.</summary>
    private const int RepeatsLogged = 20;

    /// <summary>
    /// Posts to the dispatcher's hidden window of <paramref name="thread"/>
    /// the message that makes it run its queue. The name is WPF's own.
    /// </summary>
    private static void RunQueue(uint thread)
    {
        var message = RegisterWindowMessage("DispatcherProcessQueue");

        for (var window = FindWindowEx(MessageOnly, 0, null, null);
            window != 0;
            window = FindWindowEx(MessageOnly, window, null, null))
        {
            if (GetWindowThreadProcessId(window, out _) == thread)
            {
                _ = PostMessage(window, message, 0, 0);
            }
        }
    }

    private const nint MessageOnly = -3;

    // DllImport and not LibraryImport, for the reason given in DarkTitleBar.
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    /// <summary>
    /// A fault that nobody caught. The process stops afterward:
    /// opening a window here would not always succeed, but recording
    /// it lets the next startup's report carry it.
    /// </summary>
    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "Exception non interceptée.");
            Note(exception, "Exception non interceptée.");
        }
    }

    /// <summary>
    /// A task failed without anyone waiting for its result. This is
    /// where ADB, scrcpy and the network pass through, and it used to
    /// go unseen.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Observed first: this runs on the finalizer thread, where anything
        // escaping from what follows would end the process.
        e.SetObserved();
        Log.Error(e.Exception, "Exception de tâche non observée.");
        Note(e.Exception, "Exception de tâche non observée.");
    }

    /// <summary>
    /// Records a fault for the report and for the panel's line. Silent
    /// if the host is not there yet: at the very start, the log is
    /// enough.
    /// </summary>
    public void Note(Exception exception, string headline)
    {
        try
        {
            if (_services() is { } services
                && services.GetService<DiagnosticReporter>() is { } reporter
                && Application.Current is { } application)
            {
                application.Dispatcher.InvokeAsync(() => reporter.Note(exception, headline));
            }
        }
        catch (ObjectDisposedException)
        {
            // The host is gone, at the very end of shutdown: the log has it.
        }
    }


    /// <summary>
    /// Shows a fault and gives something to describe it with.
    ///
    /// The previous box showed the path to the logs folder and an
    /// "OK" button. The exception's message, which says what
    /// happened, never made it to the screen, and there was nothing
    /// to send to anyone.
    ///
    /// The fallback to a simple box is kept: if the reporting window
    /// cannot open, which happens when the fault comes from startup
    /// itself, a sentence is better than nothing.
    /// </summary>
    public void Show(Exception exception, string headline)
    {
        Log.Fatal(exception, "{Headline}", headline);

        var services = _services();

        // Not through the container: when it is its own construction
        // that failed, there is nothing to ask it, and this is
        // precisely the case where the message that follows is the
        // only one the person will see. AppPaths only computes paths,
        // it creates nothing.
        var logs = services?.GetService<IAppPaths>()?.LogsDirectory
            ?? Quietly(static () => new AppPaths().LogsDirectory);

        try
        {
            if (services?.GetService<DiagnosticReporter>() is { } reporter
                && services.GetService<IDialogService>() is { } dialogs
                && logs is { Length: > 0 })
            {
                var owner = _owner();

                // Above everything when no panel carries it: modal, it
                // disables the tabbed frame and its games, and it used to
                // open behind them, leaving nothing to click.
                new ProblemWindow(dialogs, headline, reporter.Compose(headline, exception), exception.Message)
                {
                    Logs = logs,
                    Owner = owner,
                    Topmost = owner is null,
                }.ShowDialog();

                return;
            }
        }
        catch (Exception second) when (second is not OutOfMemoryException)
        {
            // The reporting window has in turn failed. We do not go
            // down the same path again: the system's box will open.
            Log.Error(second, "La fenêtre de signalement n'a pas pu s'ouvrir.");
        }

        // The system's box, as a last resort. It used to show only
        // the title: "Startup failed", without saying of what. The
        // exception's message is the only clue when the log could not
        // come into being, which is precisely the case of an
        // inaccessible data folder.
        MessageBox.Show(
            headline
                + Environment.NewLine + Environment.NewLine + exception.Message
                + (logs is { Length: > 0 } ? Strings.Format("ErrorDetailsInLogs", logs) : string.Empty),
            ProductInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>What the call returns, or <c>null</c> if it fails.</summary>
    private static string? Quietly(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A last resort that throws would leave the person with no
            // message at all: this is the one place where silence is
            // the right choice.
            return null;
        }
    }
}
