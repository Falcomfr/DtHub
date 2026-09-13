using DtHub.Core.Localization;

namespace DtHub.Core.Guidance;

/// <summary>
/// Steps to follow to enable debugging, specific to a brand. Menu
/// paths differ enough from one overlay to another that a generic
/// explanation would leave the user searching.
/// </summary>
public sealed record PhoneBrand
{
    /// <summary>
    /// Identifier of the entry. It builds resource keys, in the form
    /// <c>Brand{Key}{Field}</c>, and is never shown.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Fragments of the manufacturer's name used to recognize the
    /// brand from <c>ro.product.manufacturer</c>. The only data in
    /// this entry that is not text: it is not translated.
    /// </summary>
    public IReadOnlyList<string> Manufacturers { get; init; } = [];

    /// <summary>Name shown in the selector.</summary>
    public string Name => Text(nameof(Name));

    /// <summary>Menu path leading to the line to tap seven times.</summary>
    public string BuildNumberPath => Text(nameof(BuildNumberPath));

    /// <summary>Exact name of this line on this overlay.</summary>
    public string BuildNumberLabel => Text(nameof(BuildNumberLabel));

    /// <summary>Menu path to the developer options.</summary>
    public string DeveloperOptionsPath => Text(nameof(DeveloperOptionsPath));

    /// <summary>Peculiarity of the brand, when it has one.</summary>
    public string? Warning => Maybe(nameof(Warning));

    /// <summary>
    /// Name carried, on this overlay, by the feature that installs a
    /// second copy of an application. Each manufacturer named it
    /// differently, and this is the name to look for in the menus.
    /// </summary>
    public string CloneFeature => Text(nameof(CloneFeature));

    /// <summary>Menu path leading to this feature.</summary>
    public string ClonePath => Text(nameof(ClonePath));

    /// <summary>What to know before doing this on this brand.</summary>
    public string? CloneNote => Maybe(nameof(CloneNote));

    /// <summary>
    /// Name carried, on this overlay, by the setting that exempts an
    /// application from battery savings. Without it, Android suspends
    /// the game as soon as it stops being in the foreground, and the
    /// window freezes.
    /// </summary>
    public string BatteryFeature => Text(nameof(BatteryFeature));

    /// <summary>Menu path leading to this setting.</summary>
    public string BatteryPath => Text(nameof(BatteryPath));

    /// <summary>Second setting to disable, when the brand adds one.</summary>
    public string? BatteryNote => Maybe(nameof(BatteryNote));

    private string Text(string field) => Strings.Get($"Brand{Key}{field}");

    private string? Maybe(string field) => Strings.Optional($"Brand{Key}{field}");
}

/// <summary>Known brands, with their menu paths.</summary>
public static class PhoneBrands
{
    /// <summary>
    /// Procedure for Android with no overlay. Also serves as the
    /// fallback.
    ///
    /// The list of manufacturers is not decorative: it keeps brands
    /// whose paths are indeed those of AOSP from being treated as
    /// unknown. TCL, ZTE, HMD, Fairphone, ASUS and the brands of the
    /// Transsion group stray little from stock Android, and entry
    /// level tablet makers even less.
    /// </summary>
    public static readonly PhoneBrand Standard = new()
    {
        Key = "Standard",
        Manufacturers =
        [
            "google", "motorola", "lenovo", "nothing", "sony", "asus", "tcl",
            "alcatel", "zte", "nubia", "hmd", "nokia", "fairphone", "infinix",
            "tecno", "itel", "transsion", "blackview", "doogee", "ulefone",
            "oukitel", "umidigi", "sharp", "crosscall", "wiko",
        ],
    };

    /// <summary>
    /// Brands grouped by procedure. Telling them apart when the menu
    /// paths are identical would add nothing and would lengthen the
    /// list.
    ///
    /// The text of each entry lives in the resources, under the keys
    /// <c>Brand{Key}{Field}</c>: it used to be written here in
    /// French, and a person whose interface was in Spanish received
    /// the help in French at the precise moment they were stuck.
    ///
    /// Only Xiaomi's paths are verified on a real phone, as
    /// docs/DECISIONS.md already states. The others are given in good
    /// faith, in all three languages.
    /// </summary>
    public static readonly IReadOnlyList<PhoneBrand> All =
    [
        new() { Key = "Xiaomi", Manufacturers = ["xiaomi", "redmi", "poco"] },
        new() { Key = "Samsung", Manufacturers = ["samsung"] },
        new() { Key = "OnePlus", Manufacturers = ["oneplus", "oppo", "realme"] },
        new() { Key = "Honor", Manufacturers = ["honor", "huawei"] },
        new() { Key = "Vivo", Manufacturers = ["vivo", "iqoo"] },
        new() { Key = "Amazon", Manufacturers = ["amazon"] },
        Standard,
    ];

    /// <summary>
    /// Guesses the brand from the manufacturer reported by the phone.
    /// Used to preselect the right explanation rather than making the
    /// user search for it.
    /// </summary>
    public static PhoneBrand FromManufacturer(string? manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer))
        {
            return Standard;
        }

        var value = manufacturer.Trim().ToLowerInvariant();

        return All.FirstOrDefault(b => b.Manufacturers.Any(
            m => value.Contains(m, StringComparison.Ordinal))) ?? Standard;
    }
}
