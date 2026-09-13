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
        Show(e.Exception, Strings.Get("UnexpectedError"));
    }

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
        Log.Error(e.Exception, "Exception de tâche non observée.");
        Note(e.Exception, "Exception de tâche non observée.");
        e.SetObserved();
    }

    /// <summary>
    /// Records a fault for the report and for the panel's line. Silent
    /// if the host is not there yet: at the very start, the log is
    /// enough.
    /// </summary>
    public void Note(Exception exception, string headline)
    {
        if (_services() is { } services
            && services.GetService<DiagnosticReporter>() is { } reporter
            && Application.Current is { } application)
        {
            application.Dispatcher.InvokeAsync(() => reporter.Note(exception, headline));
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
                new ProblemWindow(dialogs, headline, reporter.Compose(headline, exception), exception.Message)
                {
                    Logs = logs,
                    Owner = _owner(),
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
