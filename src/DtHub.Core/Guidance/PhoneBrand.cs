namespace DtHub.Core.Guidance;

/// <summary>
/// Marche à suivre pour activer le débogage, propre à une marque. Les chemins
/// de menu diffèrent assez d'une surcouche à l'autre pour qu'une explication
/// générique laisse l'utilisateur chercher.
/// </summary>
public sealed record PhoneBrand
{
    /// <summary>Nom affiché dans le sélecteur.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Fragments du nom du constructeur permettant de reconnaître la marque à
    /// partir de <c>ro.product.manufacturer</c>.
    /// </summary>
    public IReadOnlyList<string> Manufacturers { get; init; } = [];

    /// <summary>Chemin de menu menant à la ligne à taper sept fois.</summary>
    public required string BuildNumberPath { get; init; }

    /// <summary>Nom exact de cette ligne sur cette surcouche.</summary>
    public required string BuildNumberLabel { get; init; }

    /// <summary>Chemin de menu des options pour les développeurs.</summary>
    public required string DeveloperOptionsPath { get; init; }

    /// <summary>Particularité de la marque, quand il y en a une.</summary>
    public string? Warning { get; init; }
}

/// <summary>Marques connues, avec leurs chemins de menu.</summary>
public static class PhoneBrands
{
    /// <summary>Repli quand la marque n'est pas reconnue.</summary>
    public static readonly PhoneBrand Standard = new()
    {
        Name = "Android standard",
        BuildNumberPath = "Paramètres  ›  À propos du téléphone",
        BuildNumberLabel = "Numéro de build",
        DeveloperOptionsPath = "Paramètres  ›  Système  ›  Options pour les développeurs",
    };

    /// <summary>Toutes les marques, dans l'ordre d'affichage.</summary>
    public static readonly IReadOnlyList<PhoneBrand> All =
    [
        new()
        {
            Name = "Xiaomi, Redmi, POCO",
            Manufacturers = ["xiaomi", "redmi", "poco"],
            BuildNumberPath = "Paramètres  ›  À propos du téléphone",
            BuildNumberLabel = "Version HyperOS, ou Version MIUI sur les modèles plus anciens",
            DeveloperOptionsPath =
                "Paramètres  ›  Paramètres supplémentaires  ›  Options pour les développeurs",
            Warning =
                "Sur HyperOS et MIUI, le débogage USB demande parfois une carte SIM insérée et "
                + "un compte Xiaomi connecté. Activez aussi « Débogage USB (réglages de sécurité) » "
                + "si la ligne existe.",
        },
        new()
        {
            Name = "Samsung",
            Manufacturers = ["samsung"],
            BuildNumberPath = "Paramètres  ›  À propos du téléphone  ›  Informations sur le logiciel",
            BuildNumberLabel = "Numéro de version",
            DeveloperOptionsPath = "Paramètres  ›  Options de développement",
        },
        new()
        {
            Name = "Google Pixel",
            Manufacturers = ["google"],
            BuildNumberPath = "Paramètres  ›  À propos du téléphone",
            BuildNumberLabel = "Numéro de build",
            DeveloperOptionsPath = "Paramètres  ›  Système  ›  Options pour les développeurs",
        },
        new()
        {
            Name = "OnePlus",
            Manufacturers = ["oneplus"],
            BuildNumberPath = "Paramètres  ›  À propos de l'appareil  ›  Version",
            BuildNumberLabel = "Numéro de build",
            DeveloperOptionsPath =
                "Paramètres  ›  Paramètres supplémentaires  ›  Options pour les développeurs",
        },
        new()
        {
            Name = "OPPO, realme",
            Manufacturers = ["oppo", "realme"],
            BuildNumberPath = "Paramètres  ›  À propos de l'appareil  ›  Version",
            BuildNumberLabel = "Numéro de version",
            DeveloperOptionsPath =
                "Paramètres  ›  Paramètres supplémentaires  ›  Options pour les développeurs",
        },
        new()
        {
            Name = "Honor, Huawei",
            Manufacturers = ["honor", "huawei"],
            BuildNumberPath = "Paramètres  ›  À propos du téléphone",
            BuildNumberLabel = "Numéro de build",
            DeveloperOptionsPath =
                "Paramètres  ›  Système et mises à jour  ›  Options pour les développeurs",
            Warning =
                "Sur les versions sans services Google, le débogage sans fil est parfois absent. "
                + "Le câble USB reste alors la seule voie.",
        },
        new()
        {
            Name = "Motorola",
            Manufacturers = ["motorola", "lenovo"],
            BuildNumberPath = "Paramètres  ›  À propos du téléphone",
            BuildNumberLabel = "Numéro de build",
            DeveloperOptionsPath = "Paramètres  ›  Système  ›  Options pour les développeurs",
        },
        new()
        {
            Name = "Nothing",
            Manufacturers = ["nothing"],
            BuildNumberPath = "Paramètres  ›  À propos du téléphone",
            BuildNumberLabel = "Numéro de build",
            DeveloperOptionsPath = "Paramètres  ›  Système  ›  Options pour les développeurs",
        },
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
