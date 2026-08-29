using CommunityToolkit.Mvvm.ComponentModel;

namespace DtHub.App.ViewModels;

/// <summary>
/// Base des pages de navigation. Porte l'état d'occupation et le message
/// d'erreur, présents sur presque toutes les pages.
/// </summary>
public abstract partial class PageViewModel : ObservableObject
{
    /// <summary>Titre affiché en haut de la page et dans la barre latérale.</summary>
    public abstract string Title { get; }

    /// <summary>Une phrase expliquant à quoi sert la page.</summary>
    public virtual string Subtitle => string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Message d'erreur ou d'avertissement affiché dans la page.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Vrai si un message est en cours d'affichage.</summary>
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    /// <summary>Appelée à chaque fois que la page devient visible.</summary>
    public virtual Task OnActivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));

    partial void OnIsBusyChanged(bool value) => OnBusyChanged(value);

    /// <summary>
    /// Signalé quand l'état d'occupation change. Les pages dérivées s'en
    /// servent pour réévaluer ce qui doit être grisé.
    /// </summary>
    protected virtual void OnBusyChanged(bool isBusy)
    {
    }

    /// <summary>
    /// Exécute un travail long en tenant l'état d'occupation à jour et en
    /// transformant les erreurs en message affichable.
    /// </summary>
    protected async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = null;

        try
        {
            await work(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // L'utilisateur ou la fermeture de la page a interrompu : rien à dire.
        }
        catch (Core.Adb.AdbException exception)
        {
            StatusMessage = exception.UserMessage;
        }
        catch (Core.Dependencies.DependencyProvisioningException exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
