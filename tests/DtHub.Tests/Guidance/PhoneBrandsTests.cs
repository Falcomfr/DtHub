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
        }
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
    [InlineData("Google", "Google Pixel")]
    [InlineData("OnePlus", "OnePlus")]
    [InlineData("realme", "OPPO, realme")]
    [InlineData("HONOR", "Honor, Huawei")]
    [InlineData("Nothing", "Nothing")]
    public void La_marque_est_devinee_a_partir_du_constructeur(string manufacturer, string expected)
    {
        Assert.Equal(expected, PhoneBrands.FromManufacturer(manufacturer).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ConstructeurInconnu")]
    public void Un_constructeur_inconnu_retombe_sur_android_standard(string? manufacturer)
    {
        Assert.Same(PhoneBrands.Standard, PhoneBrands.FromManufacturer(manufacturer));
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
