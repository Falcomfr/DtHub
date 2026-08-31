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
    [NotifyPropertyChangedFor(nameof(ShowsQuestChrome))]
    private bool _hasQuest;

    /// <summary>Ce qu'on lit tant qu'aucune quête n'est ouverte.</summary>
    [ObservableProperty]
    private string _placeholder = "Cherchez une quête, ou dépliez la liste.";

    /// <summary>
    /// Ligne à mettre en évidence quand le panneau s'ouvre : celle de la quête
    /// affichée. La sélection n'était posée qu'à la flèche du bas depuis la
    /// recherche, si bien que rouvrir la liste surlignait une quête qu'on avait
    /// quittée depuis longtemps.
    /// </summary>
    [ObservableProperty]
    private QuestNode? _selectedNode;

    /// <summary>Vrai quand la liste déroulante est ouverte.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsQuestChrome))]
    private bool _isListOpen;

    /// <summary>Vrai pendant l'indexation, pour montrer que ça travaille.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Vrai quand le bandeau d'étape et le pied de succès doivent se voir.
    ///
    /// Ils s'effacent tant que la liste est ouverte : elle prend alors toute la
    /// hauteur, et on ne consulte pas une étape et une liste en même temps.
    /// </summary>
    public bool ShowsQuestChrome => HasQuest && !IsListOpen;

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
        IsBusy = true;

        var progress = new Progress<QuestIndexingProgress>(
            p => StatusText = p.Total > 0
                ? $"Indexation {p.Loaded} / {p.Total}"
                : "Indexation…");

        var catalog = await _catalog.GetAsync(progress, cancellationToken).ConfigureAwait(true);

        IsBusy = false;

        // Une fois l'indexation faite, le compte n'apprend rien : on ne garde
        // un mot que lorsqu'il y a un incident à signaler.
        StatusText = catalog.Quests.Count == 0
            ? "Aucune quête : le site n'a pas répondu."
            : _catalog.LastFailure is not null
                ? "Le site n'a pas répondu ; liste en cache."
                : string.Empty;

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

        // Chercher sans voir les résultats n'a pas de sens : jusqu'ici taper du
        // texte reconstruisait la liste sans la déployer.
        IsListOpen = true;
    }

    /// <summary>Ouvre la liste sur ce qui était affiché.</summary>
    public void OpenList()
    {
        if (Nodes.Count == 0)
        {
            ShowRoot();
        }

        // Sans reconstruire la liste : elle garde ses succès dépliés et ce
        // qu'on y avait déroulé, on y retrouve seulement où l'on en est.
        SelectCurrent();

        IsListOpen = true;
    }

    /// <summary>
    /// Pose la sélection sur la quête affichée, si elle est dans la liste.
    /// Ne touche à rien quand elle n'y est pas : la liste montre peut-être une
    /// autre rubrique, et la vider serait pire que de ne rien surligner.
    /// </summary>
    public void SelectCurrent()
    {
        if (string.IsNullOrEmpty(CurrentUrl))
        {
            return;
        }

        SelectedNode = Nodes.FirstOrDefault(n =>
            n.Quest is { } quest
            && string.Equals(quest.Url, CurrentUrl, StringComparison.Ordinal));
    }

    /// <summary>Le premier niveau : les grandes branches.</summary>
    public void ShowRoot()
    {
        _section = 0;
        Breadcrumb = string.Empty;
        ClearBack();

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
            Breadcrumb = "Zone de Quêtes";
            SetBack(target: 0);

            foreach (var branch in Branches())
            {
                Nodes.Add(branch);
            }

            return;
        }

        Breadcrumb = $"Zone de Quêtes  ›  {NameOf(section)}";
        SetBack(target: RootSection);

        AddBySuccess(_catalog.InSection(section));
    }

    /// <summary>
    /// Rubrique où le retour ramène, quand il y a un cran au-dessus.
    ///
    /// Le retour était une ligne de la liste comme une autre : il défilait avec
    /// elle et disparaissait dès qu'on descendait dans une rubrique de soixante
    /// quêtes. Il est maintenant fixe, au-dessus de la liste.
    /// </summary>
    [ObservableProperty]
    private bool _canGoBack;

    private int _backTarget;

    private void SetBack(int target)
    {
        _backTarget = target;
        CanGoBack = true;
    }

    private void ClearBack() => CanGoBack = false;

    /// <summary>Remonte d'un cran.</summary>
    public void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        Query = string.Empty;
        ShowSection(_backTarget);
    }

    /// <summary>
    /// Résultats d'une recherche. Elle porte sur toutes les quêtes, quelle que
    /// soit la rubrique ouverte : on cherche un nom, pas un rangement.
    /// </summary>
    private void ShowSearch(string query)
    {
        Breadcrumb = "Recherche";
        ClearBack();

        Nodes.Clear();

        var found = _catalog.SearchAll(query, limit: 60);

        if (found.Zones.Count > 0)
        {
            Nodes.Add(new QuestNode(
                QuestNodeKind.Header, "Zones", Combien(found.Zones.Count, "zone", "zones")));

            foreach (var zone in found.Zones)
            {
                Nodes.Add(new QuestNode(
                    QuestNodeKind.Branch,
                    $"{QuestZoneOrder.DisplayName(zone.Name)} ({_sectionCounts.GetValueOrDefault(zone.Id)})",
                    Id: zone.Id));
            }
        }

        if (found.Successes.Count > 0)
        {
            Nodes.Add(new QuestNode(
                QuestNodeKind.Header, "Succès", Combien(found.Successes.Count, "succès", "succès")));

            // Un succès ne se choisit pas : ce qu'on veut, ce sont ses quêtes.
            // Elles suivent donc son nom, dans l'ordre où l'on y joue.
            foreach (var success in found.Successes)
            {
                List<QuestSummary> quests =
                [
                    .. InPlayOrder(_catalog.Catalog.Quests.Where(q =>
                        string.Equals(q.SuccessName, success, StringComparison.Ordinal))),
                ];

                Nodes.Add(new QuestNode(
                    QuestNodeKind.Success, $"{success} ({quests.Count})", LevelRange(quests)));

                foreach (var quest in quests)
                {
                    Nodes.Add(ToNode(quest));
                }
            }
        }

        if (found.Quests.Count > 0)
        {
            Nodes.Add(new QuestNode(QuestNodeKind.Header, "Quêtes", Nombre(found.Quests.Count)));

            foreach (var quest in found.Quests)
            {
                Nodes.Add(ToNode(quest));
            }
        }

        if (Nodes.Count == 0)
        {
            Nodes.Add(new QuestNode(QuestNodeKind.Pending, "Rien de ce nom"));
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
    private IEnumerable<QuestNode> Branches()
    {
        var zones = _catalog.Catalog.Sections
            .Where(s => s.Id != RootSection && _sectionCounts.GetValueOrDefault(s.Id) > 0)
            .OrderBy(s => QuestZoneOrder.RankOf(s.Name))
            .ThenBy(s => QuestZoneOrder.DisplayName(s.Name), StringComparer.CurrentCulture);

        var separated = false;

        foreach (var zone in zones)
        {
            // Ce qui ne relève pas de la progression vient après un intertitre,
            // pour que la liste ne mélange pas un lieu et un cheminement.
            if (!separated && QuestZoneOrder.IsExtra(zone.Name))
            {
                separated = true;

                yield return new QuestNode(QuestNodeKind.Header, QuestZoneOrder.ExtrasHeader);
            }

            var name = QuestZoneOrder.DisplayName(zone.Name);

            yield return new QuestNode(
                QuestNodeKind.Branch,
                $"{name} ({_sectionCounts[zone.Id]})",
                LevelRange(_catalog.InSection(zone.Id)),
                Id: zone.Id);
        }
    }

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

        // Une plage tirée d'une ou deux quêtes sur vingt-huit passerait pour la
        // plage de la zone. Le site ne renseigne le niveau que sur 117 quêtes
        // sur 782 : mieux vaut ne rien dire que dire à peu près.
        if (levels.Count < 3 && levels.Count < quests.Count)
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
            List<QuestSummary> ordered = [.. InPlayOrder(group)];

            Nodes.Add(new QuestNode(
                QuestNodeKind.Success,
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
        if (Nodes.Any(n => n.Kind == QuestNodeKind.Success))
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

    /// <summary>
    /// Ordre de jeu d'un ensemble de quêtes.
    ///
    /// La place dans le succès d'abord, calculée à l'indexation à partir des
    /// prérequis du site ; le rang de chaîne ensuite, pour les quêtes que la
    /// carte ne connaît pas ; le titre en dernier, pour que l'ordre soit total
    /// et toujours le même.
    ///
    /// Un seul et même ordre pour la liste et pour la navigation d'une quête à
    /// l'autre : la suivante doit être celle qu'on voit juste en dessous.
    /// </summary>
    private static IEnumerable<QuestSummary> InPlayOrder(IEnumerable<QuestSummary> quests) =>
        quests
            .OrderBy(q => q.PlayOrder == 0 ? int.MaxValue : q.PlayOrder)
            .ThenBy(q => q.ChainStep == 0 ? int.MaxValue : q.ChainStep)
            .ThenBy(q => q.Title, StringComparer.CurrentCulture);

    /// <summary>
    /// Une ligne de quête.
    ///
    /// La colonne de droite ne porte plus le niveau : le site ne le renseigne
    /// que sur cent dix-sept quêtes sur sept cent quatre-vingt-deux, et une
    /// colonne vide neuf fois sur dix ne mérite pas sa place. Elle porte les
    /// prérequis, qui en couvrent six cent treize, et qui disent quelque chose
    /// d'utile avant de partir : ce qu'il faut avoir fait.
    /// </summary>
    private QuestNode ToNode(QuestSummary quest) => new(
        QuestNodeKind.Quest,
        quest.Title,
        Quest: quest,
        Tip: quest.Prerequisites.Count > 0
            ? "À faire avant :\n" + string.Join('\n', quest.Prerequisites)
            : null);

    private string NameOf(int section) =>
        QuestZoneOrder.DisplayName(
            _catalog.Catalog.Sections.FirstOrDefault(s => s.Id == section)?.Name)
        is { Length: > 0 } name
            ? name
            : "Rubrique";

    private static string Nombre(int count) => count == 1 ? "1 quête" : $"{count} quêtes";

    /// <summary>
    /// Compte d'un intertitre de recherche. Le mot suit la nature : annoncer
    /// « 1 quête » au-dessus d'une zone ferait mentir l'intertitre juste
    /// au-dessus de ce qu'il coiffe.
    /// </summary>
    private static string Combien(int count, string singulier, string pluriel) =>
        count == 1 ? $"1 {singulier}" : $"{count} {pluriel}";

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
        HasQuest = true;

        _start = QuestStepSummary.OfStart(quest.StartPosition, quest.StartPerson);

        SetNeighbours(quest);

        // La page suivante n'est pas encore chargée : garder les étapes de la
        // précédente afficherait un objectif qui n'a plus rien à voir.
        _steps = [];
        HasSteps = false;
        SetStep(-1);
    }

    /// <summary>
    /// Établit le succès de la quête ouverte, sa place et ses voisines.
    ///
    /// Ce sont les voisines de la liste du succès, dans l'ordre du site, et non
    /// celles de la chaîne publiée en pied de page : cette chaîne-là relie les
    /// prérequis, saute d'un succès à l'autre et se ramifie. La première quête
    /// d'un succès n'a pas de précédente, la dernière pas de suivante, et c'est
    /// ce qu'on attend en parcourant une liste.
    ///
    /// Une quête sans succès n'a pas de voisines : les autres quêtes de sa
    /// rubrique ne forment pas une suite.
    /// </summary>
    private void SetNeighbours(QuestSummary quest)
    {
        ChainText = quest.SuccessName;
        ChainStep = string.Empty;
        PreviousQuest = null;
        NextQuest = null;

        if (quest.SuccessName.Length == 0)
        {
            return;
        }

        List<QuestSummary> group =
        [
            .. InPlayOrder(
                _catalog.Catalog.Quests.Where(q =>
                    string.Equals(q.SuccessName, quest.SuccessName, StringComparison.Ordinal))),
        ];

        var index = group.FindIndex(q =>
            string.Equals(q.Url, quest.Url, StringComparison.Ordinal));

        if (index < 0)
        {
            return;
        }

        ChainStep = $"{Text(index + 1)} / {Text(group.Count)}";

        if (index > 0)
        {
            PreviousQuest = ToLink(group[index - 1]);
        }

        if (index < group.Count - 1)
        {
            NextQuest = ToLink(group[index + 1]);
        }
    }

    private static QuestLink ToLink(QuestSummary quest) =>
        new(quest.Title, quest.Url, QuestLinkKind.Quest);

    /// <summary>Étapes repérées dans la page ouverte.</summary>
    private IReadOnlyList<string> _steps = [];

    /// <summary>
    /// Raccourci de la première étape, composé des métadonnées de la quête
    /// ouverte. Null quand le site ne dit ni où ni auprès de qui elle se lance.
    /// </summary>
    private string? _start;

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

    /// <summary>Quête suivante du succès, s'il y en a une après celle-ci.</summary>
    [ObservableProperty]
    private QuestLink? _nextQuest;

    /// <summary>Quête précédente du succès, s'il y en a une avant celle-ci.</summary>
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

        // La chaîne publiée en pied de page relie les prérequis : elle saute
        // d'un succès à l'autre et se ramifie. « Les rescapés de Frigost » y a
        // deux suites et pour seul précédent un jalon. Ce n'est pas ce qu'on
        // parcourt : les voisines sont celles de la liste du succès, et
        // SetCurrent les a déjà posées avant même que la page arrive.
        //
        // Le nom du succès vient du catalogue pour la même raison, afin que la
        // liste et la page s'accordent. On ne retombe sur celui de la page que
        // pour une quête que le catalogue ne rattache à rien.
        if (ChainText.Length == 0)
        {
            ChainText = facts.Success ?? string.Empty;
        }


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

        // Le texte brut du paragraphe tenait sur une ligne tronquée où l'on ne
        // voyait ni où aller ni à qui parler. La première étape se compose des
        // métadonnées de la quête, bien plus sûres que sa prose.
        StepDetail = index >= 0 && index < total
            ? (index == 0 ? _start ?? QuestStepSummary.Of(_steps[0]) : QuestStepSummary.Of(_steps[index]))
            : string.Empty;

        CanGoPreviousStep = index > 0;
        CanGoNextStep = index >= 0 && index < total - 1;
    }

    /// <summary>
    /// On suit un lien de succès. Le titre est repris tout de suite : la page
    /// met une seconde à répondre, et un bandeau qui garde l'ancien nom pendant
    /// ce temps laisse croire que le clic n'a rien fait.
    ///
    /// La quête est retrouvée dans le catalogue par son adresse, pour que le
    /// pied reste peuplé. Il ne l'était pas : la méthode effaçait la précédente
    /// et la suivante sans jamais les rétablir, si bien qu'après un seul saut
    /// la navigation s'éteignait et qu'il fallait repasser par la liste.
    /// </summary>
    public void Follow(QuestLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        var quest = _catalog.Catalog.Quests.FirstOrDefault(q =>
            string.Equals(q.Url, link.Url, StringComparison.Ordinal));

        if (quest is not null)
        {
            SetCurrent(quest);
            IsListOpen = false;

            return;
        }

        // Une adresse que le catalogue ne connaît pas : on ouvre quand même,
        // sans voisines, plutôt que de ne rien faire.
        CurrentUrl = link.Url;
        QuestTitle = link.Title;
        ChainText = string.Empty;
        ChainStep = string.Empty;
        HasQuest = true;
        IsListOpen = false;

        _start = null;
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
