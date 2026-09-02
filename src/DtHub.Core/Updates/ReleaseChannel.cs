namespace DtHub.Core.Updates;

/// <summary>
/// D'où viennent les livraisons.
///
/// Un dépôt public : l'application interroge l'API sans jeton, et un jeton posé
/// dans l'exécutable serait de toute façon lisible par qui l'ouvre. Tant que le
/// dépôt n'existe pas, la demande rend « rien à signaler » et l'application
/// n'en sait pas plus.
///
/// La marche à suivre pour livrer est dans docs/LIVRAISON.md.
/// </summary>
public static class ReleaseChannel
{
    /// <summary>Le compte qui héberge le dépôt.</summary>
    public const string Owner = "Falcomfr";

    /// <summary>Le dépôt.</summary>
    public const string Repository = "DtHub";
}
