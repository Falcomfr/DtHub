using CommunityToolkit.Mvvm.ComponentModel;

namespace DtHub.App.ViewModels;

/// <summary>
/// A launch profile, as it appears in the list.
///
/// The summary goes along with the name: "Fishing duo" does not say
/// which accounts it opens, and we do not want to have to open it to
/// remember.
/// </summary>
public sealed partial class LaunchProfileRowViewModel : ObservableObject
{
    public LaunchProfileRowViewModel(string name, string summary, bool isDefault)
    {
        Name = name;
        _summary = summary;
        _isDefault = isDefault;
    }

    /// <summary>
    /// Retained name. It identifies the profile: it does not change.
    /// </summary>
    public string Name { get; }

    [ObservableProperty]
    private string _summary;

    /// <summary>True if this profile opens at startup.</summary>
    [ObservableProperty]
    private bool _isDefault;

    /// <summary>
    /// What the row announces about itself.
    ///
    /// Without this, a list names its entries after the type: when
    /// sampled by automation, they were all called
    /// "DtHub.App.ViewModels.LaunchProfileRowViewModel". The display
    /// template does not fix that name.
    /// </summary>
    public override string ToString() => $"{Name}  ·  {Summary}";
}
