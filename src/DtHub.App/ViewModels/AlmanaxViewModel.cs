using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.Core.Almanax;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>
/// La culture qui met en forme dates et nombres dans cette fenêtre.
///
/// « CurrentUICulture » choisit les textes traduits et ne doit pas servir à
/// mettre en forme : l'analyseur le refuse, et il a raison, ce sont deux
/// réglages distincts. On en tire donc une culture de mise en forme, pour que
/// les noms de jours suivent la langue de l'application plutôt que la région
/// de Windows : qui a choisi l'anglais veut « Thu », pas « jeu. »
/// </summary>
internal static class DisplayCulture
{
    public static CultureInfo Current => CultureInfo.GetCultureInfo(CultureInfo.CurrentUICulture.Name);
}

/// <summary>Un jour de la bande, tel qu'il se présente et se choisit.</summary>
public sealed partial class AlmanaxChip : ObservableObject
{
    public AlmanaxChip(DateOnly date, bool today)
    {
        Date = date;
        IsToday = today;

        var when = date.ToDateTime(TimeOnly.MinValue);

        Weekday = when.ToString("ddd", DisplayCulture.Current).TrimEnd('.');
        Number = when.Day.ToString(DisplayCulture.Current);
    }

    public DateOnly Date { get; }

    /// <summary>Le jour de la semaine, abrégé dans la langue de l'application.</summary>
    public string Weekday { get; }

    /// <summary>Le quantième, seul : le mois se lit dans le bandeau du dessous.</summary>
    public string Number { get; }

    /// <summary>Vrai pour aujourd'hui, qui se marque même quand un autre est choisi.</summary>
    public bool IsToday { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Une case du calendrier déroulant.</summary>
public sealed partial class AlmanaxCell : ObservableObject
{
    public AlmanaxCell(DateOnly date, bool inMonth, bool today, bool selected, bool reachable)
    {
        Date = date;
        InMonth = inMonth;
        IsToday = today;
        IsSelected = selected;
        IsReachable = reachable;
        Number = date.Day.ToString(DisplayCulture.Current);
    }

    public DateOnly Date { get; }

    public string Number { get; }

    /// <summary>Faux pour les débords du mois voisin, qu'on montre en retrait.</summary>
    public bool InMonth { get; }

    public bool IsToday { get; }

    /// <summary>
    /// Faux hors des bornes consultables. La case reste visible pour que la
    /// grille garde sa forme, mais elle ne se choisit pas.
    /// </summary>
    public bool IsReachable { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// L'Almanax du jour, réduit à ce qu'on vient y chercher.
///
/// La page du portail est une page de bureau entière : décor, protecteur du
/// mois, signe du zodiaque, Rubrikabrax et leurs textes d'ambiance. Une seule
/// question s'y pose vraiment, « qu'est-ce que j'apporte aujourd'hui », et
/// c'est celle-là qu'on met en tête.
///
/// La bande des jours vient d'abord parce qu'un Almanax se prépare : on veut
/// savoir ce qu'il faudra demain pour l'avoir en poche.
/// </summary>
public sealed partial class AlmanaxViewModel : ObservableObject
{
    /// <summary>
    /// Sept jours, à partir d'aujourd'hui. C'est l'horizon que le portail
    /// lui-même propose, « voir les 7 prochains jours », et celui qui suffit à
    /// préparer ses offrandes sans encombrer la bande.
    /// </summary>
    private const int Span = 7;

    private DateOnly _anchor = DateOnly.FromDateTime(DateTime.Now);

    public AlmanaxViewModel()
    {
        Selected = DateOnly.FromDateTime(DateTime.Now);

        RebuildDays();
    }

    /// <summary>Les sept jours de la bande.</summary>
    public ObservableCollection<AlmanaxChip> Days { get; } = [];

    /// <summary>Demandé quand il faut aller lire un autre jour.</summary>
    public event EventHandler<DateOnly>? DateRequested;

    [ObservableProperty]
    private DateOnly _selected;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _problem = string.Empty;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private string _dofusianDay = string.Empty;

    /// <summary>Combien en apporter. Vide quand la phrase ne s'est pas laissé lire.</summary>
    [ObservableProperty]
    private string _offeringCount = string.Empty;

    /// <summary>
    /// Quoi apporter, ou la phrase entière quand elle ne s'est pas laissé
    /// lire : dans les deux cas la ligne dit ce qu'il faut faire.
    /// </summary>
    [ObservableProperty]
    private string _offeringItem = string.Empty;

    /// <summary>La phrase du portail, sous le titre, quand elle apporte plus que lui.</summary>
    [ObservableProperty]
    private string _offeringDetail = string.Empty;

    [ObservableProperty]
    private string _bonus = string.Empty;

    [ObservableProperty]
    private string _bonusDetail = string.Empty;

    [ObservableProperty]
    private string _quest = string.Empty;

    [ObservableProperty]
    private string _meryde = string.Empty;

    /// <summary>Vrai quand une journée est affichée, donc qu'il y a autre chose qu'une attente.</summary>
    [ObservableProperty]
    private bool _hasDay;

    /// <summary>Vrai quand le jour choisi n'est pas aujourd'hui : le retour se propose alors.</summary>
    public bool CanGoToday => Selected != DateOnly.FromDateTime(DateTime.Now);

    /// <summary>
    /// L'attente seule, tant que rien n'a jamais été lu. Une fois une journée
    /// affichée, changer de jour ne la remplace pas par un message : la
    /// fenêtre ne clignote pas entre deux lectures.
    /// </summary>
    public bool ShowsWaiting => IsLoading && !HasDay;

    /// <summary>Montre la journée lue.</summary>
    public void Show(AlmanaxDay day)
    {
        Selected = day.Date;

        DateText = day.Date.ToDateTime(TimeOnly.MinValue)
            .ToString("D", DisplayCulture.Current);

        DofusianDay = day.DofusianDay;

        // Le nombre et l'objet quand la phrase s'est laissé lire, la phrase
        // entière sinon : dans les deux cas le lecteur sait quoi apporter. Les
        // deux sont séparés pour que la fenêtre puisse donner au nombre le
        // poids qu'il mérite, c'est lui qu'on vérifie d'un coup d'œil.
        var read = day.Item is { Length: > 0 };

        OfferingCount = read ? day.Quantity?.ToString(DisplayCulture.Current) ?? string.Empty : string.Empty;
        OfferingItem = read ? day.Item! : day.Offering;
        OfferingDetail = read ? day.Offering : string.Empty;

        Bonus = day.Bonus;
        BonusDetail = day.BonusDetail;
        Quest = day.Quest;
        Meryde = day.Meryde;

        Problem = string.Empty;
        HasDay = true;
        IsLoading = false;
    }

    /// <summary>Dit qu'on n'a pas pu lire, sans effacer ce qui était affiché.</summary>
    public void Fail()
    {
        Problem = Strings.Get("AlmanaxUnavailable");
        IsLoading = false;
    }

    /// <summary>
    /// Demande une journée. **Ne fait que demander.**
    ///
    /// Cette méthode lève l'événement et rien d'autre, et la fenêtre qui
    /// l'écoute ne doit jamais la rappeler : elle l'a fait, et chargement et
    /// événement se sont relancés l'un l'autre jusqu'à épuisement de la pile.
    /// La fenêtre passe donc par <see cref="BeginLoading" />, qui pose l'état
    /// sans rien lever. Deux méthodes plutôt qu'un garde-fou : un garde-fou
    /// se contourne à la modification suivante, une méthode qui ne lève rien
    /// ne peut pas boucler.
    /// </summary>
    public void Go(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        // Rien hors des bornes ne part au portail : interrogé sur une date
        // qu'il ne sait pas traiter, il rend la journée du jour sans se
        // plaindre, et le bandeau annoncerait une date en montrant l'offrande
        // d'une autre. Voir « AlmanaxRange ».
        if (AlmanaxRange.Contains(date, today))
        {
            DateRequested?.Invoke(this, date);
        }
    }

    /// <summary>
    /// Pose l'attente sur cette journée, sans rien demander à personne.
    /// Appelée par la fenêtre quand elle part vraiment lire.
    /// </summary>
    public void BeginLoading(DateOnly date)
    {
        Selected = date;
        Problem = string.Empty;
        IsLoading = true;
    }

    /// <summary>
    /// Vrai quand cette journée est déjà sous les yeux, sans incident et sans
    /// chargement en cours : il n'y a alors rien à refaire.
    /// </summary>
    public bool AlreadyShowing(DateOnly date) =>
        HasDay && !IsLoading && Problem.Length == 0 && date == Selected;

    /// <summary>Vrai quand le calendrier déroulant est ouvert.</summary>
    [ObservableProperty]
    private bool _isCalendarOpen;

    /// <summary>Le mois montré par le calendrier, qui n'est pas forcément celui du jour choisi.</summary>
    private DateOnly _month = new(DateTime.Now.Year, DateTime.Now.Month, 1);

    /// <summary>Les quarante-deux cases du mois montré.</summary>
    public ObservableCollection<AlmanaxCell> MonthDays { get; } = [];

    /// <summary>Les initiales des jours, du premier de la semaine au dernier selon la culture.</summary>
    public ObservableCollection<string> Weekdays { get; } = [];

    /// <summary>« septembre 2026 », en tête du calendrier.</summary>
    [ObservableProperty]
    private string _monthLabel = string.Empty;

    /// <summary>
    /// À l'ouverture, le calendrier se pose sur le mois du jour choisi.
    ///
    /// Sur celui du jour choisi et non sur celui d'aujourd'hui : on ouvre le
    /// calendrier pour aller plus loin, et repartir chaque fois du mois
    /// courant obligerait à refaire le chemin.
    ///
    /// Ici et non dans une commande de la bascule : la bascule appelait sa
    /// commande à la fermeture comme à l'ouverture, et refermer le calendrier
    /// le rouvrait aussitôt.
    /// </summary>
    partial void OnIsCalendarOpenChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        _month = new DateOnly(Selected.Year, Selected.Month, 1);

        RebuildMonth();
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        _month = AlmanaxRange.ShiftMonth(_month, -1, DateOnly.FromDateTime(DateTime.Now));

        RebuildMonth();
    }

    [RelayCommand]
    private void NextMonth()
    {
        _month = AlmanaxRange.ShiftMonth(_month, 1, DateOnly.FromDateTime(DateTime.Now));

        RebuildMonth();
    }

    /// <summary>Choisir une case ferme le calendrier : on l'a ouvert pour choisir.</summary>
    [RelayCommand]
    private void PickDay(AlmanaxCell? cell)
    {
        if (cell is null || !cell.IsReachable)
        {
            return;
        }

        IsCalendarOpen = false;

        Go(cell.Date);
    }

    private void RebuildMonth()
    {
        var culture = DisplayCulture.Current;
        var first = culture.DateTimeFormat.FirstDayOfWeek;
        var today = DateOnly.FromDateTime(DateTime.Now);

        MonthLabel = _month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", culture);

        Weekdays.Clear();

        foreach (var day in MonthGrid.Header(first))
        {
            Weekdays.Add(culture.DateTimeFormat.ShortestDayNames[(int)day]);
        }

        MonthDays.Clear();

        foreach (var date in MonthGrid.For(_month.Year, _month.Month, first))
        {
            MonthDays.Add(new AlmanaxCell(
                date,
                date.Month == _month.Month,
                date == today,
                date == Selected,
                AlmanaxRange.Contains(date, today)));
        }
    }

    /// <summary>Vrai tant qu'il reste un jour en arrière dans les bornes.</summary>
    public bool CanGoPrevious => Selected > AlmanaxRange.Earliest(DateOnly.FromDateTime(DateTime.Now));

    /// <summary>Vrai tant qu'il reste un jour en avant dans les bornes.</summary>
    public bool CanGoNext => Selected < AlmanaxRange.Latest(DateOnly.FromDateTime(DateTime.Now));

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => Go(AlmanaxRange.Clamp(Selected.AddDays(-1), DateOnly.FromDateTime(DateTime.Now)));

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Go(AlmanaxRange.Clamp(Selected.AddDays(1), DateOnly.FromDateTime(DateTime.Now)));

    [RelayCommand]
    private void Today() => Go(DateOnly.FromDateTime(DateTime.Now));

    [RelayCommand]
    private void Pick(AlmanaxChip? chip)
    {
        if (chip is not null)
        {
            Go(chip.Date);
        }
    }

    /// <summary>
    /// La bande suit le jour choisi plutôt que de rester sur aujourd'hui :
    /// avancer de sept jours avec la flèche laissait sinon la bande derrière,
    /// et le jour affiché n'y était plus marqué nulle part.
    /// </summary>
    partial void OnSelectedChanged(DateOnly value)
    {
        if (value < _anchor || value > _anchor.AddDays(Span - 1))
        {
            _anchor = value < _anchor ? value : value.AddDays(-(Span - 1));

            RebuildDays();
        }

        foreach (var chip in Days)
        {
            chip.IsSelected = chip.Date == value;
        }

        OnPropertyChanged(nameof(CanGoToday));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));

        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowsWaiting));

    partial void OnHasDayChanged(bool value) => OnPropertyChanged(nameof(ShowsWaiting));

    private void RebuildDays()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        Days.Clear();

        for (var i = 0; i < Span; i++)
        {
            var date = _anchor.AddDays(i);

            Days.Add(new AlmanaxChip(date, date == today) { IsSelected = date == Selected });
        }
    }
}
