using System.Globalization;

using DtHub.Core.Guidance;
using DtHub.Core.Localization;

namespace DtHub.Tests.Guidance;

public class PhoneBrandsTests
{
    /// <summary>
    /// The fields a sheet must carry in all three languages.
    /// </summary>
    private static readonly string[] Obligatoires =
    [
        nameof(PhoneBrand.Name), nameof(PhoneBrand.BuildNumberPath),
        nameof(PhoneBrand.BuildNumberLabel), nameof(PhoneBrand.DeveloperOptionsPath),
        nameof(PhoneBrand.CloneFeature), nameof(PhoneBrand.ClonePath),
        nameof(PhoneBrand.BatteryFeature), nameof(PhoneBrand.BatteryPath),
    ];

    /// <summary>The three embedded languages.</summary>
    private static readonly string[] Langues = ["en", "fr", "es"];

    [Fact]
    public void Chaque_marque_indique_un_chemin_complet()
    {
        foreach (var brand in PhoneBrands.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(brand.Name));
            Assert.False(string.IsNullOrWhiteSpace(brand.BuildNumberPath));
            Assert.False(string.IsNullOrWhiteSpace(brand.BuildNumberLabel));
            Assert.False(string.IsNullOrWhiteSpace(brand.DeveloperOptionsPath));
            Assert.False(string.IsNullOrWhiteSpace(brand.CloneFeature));
            Assert.False(string.IsNullOrWhiteSpace(brand.ClonePath));
            Assert.False(string.IsNullOrWhiteSpace(brand.BatteryFeature));
            Assert.False(string.IsNullOrWhiteSpace(brand.BatteryPath));
        }
    }

    /// <summary>
    /// The real check now that the sheets live in resources: a missing
    /// key does not throw, it returns its own name.
    /// "BrandXiaomiClonePath" would then be displayed on screen
    /// without anything turning red, and the empty-string check would
    /// not catch it.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es")]
    public void Chaque_fiche_est_ecrite_dans_les_trois_langues(string langue)
    {
        var culture = CultureInfo.GetCultureInfo(langue);

        foreach (var brand in PhoneBrands.All)
        {
            foreach (var champ in Obligatoires)
            {
                var cle = $"Brand{brand.Key}{champ}";
                var texte = Strings.GetIn(cle, culture);

                Assert.False(
                    string.IsNullOrWhiteSpace(texte) || string.Equals(texte, cle, StringComparison.Ordinal),
                    $"{cle} manque en « {langue} ».");
            }
        }
    }

    /// <summary>
    /// Menu paths must differ from one language to another: that is
    /// the whole point of the work. Brand names, on the other hand,
    /// are not translated, which is why the check is on a path.
    /// </summary>
    [Fact]
    public void Les_chemins_de_menu_sont_bien_traduits()
    {
        var xiaomi = PhoneBrands.All.First(b => b.Key == "Xiaomi");
        var cle = $"Brand{xiaomi.Key}{nameof(PhoneBrand.ClonePath)}";

        var textes = Langues
            .Select(l => Strings.GetIn(cle, CultureInfo.GetCultureInfo(l)))
            .ToList();

        Assert.Equal(3, textes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Chaque_marque_nomme_son_propre_reglage_de_batterie()
    {
        // The setting exists everywhere, but no manufacturer has named
        // it like its neighbor: a generic explanation would leave you
        // searching.
        var settings = PhoneBrands.All.Select(b => b.BatteryPath).ToList();

        Assert.Equal(settings.Count, settings.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Le_repli_figure_dans_la_liste_et_arrive_en_dernier()
    {
        Assert.Contains(PhoneBrands.Standard, PhoneBrands.All);
        Assert.Same(PhoneBrands.Standard, PhoneBrands.All[^1]);
    }

    [Theory]
    [InlineData("Xiaomi", "Xiaomi, Redmi, POCO")]
    [InlineData("xiaomi", "Xiaomi, Redmi, POCO")]
    [InlineData("Redmi", "Xiaomi, Redmi, POCO")]
    [InlineData("samsung", "Samsung")]
    [InlineData("OnePlus", "OnePlus, OPPO, realme")]
    [InlineData("realme", "OnePlus, OPPO, realme")]
    [InlineData("HONOR", "Honor, Huawei")]
    [InlineData("vivo", "vivo, iQOO")]
    [InlineData("iQOO", "vivo, iQOO")]
    [InlineData("Amazon", "Amazon Fire")]
    public void La_marque_est_devinee_a_partir_du_constructeur(string manufacturer, string expected)
    {
        Assert.Equal(expected, PhoneBrands.FromManufacturer(manufacturer).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ConstructeurInconnu")]
    [InlineData("Google")]
    [InlineData("Motorola")]
    [InlineData("Nothing")]
    [InlineData("asus")]
    [InlineData("TCL")]
    [InlineData("ZTE")]
    [InlineData("HMD Global")]
    [InlineData("Fairphone")]
    [InlineData("Infinix")]
    [InlineData("TECNO")]
    public void Un_constructeur_sans_surcouche_ou_inconnu_utilise_la_procedure_standard(string? manufacturer)
    {
        Assert.Same(PhoneBrands.Standard, PhoneBrands.FromManufacturer(manufacturer));
    }

    [Fact]
    public void Deux_entrees_ne_decrivent_jamais_la_meme_procedure()
    {
        // Distinguishing brands whose paths are identical would only
        // make the list longer without teaching us anything.
        var procedures = PhoneBrands.All
            .Select(b => $"{b.BuildNumberPath}|{b.BuildNumberLabel}|{b.DeveloperOptionsPath}")
            .ToList();

        Assert.Equal(procedures.Count, procedures.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Le_telephone_de_reference_est_reconnu()
    {
        // Exact manufacturer reported by the reference Xiaomi 13T.
        var brand = PhoneBrands.FromManufacturer("Xiaomi");

        Assert.Contains("HyperOS", brand.BuildNumberLabel, StringComparison.Ordinal);
        Assert.Contains("Paramètres supplémentaires", brand.DeveloperOptionsPath, StringComparison.Ordinal);
        Assert.NotNull(brand.Warning);
    }

    [Fact]
    public void Les_chemins_de_menu_valent_aussi_pour_une_tablette()
    {
        // A Galaxy Tab has no "À propos du téléphone" ("About phone")
        // line. The missing word alone was enough to make the path
        // unfindable.
        foreach (var brand in PhoneBrands.All)
        {
            if (brand.BuildNumberPath.Contains("téléphone", StringComparison.Ordinal))
            {
                Assert.Contains("tablette", brand.BuildNumberPath, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Une_marche_a_suivre_qui_ne_promet_pas_le_resultat_le_dit()
    {
        // Fire OS does not have the Play Store. Giving the menus
        // without saying so would send the user through an entire
        // procedure for nothing.
        var fire = PhoneBrands.FromManufacturer("Amazon");

        Assert.NotNull(fire.Warning);
        Assert.Contains("Play Store", fire.Warning, StringComparison.Ordinal);
    }
}
