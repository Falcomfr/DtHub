using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core;

namespace DtHub.App.ViewModels;

/// <summary>
/// Coquille de l'application : navigation latérale et page courante.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly SessionOrchestrator _orchestrator;

    public ShellViewModel(
        HomeViewModel home,
        ProfilesViewModel profiles,
        AppsViewModel apps,
        DevicesViewModel devices,
        SettingsViewModel settings,
        AboutViewModel about,
        SessionOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;

        Pages = [home, profiles, apps, devices, settings, about];
        Settings = settings;
        _currentPage = home;

        // Le raccourci dédié doit ouvrir les paramètres même quand la fenêtre
        // principale est en arrière-plan.
        _orchestrator.SettingsRequested += (_, _) =>
            System.Windows.Application.Current?.Dispatcher.Invoke(() => Navigate(settings));
    }

    public ObservableCollection<PageViewModel> Pages { get; }

    /// <summary>Page des paramètres, atteinte aussi par raccourci.</summary>
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private PageViewModel _currentPage;

    public string ProductName => ProductInfo.Name;

    public string ProductVersion => ProductInfo.Version;

    [RelayCommand]
    private void Navigate(PageViewModel page)
    {
        if (page is null || ReferenceEquals(page, CurrentPage))
        {
            return;
        }

        CurrentPage = page;
        _ = page.OnActivatedAsync();
    }

    /// <summary>Charge la première page au démarrage.</summary>
    public Task InitializeAsync() => CurrentPage.OnActivatedAsync();
}
