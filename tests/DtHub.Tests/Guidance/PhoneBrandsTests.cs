using DtHub.Core.Guidance;

namespace DtHub.Tests.Guidance;

public class PhoneBrandsTests
{
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

    [Fact]
    public void Chaque_marque_nomme_son_propre_reglage_de_batterie()
    {
        // Le réglage existe partout, mais aucun constructeur ne l'a nommé
        // comme son voisin : une explication générique laisserait chercher.
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
    public void Un_constructeur_sans_surcouche_ou_inconnu_utilise_la_procedure_standard(string? manufacturer)
    {
        Assert.Same(PhoneBrands.Standard, PhoneBrands.FromManufacturer(manufacturer));
    }

    [Fact]
    public void Deux_entrees_ne_decrivent_jamais_la_meme_procedure()
    {
        // Distinguer des marques dont les chemins sont identiques ne ferait
        // qu'allonger la liste sans rien apprendre.
        var procedures = PhoneBrands.All
            .Select(b => $"{b.BuildNumberPath}|{b.BuildNumberLabel}|{b.DeveloperOptionsPath}")
            .ToList();

        Assert.Equal(procedures.Count, procedures.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Le_telephone_de_reference_est_reconnu()
    {
        // Constructeur exact rapporté par le Xiaomi 13T de référence.
        var brand = PhoneBrands.FromManufacturer("Xiaomi");

        Assert.Contains("HyperOS", brand.BuildNumberLabel, StringComparison.Ordinal);
        Assert.Contains("Paramètres supplémentaires", brand.DeveloperOptionsPath, StringComparison.Ordinal);
        Assert.NotNull(brand.Warning);
    }
}
