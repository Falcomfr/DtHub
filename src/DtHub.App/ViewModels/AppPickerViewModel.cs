using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.App.Services;
using DtHub.Core.Dofus;
using DtHub.Core.Localization;
using DtHub.Core.Users;

namespace DtHub.App.ViewModels;

/// <summary>What the person decided in the application window.</summary>
public enum AppPickerOutcome
{
    /// <summary>Closed without saving anything.</summary>
    Cancelled,

    /// <summary>The ticked applications are to be shown.</summary>
    Saved,

    /// <summary>A new account is wanted: the game cloned on a new profile.</summary>
    CloneRequested,
}

/// <summary>
/// The window behind a phone's plus button. Two different things live
/// there and must never be mistaken for one another: showing an
/// application the phone already has, which installs nothing, and
/// cloning the game onto a new Android profile, which creates one.
/// </summary>
public sealed partial class AppPickerViewModel : ObservableObject
{
    private readonly GameLauncher _launcher;

    public AppPickerViewModel(GameLauncher launcher, string deviceId, string deviceName)
    {
        _launcher = launcher;
        DeviceId = deviceId;
        Title = Strings.Format("AppPickerTitle", deviceName);

        View = CollectionViewSource.GetDefaultView(Rows);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AppChoiceRow.ProfileName)));
        View.Filter = Matches;
    }

    public string DeviceId { get; }

    public string Title { get; }

    /// <summary>Every application offered, ticked or not.</summary>
    public ObservableCollection<AppChoiceRow> Rows { get; } = [];

    /// <summary>The rows as the list shows them: filtered and grouped by profile.</summary>
    public ICollectionView View { get; }

    /// <summary>True while the phone is being asked.</summary>
    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>Why nothing could be listed, or <c>null</c>.</summary>
    [ObservableProperty]
    private string? _problem;

    /// <summary>True when the filter leaves nothing to show.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>
    /// Also offer what came with the phone. Off by default: a phone
    /// carries a few dozen of them, and nobody comes here for the
    /// calculator.
    /// </summary>
    [ObservableProperty]
    private bool _showSystemApps;

    public AppPickerOutcome Outcome { get; set; } = AppPickerOutcome.Cancelled;

    /// <summary>The ticked applications, what the window saves.</summary>
    public IReadOnlyList<ShownApp> Chosen =>
        [.. Rows.Where(r => r.IsChosen).Select(r => new ShownApp(DeviceId, r.UserId, r.PackageName, r.Label))];

    partial void OnFilterChanged(string value) => Refresh();

    partial void OnShowSystemAppsChanged(bool value) => Refresh();

    /// <summary>Asks the phone, then fills the list.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        Problem = null;

        try
        {
            var shown = (await _launcher.GetShownAppsAsync(cancellationToken).ConfigureAwait(true))
                .Where(a => string.Equals(a.DeviceId, DeviceId, StringComparison.Ordinal))
                .ToList();

            var profiles = await _launcher.ListAppsAsync(DeviceId, cancellationToken).ConfigureAwait(true);

            if (profiles is null)
            {
                Problem = Strings.Get("PhoneNotConnected");
            }

            Fill(profiles ?? [], shown);
        }
        finally
        {
            IsLoading = false;
            Refresh();
        }
    }

    private void Fill(IReadOnlyList<ProfileApps> profiles, IReadOnlyList<ShownApp> shown)
    {
        Rows.Clear();

        var chosen = shown.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);

        // A single profile needs no heading: naming it would only repeat
        // the phone's name.
        var named = profiles.Count(p => p.Apps.Count > 0) > 1;

        foreach (var profile in profiles)
        {
            var heading = named ? ProfileHeading(profile) : string.Empty;

            foreach (var app in profile.Apps)
            {
                Rows.Add(new AppChoiceRow(
                    app.Label,
                    app.PackageName,
                    profile.UserId,
                    heading,
                    app.IsSystem,
                    chosen.Contains($"{DeviceId}|{profile.UserId}|{app.PackageName}")));
            }
        }

        // An application already shown that the phone no longer offers,
        // or did not list this time: kept, ticked, so that it can still
        // be unticked. Hiding it would leave a row nobody can remove.
        foreach (var app in shown.Where(a => !Rows.Any(r => r.UserId == a.UserId
                     && string.Equals(r.PackageName, a.PackageName, StringComparison.Ordinal))))
        {
            Rows.Add(new AppChoiceRow(
                string.IsNullOrWhiteSpace(app.Label) ? app.PackageName : app.Label,
                app.PackageName,
                app.UserId,
                named ? Strings.Get("AppPickerNotListed") : string.Empty,
                isSystem: false,
                isChosen: true));
        }
    }

    private static string ProfileHeading(ProfileApps profile) => profile.Type switch
    {
        AndroidUserType.Primary => Strings.Get("AppPickerMainProfile"),
        _ => profile.UserName,
    };

    private bool Matches(object item)
    {
        if (item is not AppChoiceRow row)
        {
            return false;
        }

        // An application shown when the window opened always shows:
        // filtering it out would hide a choice already made. Read from
        // the opening and not from the box, or unticking it under the
        // filter would make it vanish under the mouse.
        if (row.WasShown)
        {
            return true;
        }

        if (row.IsSystem && !ShowSystemApps)
        {
            return false;
        }

        var text = Filter.Trim();

        return text.Length == 0
               || row.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase)
               || row.PackageName.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void Refresh()
    {
        View.Refresh();
        IsEmpty = !IsLoading && Problem is null && View.IsEmpty;
    }
}

/// <summary>One application of one profile, and whether it is ticked.</summary>
public sealed partial class AppChoiceRow : ObservableObject
{
    public AppChoiceRow(
        string label,
        string packageName,
        int userId,
        string profileName,
        bool isSystem,
        bool isChosen)
    {
        Label = label;
        PackageName = packageName;
        UserId = userId;
        ProfileName = profileName;
        IsSystem = isSystem;
        WasShown = isChosen;
        _isChosen = isChosen;
    }

    /// <summary>True if the application was already shown when the window opened.</summary>
    public bool WasShown { get; }

    public string Label { get; }

    public string PackageName { get; }

    public int UserId { get; }

    /// <summary>The group heading, empty when the phone has one profile only.</summary>
    public string ProfileName { get; }

    public bool IsSystem { get; }

    [ObservableProperty]
    private bool _isChosen;
}
