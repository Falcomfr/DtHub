using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core;
using DtHub.Core.Scrcpy;

namespace DtHub.App.ViewModels;

/// <summary>Page À propos : version, licences, non-affiliation.</summary>
public sealed partial class AboutViewModel : PageViewModel
{
    private readonly IDialogService _dialogs;
    private readonly IScrcpyLocator _scrcpy;

    public AboutViewModel(IDialogService dialogs, IScrcpyLocator scrcpy)
    {
        _dialogs = dialogs;
        _scrcpy = scrcpy;
    }

    public override string Title => "À propos";

    public override string Subtitle => string.Empty;

    public string ProductName => ProductInfo.Name;

    public string Version => ProductInfo.Version;

    public string ScrcpyVersion => _scrcpy.Version;

    public string RepositoryUrl => ProductInfo.RepositoryUrl;

    public string Disclaimer =>
        $"{ProductInfo.Name} est un projet indépendant, sans lien avec Genymobile, Google, "
        + "les fabricants d'appareils, ni les applications affichées à travers lui. "
        + "Toutes les marques appartiennent à leurs propriétaires respectifs.";

    public string LicenseSummary =>
        $"{ProductInfo.Name} est publié sous licence MIT. scrcpy est publié par Genymobile sous "
        + "licence Apache 2.0 et n'est pas modifié. Les outils Android sont téléchargés depuis "
        + "Google et restent soumis à leur propre contrat de licence.";

    [RelayCommand]
    private void OpenRepository() => _dialogs.OpenUrl(ProductInfo.RepositoryUrl);

    [RelayCommand]
    private void OpenScrcpy() => _dialogs.OpenUrl("https://github.com/Genymobile/scrcpy");
}
