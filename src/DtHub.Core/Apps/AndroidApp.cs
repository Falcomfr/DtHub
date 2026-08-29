using System.Globalization;

namespace DtHub.Core.Apps;

/// <summary>
/// Une application installée pour un utilisateur Android donné. La même
/// application clonée sur deux profils donne deux entrées distinctes : ce sont
/// deux cibles de lancement différentes.
/// </summary>
public sealed record AndroidApp
{
    public required string PackageName { get; init; }

    /// <summary>Utilisateur Android pour lequel l'application est installée.</summary>
    public required int UserId { get; init; }

    /// <summary>
    /// Vrai nom de l'application, quand il a pu être lu. Rien ne garantit
    /// qu'on l'obtienne : l'affichage retombe alors sur le nom de paquet.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// Composant à lancer, sous la forme <c>paquet/activité</c>. Absent tant
    /// que l'activité principale n'a pas été résolue.
    /// </summary>
    public string? LaunchComponent { get; init; }

    /// <summary>Application préinstallée ou système.</summary>
    public bool IsSystem { get; init; }

    /// <summary>Vrai si l'application peut être lancée telle quelle.</summary>
    public bool IsLaunchable => !string.IsNullOrWhiteSpace(LaunchComponent);

    /// <summary>Nom affiché, avec repli lisible sur le nom de paquet.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Label)
        ? HumanizePackageName(PackageName)
        : Label.Trim();

    /// <summary>
    /// Rend un nom de paquet présentable quand le vrai nom est indisponible :
    /// dernier segment, séparateurs remplacés, première lettre en capitale.
    /// </summary>
    public static string HumanizePackageName(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return "(inconnu)";
        }

        var segment = packageName.Split('.').LastOrDefault(s => s.Length > 0) ?? packageName;
        segment = segment.Replace('_', ' ').Replace('-', ' ').Trim();

        if (segment.Length == 0)
        {
            return packageName;
        }

        return char.ToUpper(segment[0], CultureInfo.CurrentCulture) + segment[1..];
    }
}
