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

    /// <summary>
    /// Nom que porte, sur cette surcouche, la fonction qui installe une
    /// seconde copie d'une application. Chaque constructeur l'a nommée
    /// autrement, et c'est ce nom qu'il faut chercher dans les menus.
    /// </summary>
    public required string CloneFeature { get; init; }

    /// <summary>Chemin de menu menant à cette fonction.</summary>
    public required string ClonePath { get; init; }

    /// <summary>Ce qu'il faut savoir avant de s'y prendre sur cette marque.</summary>
    public string? CloneNote { get; init; }
}

/// <summary>Marques connues, avec leurs chemins de menu.</summary>
public static class PhoneBrands
{
    /// <summary>
    /// Procédure d'Android sans surcouche. Sert aussi de repli, et couvre
    /// Google Pixel, Motorola, Nothing et Sony, dont les chemins sont les
    /// mêmes.
    /// </summary>
    public static readonly PhoneBrand Standard = new()
    {
        Name = "Google Pixel, Motorola, Nothing, Sony, autre",
        CloneFeature = "Utilisateurs multiples",
        ClonePath = "Paramètres  ›  Système  ›  Utilisateurs multiples",
        CloneNote =
            "Android sans surcouche n'a pas de fonction de duplication. La voie est d'ajouter un second utilisateur, puis d'y installer le jeu depuis le Play Store. DT Hub ouvre chaque profil sur son propre affichage, sans avoir à basculer de l'un à l'autre.",
        Manufacturers = ["google", "motorola", "lenovo", "nothing", "sony"],
        BuildNumberPath = "Paramètres  ›  À propos du téléphone",
        BuildNumberLabel = "Numéro de build",
        DeveloperOptionsPath = "Paramètres  ›  Système  ›  Options pour les développeurs",
    };

    /// <summary>
    /// Marques regroupées par procédure. Les distinguer quand les chemins de
    /// menu sont identiques n'apporterait rien et allongerait la liste.
    /// </summary>
    public static readonly IReadOnlyList<PhoneBrand> All =
    [
        new()
        {
            Name = "Xiaomi, Redmi, POCO",
            CloneFeature = "Applications doubles",
            ClonePath = "Paramètres  ›  Applications  ›  Applications doubles",
            CloneNote =
                "Cette marque propose aussi « Second espace », qui crée un espace complet plutôt qu'une simple copie. Les deux conviennent : DT Hub voit les instances dans les deux cas.",
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
            CloneFeature = "Dossier sécurisé",
            ClonePath = "Paramètres  ›  Sécurité et confidentialité  ›  Dossier sécurisé",
            CloneNote =
                "« Dual Messenger » ne duplique que les applications de messagerie et ne convient donc pas pour un jeu. Le dossier sécurisé accepte n'importe quelle application, et demande un compte Samsung.",
            Manufacturers = ["samsung"],
            BuildNumberPath = "Paramètres  ›  À propos du téléphone  ›  Informations sur le logiciel",
            BuildNumberLabel = "Numéro de version",
            DeveloperOptionsPath = "Paramètres  ›  Options de développement",
        },
        new()
        {
            Name = "OnePlus, OPPO, realme",
            CloneFeature = "Clonage d'applications",
            ClonePath = "Paramètres  ›  Applications  ›  Clonage d'applications",
            Manufacturers = ["oneplus", "oppo", "realme"],
            BuildNumberPath = "Paramètres  ›  À propos de l'appareil  ›  Version",
            BuildNumberLabel = "Numéro de build, ou Numéro de version selon la version installée",
            DeveloperOptionsPath =
                "Paramètres  ›  Paramètres supplémentaires  ›  Options pour les développeurs",
        },
        new()
        {
            Name = "Honor, Huawei",
            CloneFeature = "Double instance d'application",
            ClonePath = "Paramètres  ›  Applications  ›  Double instance d'application",
            CloneNote =
                "Sur les versions sans services Google, l'installation de la seconde copie peut demander de passer par la boutique du constructeur.",
            Manufacturers = ["honor", "huawei"],
            BuildNumberPath = "Paramètres  ›  À propos du téléphone",
            BuildNumberLabel = "Numéro de build",
            DeveloperOptionsPath =
                "Paramètres  ›  Système et mises à jour  ›  Options pour les développeurs",
            Warning =
                "Sur les versions sans services Google, le débogage sans fil est parfois absent. "
                + "Le câble USB reste alors la seule voie.",
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
