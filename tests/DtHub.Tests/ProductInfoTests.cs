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
    public void DiscordUrl_est_une_invitation_https_que_OpenUrl_accepte()
    {
        // OpenUrl ignores anything but https: a plain http link would make
        // the button do nothing at all, without a word.
        var uri = new Uri(ProductInfo.DiscordUrl);

        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal("discord.gg", uri.Host);
        Assert.Matches("^/[A-Za-z0-9]+$", uri.AbsolutePath);
    }

    [Fact]
    public void Version_est_renseignee()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", ProductInfo.Version);
    }
}
