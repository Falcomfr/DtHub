using DtHub.Core;

namespace DtHub.Tests;

public class ProductInfoTests
{
    [Fact]
    public void Slug_ne_contient_ni_espace_ni_caractere_interdit_dans_un_chemin()
    {
        Assert.False(string.IsNullOrWhiteSpace(ProductInfo.Slug));
        Assert.DoesNotContain(' ', ProductInfo.Slug);
        Assert.Empty(ProductInfo.Slug.Intersect(Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public void Version_est_renseignee()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", ProductInfo.Version);
    }
}
