using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.Core.Almanax;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>
/// The culture that formats dates and numbers in this window.
///
/// "CurrentUICulture" picks the translated texts and must not be used
/// for formatting: the analyzer refuses it, and it is right, these are
/// two distinct settings. A formatting culture is therefore derived
/// from it, so that day names follow the application's language rather
/// than Windows' region: someone who chose English wants "Thu", not
/// "jeu."
/// </summary>
internal static class DisplayCulture
{
    public static CultureInfo Current => CultureInfo.GetCultureInfo(CultureInfo.CurrentUICulture.Name);
}

/// <summary>A day of the strip, as it is shown and chosen.</summary>
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

    /// <summary>
    /// The day of the week, abbreviated in the application's language.
    /// </summary>
    public string Weekday { get; }

    /// <summary>
    /// The day number alone: the month is read in the banner below.
    /// </summary>
    public string Number { get; }

    /// <summary>
    /// True for today, which is marked even when another day is chosen.
    /// </summary>
    public bool IsToday { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>A cell of the drop-down calendar.</summary>
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

    /// <summary>
    /// False for the days that spill over from the neighboring month,
    /// which are shown muted.
    /// </summary>
    public bool InMonth { get; }

    public bool IsToday { get; }

    /// <summary>
    /// False outside the browsable bounds. The cell stays visible so that
    /// the grid keeps its shape, but it cannot be chosen.
    /// </summary>
    public bool IsReachable { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// The Almanax of the day, reduced to what one comes here to look for.
///
/// The portal page is an entire desktop page: scenery, guardian of the
/// month, zodiac sign, Rubrikabrax and their mood texts. Only one
/// question truly belongs here, "what do I bring today", and that is
/// the one put first.
///
/// The day strip comes first because an Almanax is prepared ahead: one
/// wants to know what will be needed tomorrow to have it in the bag.
/// </summary>
public sealed partial class AlmanaxViewModel : ObservableObject
{
    /// <summary>
    /// Seven days, starting from today. This is the horizon the portal
    /// itself offers, "see the next 7 days", and the one that is enough
    /// to prepare one's offerings without cluttering the strip.
    /// </summary>
    private const int Span = 7;

    private DateOnly _anchor = DateOnly.FromDateTime(DateTime.Now);

    public AlmanaxViewModel()
    {
        Selected = DateOnly.FromDateTime(DateTime.Now);

        RebuildDays();
    }

    /// <summary>The seven days of the strip.</summary>
    public ObservableCollection<AlmanaxChip> Days { get; } = [];

    /// <summary>Raised when another day needs to be read.</summary>
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

    /// <summary>
    /// How many to bring. Empty when the sentence could not be parsed.
    /// </summary>
    [ObservableProperty]
    private string _offeringCount = string.Empty;

    /// <summary>
    /// What to bring, or the whole sentence when it could not be parsed:
    /// in both cases the line says what needs to be done.
    /// </summary>
    [ObservableProperty]
    private string _offeringItem = string.Empty;

    /// <summary>
    /// The portal's sentence, under the title, when it brings more than
    /// the title does.
    /// </summary>
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

    /// <summary>
    /// The event of the month, the same throughout the month.
    /// </summary>
    [ObservableProperty]
    private string _monthEvent = string.Empty;

    /// <summary>
    /// True when a day is displayed, meaning there is something other
    /// than a wait.
    /// </summary>
    [ObservableProperty]
    private bool _hasDay;

    /// <summary>
    /// True when the chosen day is not today: the return option is then
    /// offered.
    /// </summary>
    public bool CanGoToday => Selected != DateOnly.FromDateTime(DateTime.Now);

    /// <summary>
    /// The wait alone, as long as nothing has ever been read. Once a day
    /// is displayed, changing day does not replace it with a message: the
    /// window does not flicker between two reads.
    /// </summary>
    public bool ShowsWaiting => IsLoading && !HasDay;

    /// <summary>Shows the day that was read.</summary>
    public void Show(AlmanaxDay day)
    {
        Selected = day.Date;

        DateText = day.Date.ToDateTime(TimeOnly.MinValue)
            .ToString("D", DisplayCulture.Current);

        DofusianDay = day.DofusianDay;

        // The number and the item when the sentence could be parsed, the
        // whole sentence otherwise: in both cases the reader knows what to
        // bring. The two are kept separate so that the window can give the
        // number the weight it deserves, since it is the one checked at a
        // glance.
        var read = day.Item is { Length: > 0 };

        OfferingCount = read ? day.Quantity?.ToString(DisplayCulture.Current) ?? string.Empty : string.Empty;
        OfferingItem = read ? day.Item! : day.Offering;
        OfferingDetail = read ? day.Offering : string.Empty;

        Bonus = day.Bonus;
        BonusDetail = day.BonusDetail;
        Quest = day.Quest;
        Meryde = day.Meryde;
        MonthEvent = day.MonthEvent;

        Problem = string.Empty;
        HasDay = true;
        IsLoading = false;
    }

    /// <summary>
    /// Says that reading failed, without erasing what was displayed.
    /// </summary>
    public void Fail()
    {
        Problem = Strings.Get("AlmanaxUnavailable");
        IsLoading = false;
    }

    /// <summary>
    /// Requests a day. **Only requests.**
    ///
    /// This method raises the event and nothing else, and the window
    /// listening to it must never call it back: it did, once, and loading
    /// and the event kept relaunching each other until the stack was
    /// exhausted. The window therefore goes through
    /// <see cref="BeginLoading" />, which sets the state without raising
    /// anything. Two methods rather than a safety rail: a safety rail can
    /// be bypassed at the next change, a method that raises nothing cannot
    /// loop.
    /// </summary>
    public void Go(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        // Nothing outside the bounds goes to the portal: when asked about a
        // date it does not know how to handle, it returns today's day
        // without complaining, and the banner would announce one date
        // while showing the offering of another. See "AlmanaxRange".
        if (AlmanaxRange.Contains(date, today))
        {
            DateRequested?.Invoke(this, date);
        }
    }

    /// <summary>
    /// Sets the wait on this day, without asking anyone for anything.
    /// Called by the window when it actually goes to read.
    /// </summary>
    public void BeginLoading(DateOnly date)
    {
        Selected = date;
        Problem = string.Empty;
        IsLoading = true;
    }

    /// <summary>
    /// True when this day is already in view, without incident and
    /// without a load in progress: there is then nothing to redo.
    /// </summary>
    public bool AlreadyShowing(DateOnly date) =>
        HasDay && !IsLoading && Problem.Length == 0 && date == Selected;

    /// <summary>True when the drop-down calendar is open.</summary>
    [ObservableProperty]
    private bool _isCalendarOpen;

    /// <summary>
    /// The month shown by the calendar, which is not necessarily that of
    /// the chosen day.
    /// </summary>
    private DateOnly _month = new(DateTime.Now.Year, DateTime.Now.Month, 1);

    /// <summary>The forty-two cells of the shown month.</summary>
    public ObservableCollection<AlmanaxCell> MonthDays { get; } = [];

    /// <summary>
    /// The initials of the days, from the first day of the week to the
    /// last according to the culture.
    /// </summary>
    public ObservableCollection<string> Weekdays { get; } = [];

    /// <summary>"September 2026", at the top of the calendar.</summary>
    [ObservableProperty]
    private string _monthLabel = string.Empty;

    /// <summary>
    /// On opening, the calendar settles on the month of the chosen day.
    ///
    /// On that of the chosen day and not on that of today: the calendar
    /// is opened to go further, and starting over each time from the
    /// current month would force retracing the path.
    ///
    /// Here and not in a toggle command: the toggle used to call its
    /// command on closing as well as on opening, and closing the calendar
    /// would immediately reopen it.
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

    /// <summary>
    /// Choosing a cell closes the calendar: it was opened in order to
    /// choose.
    /// </summary>
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

    /// <summary>
    /// True as long as a day remains backward within the bounds.
    /// </summary>
    public bool CanGoPrevious => Selected > AlmanaxRange.Earliest(DateOnly.FromDateTime(DateTime.Now));

    /// <summary>
    /// True as long as a day remains forward within the bounds.
    /// </summary>
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
    /// The strip follows the chosen day rather than staying on today:
    /// advancing seven days with the arrow would otherwise leave the
    /// strip behind, and the displayed day would no longer be marked
    /// anywhere in it.
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
