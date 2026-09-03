using DtHub.Core.Localization;

namespace DtHub.Core.Guidance;

/// <summary>
/// Marche à suivre pour activer le débogage, propre à une marque. Les chemins
/// de menu diffèrent assez d'une surcouche à l'autre pour qu'une explication
/// générique laisse l'utilisateur chercher.
/// </summary>
public sealed record PhoneBrand
{
    /// <summary>
    /// Identifiant de la fiche. Il compose les clés de ressources, sur la forme
    /// <c>Brand{Key}{Champ}</c>, et n'est jamais montré.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Fragments du nom du constructeur permettant de reconnaître la marque à
    /// partir de <c>ro.product.manufacturer</c>. Seule donnée de cette fiche qui
    /// ne soit pas du texte : elle ne se traduit pas.
    /// </summary>
    public IReadOnlyList<string> Manufacturers { get; init; } = [];

    /// <summary>Nom affiché dans le sélecteur.</summary>
    public string Name => Text(nameof(Name));

    /// <summary>Chemin de menu menant à la ligne à taper sept fois.</summary>
    public string BuildNumberPath => Text(nameof(BuildNumberPath));

    /// <summary>Nom exact de cette ligne sur cette surcouche.</summary>
    public string BuildNumberLabel => Text(nameof(BuildNumberLabel));

    /// <summary>Chemin de menu des options pour les développeurs.</summary>
    public string DeveloperOptionsPath => Text(nameof(DeveloperOptionsPath));

    /// <summary>Particularité de la marque, quand il y en a une.</summary>
    public string? Warning => Maybe(nameof(Warning));

    /// <summary>
    /// Nom que porte, sur cette surcouche, la fonction qui installe une
    /// seconde copie d'une application. Chaque constructeur l'a nommée
    /// autrement, et c'est ce nom qu'il faut chercher dans les menus.
    /// </summary>
    public string CloneFeature => Text(nameof(CloneFeature));

    /// <summary>Chemin de menu menant à cette fonction.</summary>
    public string ClonePath => Text(nameof(ClonePath));

    /// <summary>Ce qu'il faut savoir avant de s'y prendre sur cette marque.</summary>
    public string? CloneNote => Maybe(nameof(CloneNote));

    /// <summary>
    /// Nom que porte, sur cette surcouche, le réglage qui dispense une
    /// application des économies de batterie. Sans lui, Android suspend le jeu
    /// dès qu'il cesse d'être au premier plan, et la fenêtre se fige.
    /// </summary>
    public string BatteryFeature => Text(nameof(BatteryFeature));

    /// <summary>Chemin de menu menant à ce réglage.</summary>
    public string BatteryPath => Text(nameof(BatteryPath));

    /// <summary>Second réglage à désactiver, quand la marque en ajoute un.</summary>
    public string? BatteryNote => Maybe(nameof(BatteryNote));

    private string Text(string field) => Strings.Get($"Brand{Key}{field}");

    private string? Maybe(string field) => Strings.Optional($"Brand{Key}{field}");
}

/// <summary>Marques connues, avec leurs chemins de menu.</summary>
public static class PhoneBrands
{
    /// <summary>
    /// Procédure d'Android sans surcouche. Sert aussi de repli.
    ///
    /// La liste de constructeurs n'est pas décorative : elle évite que des
    /// marques dont les chemins sont bel et bien ceux d'AOSP soient traitées
    /// comme des inconnues. TCL, ZTE, HMD, Fairphone, ASUS et les marques du
    /// groupe Transsion s'écartent peu d'Android nu, et les fabricants de
    /// tablettes d'entrée de gamme encore moins.
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
    /// Marques regroupées par procédure. Les distinguer quand les chemins de
    /// menu sont identiques n'apporterait rien et allongerait la liste.
    ///
    /// Les textes de chaque fiche vivent dans les ressources, sous les clés
    /// <c>Brand{Key}{Champ}</c> : ils étaient écrits ici en français, et une
    /// personne dont l'interface est en espagnol recevait l'aide en français au
    /// moment précis où elle ne s'en sortait pas.
    ///
    /// Seuls les chemins de Xiaomi sont vérifiés sur un vrai téléphone, ce que
    /// dit déjà docs/DECISIONS.md. Les autres sont donnés de bonne foi, dans
    /// les trois langues.
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
    /// Devine la marque à partir du constructeur rapporté par le téléphone.
    /// Sert à présélectionner la bonne explication plutôt que de la faire
    /// chercher.
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
