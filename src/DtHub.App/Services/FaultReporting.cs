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
/// Ce qu'on fait d'une faute que personne n'a rattrapée.
///
/// Quatre portes y mènent : le fil d'interface, le domaine, une tâche dont
/// personne n'attendait le résultat, et le garde-fou du démarrage. Elles
/// vivaient au milieu du point d'entrée de l'application, avec la séquence de
/// démarrage, celle d'arrêt et la place unique, quatre métiers qui ne se
/// parlent pas. Ici, elles se lisent d'un bloc.
///
/// La classe ne connaît ni l'hôte ni les fenêtres : elle les demande au moment
/// où elle en a besoin. C'est ce qui lui permet de servir aussi quand la
/// construction du conteneur a échoué, cas où le message qu'elle affiche est le
/// seul que la personne verra.
/// </summary>
internal sealed class FaultReporting
{
    private readonly Func<IServiceProvider?> _services;
    private readonly Func<Window?> _owner;

    /// <param name="services">
    /// Le conteneur, ou <c>null</c> tant qu'il n'existe pas.
    /// </param>
    /// <param name="owner">
    /// La fenêtre qui doit porter la boîte, ou <c>null</c> s'il n'y en a pas de
    /// visible.
    /// </param>
    public FaultReporting(Func<IServiceProvider?> services, Func<Window?> owner)
    {
        _services = services;
        _owner = owner;
    }

    /// <summary>Branche les trois gardes qui ne passent pas par le démarrage.</summary>
    public void Arm(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        application.DispatcherUnhandledException += OnDispatcherException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // L'interface reste vivante : une erreur d'affichage ne doit pas
        // fermer les fenêtres de jeu.
        e.Handled = true;
        Show(e.Exception, Strings.Get("UnexpectedError"));
    }

    /// <summary>
    /// Une faute que personne n'a rattrapée. Le processus s'arrête après :
    /// ouvrir une fenêtre ici n'aboutirait pas toujours, mais la retenir permet
    /// au rapport du prochain démarrage de la porter.
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
    /// Une tâche a échoué sans que personne n'attende son résultat. C'est par
    /// là que passent ADB, scrcpy et le réseau, et cela ne se voyait pas.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Exception de tâche non observée.");
        Note(e.Exception, "Exception de tâche non observée.");
        e.SetObserved();
    }

    /// <summary>
    /// Retient une faute pour le rapport et pour la ligne du panneau. Muet si
    /// l'hôte n'est pas encore là : au tout début, le journal suffit.
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
    /// Montre une faute et donne de quoi la raconter.
    ///
    /// La boîte d'avant affichait le chemin du dossier de journaux et un bouton
    /// « OK ». Le message de l'exception, qui dit ce qui s'est passé, n'y
    /// atteignait jamais l'écran, et il n'y avait rien à envoyer à personne.
    ///
    /// Le repli sur une boîte simple est gardé : si la fenêtre de signalement
    /// ne peut pas s'ouvrir, ce qui arrive quand la faute vient du démarrage
    /// lui-même, il vaut mieux une phrase que rien.
    /// </summary>
    public void Show(Exception exception, string headline)
    {
        Log.Fatal(exception, "{Headline}", headline);

        var services = _services();

        // Pas par le conteneur : quand c'est sa construction qui a échoué, il
        // n'y a rien à lui demander, et c'est justement le cas où le message
        // qui suit est le seul que la personne verra. AppPaths ne fait que
        // calculer des chemins, il ne crée rien.
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
            // La fenêtre de signalement a échoué à son tour. On ne repart pas
            // dans le même chemin : la boîte du système, elle, s'ouvrira.
            Log.Error(second, "La fenêtre de signalement n'a pas pu s'ouvrir.");
        }

        // La boîte du système, en dernier recours. Elle ne montrait que le
        // titre : « Le démarrage a échoué », sans dire de quoi. Le message de
        // l'exception est le seul indice quand le journal n'a pas pu naître,
        // ce qui est précisément le cas d'un dossier de données inaccessible.
        MessageBox.Show(
            headline
                + Environment.NewLine + Environment.NewLine + exception.Message
                + (logs is { Length: > 0 } ? Strings.Format("ErrorDetailsInLogs", logs) : string.Empty),
            ProductInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>Ce que rend l'appel, ou <c>null</c> s'il échoue.</summary>
    private static string? Quietly(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Un dernier recours qui lève laisserait la personne sans message
            // du tout : c'est le seul endroit où le silence est le bon choix.
            return null;
        }
    }
}
