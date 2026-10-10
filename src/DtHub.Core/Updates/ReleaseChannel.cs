namespace DtHub.Core.Updates;

/// <summary>
/// Where releases come from.
///
/// A public repository: the application follows its "releases/latest"
/// link, without a token or the API. As long as the repository does
/// not exist, the request returns "nothing to report" and the
/// application knows no more.
///
/// The procedure for releasing is in docs/LIVRAISON.md.
/// </summary>
public static class ReleaseChannel
{
    /// <summary>The account that hosts the repository.</summary>
    public const string Owner = "Falcomfr";

    /// <summary>The repository.</summary>
    public const string Repository = "DtHub";
}
