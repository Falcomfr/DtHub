using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.App.Services;
using DtHub.Core.Papycha;

namespace DtHub.App.ViewModels;

/// <summary>
/// Ce que la fenêtre de quêtes affiche : la recherche, ses résultats, et la
/// quête ouverte.
/// </summary>
public sealed partial class QuestViewModel : ObservableObject
{
    private readonly QuestCatalogService _catalog;
    private readonly IDialogService _dialogs;

    public QuestViewModel(QuestCatalogService catalog, IDialogService dialogs)
    {
        _catalog = catalog;
        _dialogs = dialogs;
    }

    /// <summary>Résultats de la recherche, au fil de la frappe.</summary>
    public ObservableCollection<QuestSummary> Results { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _questTitle = string.Empty;

    [ObservableProperty]
    private string _chainText = string.Empty;

    [ObservableProperty]
    private bool _hasQuest;

    /// <summary>Ce qu'on lit tant qu'aucune quête n'est ouverte.</summary>
    [ObservableProperty]
    private string _placeholder = "Cherchez une quête par son nom.";

    /// <summary>Adresse de la page ouverte, pour la rouvrir dans le navigateur.</summary>
    public string? CurrentUrl { get; private set; }

    /// <summary>
    /// Prépare le catalogue. La fenêtre reste utilisable pendant l'indexation :
    /// elle dure quelques secondes la première fois, et rien ensuite.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var progress = new Progress<QuestIndexingProgress>(
            p => StatusText = p.Total > 0
                ? $"Indexation {p.Loaded}/{p.Total}"
                : "Indexation...");

        var catalog = await _catalog.GetAsync(progress, cancellationToken).ConfigureAwait(true);

        StatusText = catalog.Quests.Count > 0
            ? $"{catalog.Quests.Count} quêtes"
            : "Aucune quête : le site n'a pas répondu.";

        if (_catalog.LastFailure is not null && catalog.Quests.Count > 0)
        {
            StatusText += " (liste en cache)";
        }

        Refresh();
    }

    partial void OnQueryChanged(string value) => Refresh();

    private void Refresh()
    {
        Results.Clear();

        foreach (var quest in _catalog.Search(Query, limit: 40))
        {
            Results.Add(quest);
        }
    }

    /// <summary>Retient la quête ouverte, pour le pied de fenêtre et le titre.</summary>
    public void SetCurrent(QuestSummary quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        CurrentUrl = quest.Url;
        QuestTitle = quest.Title;
        ChainText = string.Empty;
        HasQuest = true;
    }

    /// <summary>Complète le titre avec ce que la page annonce, une fois chargée.</summary>
    public void SetFacts(QuestFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        ChainText = string.Join(
            "  ·  ",
            new[] { facts.StepText, facts.Success }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>Rouvre la page courante sur le site, dans le vrai navigateur.</summary>
    public void OpenInBrowser()
    {
        if (!string.IsNullOrWhiteSpace(CurrentUrl))
        {
            _dialogs.OpenUrl(CurrentUrl);
        }
    }
}
