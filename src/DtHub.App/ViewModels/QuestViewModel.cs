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
    [NotifyPropertyChangedFor(nameof(ShowsPage))]
    [NotifyPropertyChangedFor(nameof(ShowsLoader))]
    private bool _isListOpen;

    /// <summary>
    /// Vrai le temps qu'une page de guide arrive.
    ///
    /// Le bandeau annonce la nouvelle quête dès le clic, mais la vue montre
    /// encore l'ancien guide pendant une seconde ou deux : on croyait que le
    /// clic n'avait rien fait, ou pire, on lisait la mauvaise page.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPage))]
    [NotifyPropertyChangedFor(nameof(ShowsLoader))]
    private bool _isLoadingPage;

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

    /// <summary>
    /// Vrai quand la vue web doit se voir. Elle est retirée pendant un
    /// chargement, et non recouverte : une fenêtre native se dessine au-dessus
    /// de tout élément WPF du même châssis, et un voile posé dessus resterait
    /// invisible. C'est du reste ce que fait déjà la liste déroulante.
    /// </summary>
    public bool ShowsPage => !IsListOpen && !IsLoadingPage;

    /// <summary>Vrai quand la place de la vue revient à l'indicateur d'attente.</summary>
    public bool ShowsLoader => IsLoadingPage && !IsListOpen;

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

        _chain = new QuestChainIndex(catalog.Quests);

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
            Id: RootSection,
            Glyph: QuestNodeGlyph.Quests));
        Nodes.Add(new QuestNode(
            QuestNodeKind.Pending,
            "Donjons",
            "bientôt",
            Glyph: QuestNodeGlyph.Dungeons));
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
                QuestNodeKind.Section, "Zones", Combien(found.Zones.Count, "zone", "zones")));

            foreach (var zone in found.Zones)
            {
                Nodes.Add(new QuestNode(
                    QuestNodeKind.Branch,
                    $"{QuestZoneOrder.DisplayName(zone.Name)} ({_sectionCounts.GetValueOrDefault(zone.Id)})",
                    Id: zone.Id,
                    Glyph: GlyphOf(zone.Name)));
            }
        }

        if (found.Successes.Count > 0)
        {
            Nodes.Add(new QuestNode(
                QuestNodeKind.Section, "Succès", Combien(found.Successes.Count, "succès", "succès")));

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
                    QuestNodeKind.Success,
                    $"{success} ({quests.Count})",
                    LevelRange(quests),
                    Glyph: QuestNodeGlyph.Success));

                foreach (var quest in quests)
                {
                    Nodes.Add(ToNode(quest));
                }
            }
        }

        if (found.Quests.Count > 0)
        {
            Nodes.Add(new QuestNode(QuestNodeKind.Section, "Quêtes", Nombre(found.Quests.Count)));

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

        List<QuestSection> ordered = [.. zones];
        var separated = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var zone = ordered[i];

            // Ce qui ne relève pas de la progression vient après un intertitre,
            // pour que la liste ne mélange pas un lieu et un cheminement.
            if (!separated && QuestZoneOrder.IsExtra(zone.Name))
            {
                separated = true;

                yield return new QuestNode(
                    QuestNodeKind.Header,
                    QuestZoneOrder.ExtrasHeader,
                    Glyph: QuestNodeGlyph.Family);
            }

            // Un blanc là où l'on passe d'une famille de quêtes aux lieux :
            // « Quêtes principales » ouvre la liste sans être un endroit, et
            // sans cette respiration elle se lit comme la première zone du
            // monde. Le blanc appartient à la ligne qui précède la rupture, et
            // non à celle qui la suit, pour ne pas doubler celui de
            // l'intertitre plus bas.
            var next = i + 1 < ordered.Count ? ordered[i + 1] : null;

            yield return new QuestNode(
                QuestNodeKind.Branch,
                $"{QuestZoneOrder.DisplayName(zone.Name)} ({_sectionCounts[zone.Id]})",
                LevelRange(_catalog.InSection(zone.Id)),
                Id: zone.Id,
                Glyph: GlyphOf(zone.Name),
                Spaced: next is not null
                    && !QuestZoneOrder.IsPlace(zone.Name)
                    && QuestZoneOrder.IsPlace(next.Name));
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
            Nodes.Add(new QuestNode(
                QuestNodeKind.Header,
                NameOf(group.Key),
                Nombre(group.Count()),
                Glyph: GlyphOf(RawNameOf(group.Key))));

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
                LevelRange(ordered),
                Glyph: QuestNodeGlyph.Success));

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
        Needs: NeedsOf(quest));

    /// <summary>
    /// Les prérequis d'une quête, chacun rattaché à la quête qu'il nomme quand
    /// c'en est une. Sur cinq cent soixante-sept prérequis distincts, beaucoup
    /// sont des objets, un alignement ou un créneau horaire : ceux-là restent
    /// du texte, et seuls les autres deviendront des liens.
    /// </summary>
    private IReadOnlyList<QuestNeed> NeedsOf(QuestSummary quest) =>
        quest.Prerequisites.Count == 0
            ? []
            : [.. quest.Prerequisites.Select(need => new QuestNeed(need, _chain?.Find(need)))];

    /// <summary>
    /// Ce que les prérequis relient, table construite une fois par catalogue.
    /// Sert aussi bien à rattacher un prérequis à sa quête qu'à prolonger la
    /// navigation au-delà d'un succès.
    /// </summary>
    private QuestChainIndex? _chain;

    /// <summary>L'icône d'une rubrique, selon qu'elle situe ou qu'elle range.</summary>
    private static QuestNodeGlyph GlyphOf(string? zone) =>
        QuestZoneOrder.IsPlace(zone) ? QuestNodeGlyph.Place : QuestNodeGlyph.Family;

    /// <summary>Le nom de rubrique tel que le site l'écrit, pour en juger la nature.</summary>
    private string? RawNameOf(int section) =>
        _catalog.Catalog.Sections.FirstOrDefault(s => s.Id == section)?.Name;

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
        _startsAtDeparture = false;

        SetNeighbours(quest);
        ExtendNeighbours(quest);

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

    /// <summary>
    /// Prolonge la navigation là où la liste du succès s'arrête, en suivant les
    /// prérequis.
    ///
    /// La première quête d'un succès n'a pas de précédente et la dernière pas
    /// de suivante ; une quête hors succès n'a ni l'une ni l'autre. Le site,
    /// lui, continue : « Bien débuter » mène à « Une arrivée mouvementée », qui
    /// mène à « Le début des problèmes », laquelle ouvre un succès. Mesuré, cela
    /// rend une suivante à cent soixante-huit quêtes et une précédente à cent
    /// quatre-vingt-dix-sept.
    /// </summary>
    private void ExtendNeighbours(QuestSummary quest)
    {
        if (_chain is null)
        {
            return;
        }

        PreviousQuest ??= ToLink(_chain.PreviousOf(quest), quest);
        NextQuest ??= ToLink(_chain.NextOf(quest), quest);
    }

    /// <summary>
    /// Le lien vers une quête voisine, annoncé par sa série quand on en change.
    ///
    /// Suivre un prérequis fait parfois entrer dans un autre succès, voire dans
    /// une autre zone. Le titre seul laisserait croire qu'on poursuit la même
    /// suite ; le nom du succès — à défaut celui de la zone — dit qu'on en
    /// commence une autre.
    /// </summary>
    private QuestLink? ToLink(QuestSummary? target, QuestSummary from)
    {
        if (target is null)
        {
            return null;
        }

        var link = ToLink(target);

        if (string.Equals(target.SuccessName, from.SuccessName, StringComparison.Ordinal))
        {
            return target.SectionId == from.SectionId
                ? link
                : link with { Series = NameOf(target.SectionId) };
        }

        return link with
        {
            Series = target.SuccessName.Length > 0 ? target.SuccessName : NameOf(target.SectionId),
        };
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

    /// <summary>
    /// Vrai quand la première étape est le départ de la quête et non un
    /// paragraphe du guide. Le pont l'annonce, parce que lui seul voit si la
    /// page porte un bloc de départ.
    /// </summary>
    private bool _startsAtDeparture;

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
    public void SetPage(
        string? introHtml,
        string? chainHtml,
        IReadOnlyList<string> steps,
        bool startsAtDeparture = false)
    {
        ArgumentNullException.ThrowIfNull(steps);

        _startsAtDeparture = startsAtDeparture;

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
        // voyait ni où aller ni à qui parler : chaque étape est donc résumée.
        //
        // La première l'est par les métadonnées de la quête, plus sûres que la
        // prose du site, mais seulement quand c'est bien le départ : le pont le
        // dit. Sans cette réserve, le départ se retrouvait annoncé au-dessus du
        // premier paragraphe du guide, qui n'a le plus souvent rien à voir.
        StepDetail = index >= 0 && index < total
            ? (index == 0 && _startsAtDeparture
                ? _start ?? QuestStepSummary.Of(_steps[0])
                : QuestStepSummary.Of(_steps[index]))
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

        if (TryFollowUrl(link.Url))
        {
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
        _startsAtDeparture = false;
        _steps = [];
        HasSteps = false;
        PreviousQuest = null;
        NextQuest = null;
        SetStep(-1);
    }

    /// <summary>
    /// Suit une adresse sur place quand le catalogue la connaît, et dit si elle
    /// l'était.
    ///
    /// Un guide renvoie vers ses quêtes voisines par de simples liens : un clic
    /// dessus ouvrait une seconde fenêtre alors que le bouton « précédente »,
    /// qui mène au même endroit, restait sur place. Le catalogue tranche : ce
    /// qu'il connaît se suit ici, le reste part à part.
    /// </summary>
    public bool TryFollowUrl(string? url)
    {
        var key = UrlKey(url);

        if (key.Length == 0)
        {
            return false;
        }

        var quest = _catalog.Catalog.Quests.FirstOrDefault(q =>
            string.Equals(UrlKey(q.Url), key, StringComparison.Ordinal));

        if (quest is null)
        {
            return false;
        }

        SetCurrent(quest);
        IsListOpen = false;

        return true;
    }

    /// <summary>
    /// Adresse réduite à ce qui l'identifie.
    ///
    /// Le site écrit ses liens tantôt avec la barre finale, tantôt sans, et la
    /// comparaison stricte manquait alors une quête pourtant au catalogue.
    /// </summary>
    private static string UrlKey(string? url) =>
        (url ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();

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

    /// <summary>La page du site qui énumère les zones de quêtes.</summary>
    private const string IndexUrl = "https://papycha.fr/quetes/";

    /// <summary>
    /// Rouvre sur le site ce que la fenêtre montre, et non la quête en toutes
    /// circonstances.
    ///
    /// Le bouton menait toujours à la quête courante, y compris quand la liste
    /// couvrait l'écran sur une rubrique qu'on parcourait : il ouvrait alors
    /// autre chose que ce qu'on avait sous les yeux, ou rien du tout tant
    /// qu'aucune quête n'avait été choisie.
    ///
    /// Une recherche fait exception : elle ne montre pas de rubrique, et ce
    /// qu'on lisait avant de la lancer reste la quête.
    /// </summary>
    public void OpenInBrowser()
    {
        var url = Browsed() ?? CurrentUrl;

        if (!string.IsNullOrWhiteSpace(url))
        {
            _dialogs.OpenUrl(url);
        }
    }

    /// <summary>
    /// La page de ce que la liste parcourt, ou <c>null</c> si elle ne parcourt
    /// rien : liste fermée, recherche en cours, ou rubrique sans page rédigée.
    /// </summary>
    private string? Browsed()
    {
        if (!IsListOpen || Query.Length > 0)
        {
            return null;
        }

        if (_section is 0 or RootSection)
        {
            return IndexUrl;
        }

        var url = _catalog.Catalog.Sections.FirstOrDefault(s => s.Id == _section)?.Url;

        // Trois rubriques sur vingt-cinq n'ont pas de page à elles : le tableau
        // du site ne les nomme pas. On reste alors sur celle qui les énumère
        // toutes, plutôt que de renvoyer vers une quête dont il n'est pas
        // question à l'écran.
        return string.IsNullOrWhiteSpace(url) ? IndexUrl : url;
    }
}
