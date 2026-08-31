using System.Globalization;

namespace DtHub.Core.Devices;

/// <summary>
/// Ce que DT Hub exige de l'appareil, et de quoi le dire avant d'essayer.
///
/// L'exigence n'est pas la nôtre : c'est celle de l'afficheur virtuel, qu'Android
/// n'expose qu'à partir de la version 11. Sans ce contrôle, un appareil plus
/// ancien va jusqu'au bout du lancement, attend le délai complet, et reçoit un
/// message deviné à partir de la sortie anglaise de scrcpy, parfois le mauvais.
/// Le niveau d'API est déjà lu à la découverte : autant s'en servir.
/// </summary>
public static class AndroidRequirements
{
    /// <summary>Niveau d'API d'Android 11, où paraît l'afficheur virtuel.</summary>
    public const int VirtualDisplaySdk = 30;

    /// <summary>Nom de cette version, tel qu'on le dit à l'utilisateur.</summary>
    public const string VirtualDisplayVersion = "Android 11";

    /// <summary>
    /// Raison pour laquelle cet appareil ne peut pas ouvrir de fenêtre de jeu,
    /// ou <c>null</c> s'il le peut.
    ///
    /// Rend aussi <c>null</c> quand le niveau d'API n'a pas pu être lu : on ne
    /// refuse pas un appareil sur une ignorance, on le laisse essayer.
    /// </summary>
    public static string? DescribeVirtualDisplayShortfall(int? sdkVersion, string? androidVersion)
    {
        if (sdkVersion is not { } sdk || sdk >= VirtualDisplaySdk)
        {
            return null;
        }

        return $"cet appareil est en {Describe(sdk, androidVersion)}, "
            + $"or ouvrir une fenêtre de jeu demande {VirtualDisplayVersion} ou plus récent.";
    }

    private static string Describe(int sdk, string? androidVersion) =>
        string.IsNullOrWhiteSpace(androidVersion)
            ? "niveau d'API " + sdk.ToString(CultureInfo.InvariantCulture)
            : "Android " + androidVersion.Trim();
}
