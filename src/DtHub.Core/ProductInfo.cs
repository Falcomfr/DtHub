namespace DtHub.Core;

/// <summary>
/// Identité du produit. Point unique de renommage côté code.
/// Le pendant MSBuild se trouve dans Directory.Build.props (voir AGENTS.md).
/// </summary>
public static class ProductInfo
{
    /// <summary>Nom affiché dans l'interface, l'installateur et les fenêtres.</summary>
    public const string Name = "DT Hub";

    /// <summary>
    /// Identifiant technique sans espace : nom du dossier de données et de
    /// l'exécutable. Volontairement dissocié du nom affiché, pour qu'un
    /// changement de nom ne déplace pas les réglages de l'utilisateur.
    /// </summary>
    public const string Slug = "DtHub";

    /// <summary>Dépôt public, utilisé par la vérification de mises à jour.</summary>
    public const string RepositoryUrl = "https://github.com/Falcomfr/DtHub";

    /// <summary>Version affichée, alimentée par l'assembly à l'exécution.</summary>
    public static string Version { get; } =
        typeof(ProductInfo).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Select(a => a.InformationalVersion.Split('+')[0])
            .FirstOrDefault() ?? "0.0.0";
}
