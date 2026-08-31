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

    /// <summary>
    /// Ce que la liste déroulante montre à cet instant : les branches, les
    /// quêtes d'une rubrique, ou les résultats d'une recherche.
    /// </summary>
    public ObservableCollection<QuestNode> Nodes { get; } = [];

    /// <summary>Nombre de quêtes du catalogue rangées dans chaque rubrique.</summary>
    private readonly Dictionary<int, int> _sectionCounts = [];

    /// <summary>Rubrique ouverte, ou zéro à la racine.</summary>
    private int _section;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _questTitle = string.Empty;

    [ObservableProperty]
    private string _chainText = string.Empty;

    /// <summary>
    /// Place de la quête dans sa chaîne de prérequis, « 6 / 7 ». Affichée au
    /// pied, entre la précédente et la suivante : c'est de cette chaîne qu'elle
    /// parle, et non du succès.
    /// </summary>
    [ObservableProperty]
    private string _chainStep = string.Empty;

    [ObservableProperty]
    private bool _hasQuest;

    /// <summary>Ce qu'on lit tant qu'aucune quête n'est ouverte.</summary>
    [ObservableProperty]
    private string _placeholder = "Cherchez une quête, ou dépliez la liste.";

    /// <summary>Vrai quand la liste déroulante est ouverte.</summary>
    [ObservableProperty]
    private bool _isListOpen;

    /// <summary>Où l'on se trouve dans l'arbre, affiché au-dessus de la liste.</summary>
    [ObservableProperty]
    private string _breadcrumb = string.Empty;

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

        CountSections();
        ShowRoot();
    }

    /// <summary>
    /// Compte les quêtes par rubrique à partir du catalogue, et non des
    /// nombres du site : celui-ci compte aussi ce qui n'est pas une quête, et
    /// proposerait des rubriques qui s'ouvriraient sur rien.
    ///
    /// Sur toutes les rubriques auxquelles la quête appartient, comme la liste
    /// les montre : le site range « Le dragon d'Astrub » dans ses quêtes
    /// principales comme dans celles d'Astrub.
    /// </summary>
    private void CountSections()
    {
        _sectionCounts.Clear();

        foreach (var quest in _catalog.Catalog.Quests)
        {
            foreach (var section in quest.SectionIds)
            {
                _sectionCounts[section] = _sectionCounts.GetValueOrDefault(section) + 1;
            }
        }
    }

    partial void OnQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Effacer la recherche ramène là où l'on était, plutôt qu'à la
            // racine : on efface souvent pour corriger une faute de frappe.
            ShowSection(_section);
            return;
        }

        ShowSearch(value);
    }

    /// <summary>Ouvre la liste sur ce qui était affiché.</summary>
    public void OpenList()
    {
        if (Nodes.Count == 0)
        {
            ShowRoot();
        }

        IsListOpen = true;
    }

    /// <summary>Le premier niveau : les grandes branches.</summary>
    public void ShowRoot()
    {
        _section = 0;
        Breadcrumb = string.Empty;

        Nodes.Clear();
        Nodes.Add(new QuestNode(
            QuestNodeKind.Branch,
            "Quêtes",
            Nombre(_catalog.Catalog.Quests.Count),
            Id: RootSection));
        Nodes.Add(new QuestNode(
            QuestNodeKind.Pending,
            "Donjons",
            "bientôt"));
    }

    /// <summary>
    /// Le contenu d'une rubrique. À la racine des quêtes, ce sont les autres
    /// rubriques ; plus bas, ce sont les quêtes elles-mêmes.
    /// </summary>
    public void ShowSection(int section)
    {
        _section = section;

        if (section == 0)
        {
            ShowRoot();
            return;
        }

        Nodes.Clear();

        if (section == RootSection)
        {
            Breadcrumb = "Quêtes";
            Nodes.Add(new QuestNode(QuestNodeKind.Back, "Retour", Id: 0));

            foreach (var branch in Branches())
            {
                Nodes.Add(branch);
            }

            return;
        }

        Breadcrumb = $"Quêtes  ›  {NameOf(section)}";
        Nodes.Add(new QuestNode(QuestNodeKind.Back, "Retour", Id: RootSection));

        AddBySuccess(_catalog.InSection(section));
    }

    /// <summary>
    /// Résultats d'une recherche. Elle porte sur toutes les quêtes, quelle que
    /// soit la rubrique ouverte : on cherche un nom, pas un rangement.
    /// </summary>
    private void ShowSearch(string query)
    {
        Breadcrumb = "Recherche";

        Nodes.Clear();

        AddGrouped(_catalog.Search(query, limit: 60));

        if (Nodes.Count == 0)
        {
            Nodes.Add(new QuestNode(QuestNodeKind.Pending, "Aucune quête de ce nom"));
        }
    }

    /// <summary>
    /// Rubriques qui contiennent au moins une quête, dans l'ordre où le site
    /// les range.
    ///
    /// Le catalogue les rend déjà ordonnées ; les reclasser par nombre de
    /// quêtes, comme on le faisait, revenait à ignorer l'ordre du site après
    /// être allé le chercher.
    /// </summary>
    private IEnumerable<QuestNode> Branches() =>
        _catalog.Catalog.Sections
            .Where(s => s.Id != RootSection && _sectionCounts.GetValueOrDefault(s.Id) > 0)
            .Select(s => new QuestNode(
                QuestNodeKind.Branch,
                $"{s.Name} ({_sectionCounts[s.Id]})",
                LevelRange(_catalog.InSection(s.Id)),
                Id: s.Id));

    /// <summary>
    /// Plage de niveaux d'un ensemble de quêtes, ou <c>null</c> quand aucune
    /// n'en porte.
    ///
    /// Le site ne renseigne le niveau que sur cent dix-sept quêtes sur sept
    /// cent quatre-vingt-deux, et sur plusieurs zones aucune. Afficher une
    /// plage tirée d'une seule quête sur vingt-trois la ferait passer pour la
    /// plage de la zone : quand rien n'est connu, on ne dit rien, et le nombre
    /// de quêtes sur lequel elle repose est rappelé dès qu'il est partiel.
    /// </summary>
    private static string? LevelRange(IReadOnlyList<QuestSummary> quests)
    {
        List<int> levels = [.. quests.Where(q => q.Level > 0).Select(q => q.Level)];

        if (levels.Count == 0)
        {
            return null;
        }

        var span = levels.Min() == levels.Max()
            ? Text(levels.Min())
            : $"{Text(levels.Min())} - {Text(levels.Max())}";

        return levels.Count == quests.Count
            ? $"niveau {span}"
            : $"niveau {span} (sur {Text(levels.Count)})";
    }

    private static string Text(int value) =>
        value.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>
    /// Ajoute les quêtes en les rangeant sous leur rubrique, dans l'ordre du
    /// site.
    ///
    /// Une liste de soixante titres sans repère ne se lit pas : l'intertitre
    /// dit d'où vient ce qu'on voit. Il est inutile dans une rubrique déjà
    /// ouverte, où il répéterait le fil d'Ariane à chaque ligne.
    /// </summary>
    private void AddGrouped(IReadOnlyList<QuestSummary> quests, bool skipHeaders = false)
    {
        if (skipHeaders)
        {
            foreach (var quest in quests)
            {
                Nodes.Add(ToNode(quest));
            }

            return;
        }

        var rank = _catalog.Catalog.Sections
            .Select((s, i) => (s.Id, Index: i))
            .ToDictionary(x => x.Id, x => x.Index);

        var groups = quests
            .GroupBy(q => q.SectionId)
            .OrderBy(g => rank.GetValueOrDefault(g.Key, int.MaxValue));

        foreach (var group in groups)
        {
            Nodes.Add(new QuestNode(QuestNodeKind.Header, NameOf(group.Key), Nombre(group.Count())));

            foreach (var quest in group)
            {
                Nodes.Add(ToNode(quest));
            }
        }
    }

    /// <summary>
    /// Ajoute les quêtes d'une rubrique en les rangeant sous leur succès.
    ///
    /// C'est ainsi que le site les présente, et c'est ainsi qu'on les joue :
    /// une quête isolée dit rarement à quoi elle sert. Les quêtes qu'aucun
    /// succès ne réclame viennent ensuite, sous un intertitre qui ne prétend
    /// pas en être un.
    /// </summary>
    private void AddBySuccess(IReadOnlyList<QuestSummary> quests)
    {
        // Dans l'ordre du site, qui est celui d'une progression. L'ordre
        // alphabétique mettait « Épilogue hivernal » avant « L'hiver arrive ».
        var rank = _catalog.Catalog.SuccessOrder
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.Ordinal);

        var groups = quests
            .Where(q => q.SuccessName.Length > 0)
            .GroupBy(q => q.SuccessName, StringComparer.Ordinal)
            .OrderBy(g => rank.GetValueOrDefault(g.Key, int.MaxValue))
            .ThenBy(g => g.Key, StringComparer.CurrentCulture);

        foreach (var group in groups)
        {
            // Dans l'ordre où l'on y joue, que le site publie sous la forme
            // « Étape 6/7 ». Ce rang situe la quête dans sa chaîne de
            // prérequis et non dans son succès, mais c'est le seul ordre de jeu
            // disponible, et l'ordre alphabétique n'en est pas un.
            List<QuestSummary> ordered =
            [
                .. group
                    .OrderBy(q => q.ChainStep == 0 ? int.MaxValue : q.ChainStep)
                    .ThenBy(q => q.Title, StringComparer.CurrentCulture),
            ];

            Nodes.Add(new QuestNode(
                QuestNodeKind.Header,
                $"{group.Key} ({ordered.Count})",
                LevelRange(ordered)));

            foreach (var quest in ordered)
            {
                Nodes.Add(ToNode(quest));
            }
        }

        List<QuestSummary> loose = [.. quests.Where(q => q.SuccessName.Length == 0)];

        if (loose.Count == 0)
        {
            return;
        }

        // L'intertitre ne s'affiche que s'il sépare de quelque chose : dans une
        // rubrique dont aucune quête n'a de succès, il ne coifferait rien.
        if (Nodes.Any(n => n.Kind == QuestNodeKind.Header))
        {
            Nodes.Add(new QuestNode(
                QuestNodeKind.Header,
                $"Hors succès ({loose.Count})",
                LevelRange(loose)));
        }

        foreach (var quest in loose)
        {
            Nodes.Add(ToNode(quest));
        }
    }

    private QuestNode ToNode(QuestSummary quest) => new(
        QuestNodeKind.Quest,
        quest.Title,
        quest.Level > 0 ? $"niveau {quest.Level}" : null,
        Quest: quest);

    private string NameOf(int section) =>
        _catalog.Catalog.Sections.FirstOrDefault(s => s.Id == section)?.Name ?? "Rubrique";

    private static string Nombre(int count) => count == 1 ? "1 quête" : $"{count} quêtes";

    /// <summary>Catégorie qui range toutes les quêtes du site.</summary>
    private const int RootSection = 7;

    /// <summary>
    /// Donne suite à un clic dans la liste. Rend la quête à ouvrir, ou null
    /// quand le clic ne fait que déplier une branche.
    /// </summary>
    public QuestSummary? Activate(QuestNode? node)
    {
        if (node is null || !node.IsEnabled)
        {
            return null;
        }

        switch (node.Kind)
        {
            case QuestNodeKind.Back:
                Query = string.Empty;
                ShowSection(node.Id);
                return null;

            case QuestNodeKind.Branch:
                Query = string.Empty;
                ShowSection(node.Id);
                return null;

            case QuestNodeKind.Quest when node.Quest is { } quest:
                SetCurrent(quest);
                IsListOpen = false;
                return quest;

            default:
                return null;
        }
    }

    /// <summary>Retient la quête ouverte, pour le pied de fenêtre et le titre.</summary>
    public void SetCurrent(QuestSummary quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        CurrentUrl = quest.Url;
        QuestTitle = quest.Title;
        ChainText = string.Empty;
        ChainStep = string.Empty;
        HasQuest = true;

        // La page suivante n'est pas encore chargée : garder les étapes de la
        // précédente afficherait un objectif qui n'a plus rien à voir.
        _steps = [];
        HasSteps = false;
        PreviousQuest = null;
        NextQuest = null;
        SetStep(-1);
    }

    /// <summary>Étapes repérées dans la page ouverte.</summary>
    private IReadOnlyList<string> _steps = [];

    [ObservableProperty]
    private int _stepIndex = -1;

    /// <summary>« Étape 3 / 7 », ou rien quand la page n'a pas d'étape.</summary>
    [ObservableProperty]
    private string _stepText = string.Empty;

    /// <summary>Ce qu'il y a à faire à cette étape, en une ligne.</summary>
    [ObservableProperty]
    private string _stepDetail = string.Empty;

    [ObservableProperty]
    private bool _hasSteps;

    [ObservableProperty]
    private bool _canGoPreviousStep;

    [ObservableProperty]
    private bool _canGoNextStep;

    /// <summary>Quête suivante de la chaîne, quand la page en annonce une.</summary>
    [ObservableProperty]
    private QuestLink? _nextQuest;

    /// <summary>Quête précédente de la chaîne.</summary>
    [ObservableProperty]
    private QuestLink? _previousQuest;

    /// <summary>
    /// Ce que la page vient de livrer : ses blocs structurés et ses étapes.
    /// </summary>
    public void SetPage(string? introHtml, string? chainHtml, IReadOnlyList<string> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var facts = QuestPageParser.ParseFacts(introHtml);
        var chain = QuestPageParser.ParseChain(chainHtml);

        // Le site nomme « étape » la place d'une quête dans son succès, et
        // nous nommons « étape » un objectif dans la page. Afficher les deux
        // mots côte à côte rendait le bandeau illisible.
        //
        // Ce nombre ne compte pas non plus les quêtes du succès, et le coller
        // au nom du succès le laissait croire. « Nettoyage express » annonce
        // 6/7 alors que « De la caillasse plein les poches » n'a que trois
        // quêtes, ce que confirme la liste du site ; et sa quête précédente,
        // « La chasse aux sorcières », relève d'un autre succès. Le rang situe
        // la quête dans sa chaîne de prérequis, laquelle traverse plusieurs
        // succès. Il part donc au pied, entre les deux quêtes de la chaîne, où
        // il ne prête plus à confusion.
        ChainText = facts.Success ?? string.Empty;
        ChainStep = facts.HasChain ? $"{facts.StepNumber} / {facts.StepCount}" : string.Empty;

        PreviousQuest = chain.PreviousQuest;
        NextQuest = chain.NextQuest;

        _steps = steps;
        HasSteps = steps.Count > 0;

        SetStep(steps.Count > 0 ? 0 : -1);
    }

    /// <summary>Le défilement a changé d'étape, ou l'utilisateur en a choisi une.</summary>
    public void SetStep(int index)
    {
        StepIndex = index;

        var total = _steps.Count;

        StepText = index >= 0 && total > 0
            ? $"Étape {index + 1} / {total}"
            : string.Empty;

        StepDetail = index >= 0 && index < total ? _steps[index] : string.Empty;

        CanGoPreviousStep = index > 0;
        CanGoNextStep = index >= 0 && index < total - 1;
    }

    /// <summary>
    /// On suit un lien de chaîne. Le titre est repris tout de suite : la page
    /// met une seconde à répondre, et un bandeau qui garde l'ancien nom pendant
    /// ce temps laisse croire que le clic n'a rien fait.
    /// </summary>
    public void Follow(QuestLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        CurrentUrl = link.Url;
        QuestTitle = link.Title;
        ChainText = string.Empty;
        ChainStep = string.Empty;
        HasQuest = true;
        IsListOpen = false;

        _steps = [];
        HasSteps = false;
        PreviousQuest = null;
        NextQuest = null;
        SetStep(-1);
    }

    /// <summary>
    /// Rang de la première ligne qui se choisit, en enjambant les intertitres.
    /// Rend -1 si la liste n'offre rien.
    /// </summary>
    public int FirstSelectable()
    {
        for (var i = 0; i < Nodes.Count; i++)
        {
            if (Nodes[i].IsEnabled)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Étape visée par une flèche, ou -1 s'il n'y a nulle part où aller.</summary>
    public int StepTarget(int direction)
    {
        var target = StepIndex + direction;

        return target >= 0 && target < _steps.Count ? target : -1;
    }

    /// <summary>
    /// Le navigateur embarqué n'a pas pu se mettre en route. Le cas le plus
    /// probable est un moteur WebView2 absent, sur un Windows qui n'a pas été
    /// mis à jour depuis longtemps.
    /// </summary>
    public void ReportViewFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        HasQuest = false;
        Placeholder =
            "Le composant d'affichage web de Windows n'a pas pu démarrer.\n"
            + "Ouvrez la page dans votre navigateur avec le bouton en bas.\n\n"
            + exception.Message;
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
