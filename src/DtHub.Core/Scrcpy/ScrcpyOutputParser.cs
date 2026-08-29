using System.Globalization;
using System.Text.RegularExpressions;

namespace DtHub.Core.Scrcpy;

/// <summary>
/// Lecture de la sortie de scrcpy. Deux informations comptent : l'identifiant
/// de l'afficheur virtuel qu'il vient de créer, et les erreurs à traduire pour
/// l'utilisateur.
/// </summary>
public static partial class ScrcpyOutputParser
{
    /// <summary>
    /// Extrait l'identifiant de l'afficheur virtuel. scrcpy le journalise à la
    /// création, sous la forme
    /// <c>[server] INFO: New display: 1080x1920/320 (id=2)</c>.
    /// </summary>
    public static int? TryParseVirtualDisplayId(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var match = NewDisplay().Match(line);

        return match.Success
               && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    /// <summary>Vrai si la ligne est une erreur signalée par scrcpy.</summary>
    public static bool IsError(string? line) =>
        !string.IsNullOrWhiteSpace(line)
        && line.Contains("ERROR:", StringComparison.Ordinal);

    /// <summary>
    /// Traduit une ligne d'erreur de scrcpy en message compréhensible. Rend
    /// <c>null</c> quand la ligne n'est pas une erreur reconnue, auquel cas
    /// l'appelant conserve un message générique et met le détail au journal.
    /// </summary>
    public static string? DescribeError(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var text = line.ToLowerInvariant();

        if (text.Contains("could not find any adb device", StringComparison.Ordinal)
            || text.Contains("no device found", StringComparison.Ordinal))
        {
            return "Le téléphone n'est plus détecté. Vérifiez le câble ou la connexion Wi-Fi.";
        }

        if (text.Contains("device disconnected", StringComparison.Ordinal)
            || text.Contains("connection reset", StringComparison.Ordinal))
        {
            return "Le téléphone s'est déconnecté pendant la session.";
        }

        if (text.Contains("unauthorized", StringComparison.Ordinal))
        {
            return "Le téléphone n'a pas autorisé ce PC. Déverrouillez l'écran et acceptez la demande de débogage.";
        }

        if (text.Contains("could not create display", StringComparison.Ordinal)
            || text.Contains("virtual display", StringComparison.Ordinal))
        {
            return "Ce téléphone n'a pas pu créer d'écran virtuel. "
                + "La fonction demande Android 11 ou plus récent.";
        }

        if (text.Contains("encoder", StringComparison.Ordinal))
        {
            return "L'encodeur vidéo du téléphone a refusé la session. "
                + "Réduisez le nombre de sessions simultanées ou le débit dans les paramètres.";
        }

        if (text.Contains("server connection failed", StringComparison.Ordinal)
            || text.Contains("could not connect", StringComparison.Ordinal))
        {
            return "La connexion avec le téléphone a échoué. Reconnectez-le, puis réessayez.";
        }

        return null;
    }

    [GeneratedRegex(@"New display:.*?\(id=(\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex NewDisplay();
}
