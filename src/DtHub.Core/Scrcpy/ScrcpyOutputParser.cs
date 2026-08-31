using System.Globalization;
using System.Text.RegularExpressions;

namespace DtHub.Core.Scrcpy;

/// <summary>
/// Nature d'un refus de scrcpy. Le message affiché ne suffit pas : il faut aussi
/// savoir si une seconde tentative a une chance d'aboutir. Retenter à une
/// définition plus modeste répare un encodeur saturé, et ne répare rien du tout
/// quand le téléphone est débranché : ce serait trente secondes d'attente de
/// plus pour le même échec.
/// </summary>
public enum ScrcpyFailureKind
{
    /// <summary>Aucun refus.</summary>
    None = 0,

    /// <summary>Refus non reconnu. La définition en fait partie des causes possibles.</summary>
    Unknown,

    /// <summary>L'afficheur virtuel n'est jamais apparu dans le temps imparti.</summary>
    Timeout,

    /// <summary>L'encodeur vidéo a refusé la définition ou le débit demandés.</summary>
    Encoder,

    /// <summary>L'appareil a refusé de créer l'afficheur virtuel.</summary>
    VirtualDisplayRefused,

    /// <summary>L'appareil n'a jamais été trouvé.</summary>
    DeviceGone,

    /// <summary>L'appareil s'est déconnecté en cours de session.</summary>
    DeviceDisconnected,

    /// <summary>L'appareil n'a pas autorisé ce PC.</summary>
    Unauthorized,

    /// <summary>La liaison avec l'appareil n'a pas pu s'établir.</summary>
    ConnectionFailed,

    /// <summary>scrcpy ou ADB manque, ou n'a pas pu démarrer sur ce PC.</summary>
    Environment,
}

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
    /// Range une ligne d'erreur de scrcpy dans une catégorie. La reconnaissance
    /// porte sur la sortie anglaise de scrcpy, qui n'est pas contractuelle :
    /// tout ce qui n'est pas reconnu devient <see cref="ScrcpyFailureKind.Unknown"/>,
    /// jamais une catégorie devinée.
    /// </summary>
    public static ScrcpyFailureKind Classify(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return ScrcpyFailureKind.None;
        }

        var text = line.ToLowerInvariant();

        if (text.Contains("could not find any adb device", StringComparison.Ordinal)
            || text.Contains("no device found", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.DeviceGone;
        }

        if (text.Contains("device disconnected", StringComparison.Ordinal)
            || text.Contains("connection reset", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.DeviceDisconnected;
        }

        if (text.Contains("unauthorized", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.Unauthorized;
        }

        if (text.Contains("could not create display", StringComparison.Ordinal)
            || text.Contains("virtual display", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.VirtualDisplayRefused;
        }

        if (text.Contains("encoder", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.Encoder;
        }

        if (text.Contains("server connection failed", StringComparison.Ordinal)
            || text.Contains("could not connect", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.ConnectionFailed;
        }

        return ScrcpyFailureKind.Unknown;
    }

    /// <summary>
    /// Traduit une ligne d'erreur de scrcpy en message compréhensible. Rend
    /// <c>null</c> quand la ligne n'est pas une erreur reconnue, auquel cas
    /// l'appelant conserve un message générique et met le détail au journal.
    /// </summary>
    public static string? DescribeError(string? line) => Describe(Classify(line));

    /// <summary>Message correspondant à une catégorie de refus.</summary>
    public static string? Describe(ScrcpyFailureKind kind) => kind switch
    {
        ScrcpyFailureKind.DeviceGone =>
            "Le téléphone n'est plus détecté. Vérifiez le câble ou la connexion Wi-Fi.",
        ScrcpyFailureKind.DeviceDisconnected =>
            "Le téléphone s'est déconnecté pendant la session.",
        ScrcpyFailureKind.Unauthorized =>
            "Le téléphone n'a pas autorisé ce PC. Déverrouillez l'écran et acceptez la demande de débogage.",
        ScrcpyFailureKind.VirtualDisplayRefused =>
            "Ce téléphone n'a pas pu créer d'écran virtuel. "
            + "La fonction demande Android 11 ou plus récent.",
        ScrcpyFailureKind.Encoder =>
            "L'encodeur vidéo du téléphone a refusé la session. "
            + "Réduisez le nombre de sessions simultanées ou le débit dans les paramètres.",
        ScrcpyFailureKind.ConnectionFailed =>
            "La connexion avec le téléphone a échoué. Reconnectez-le, puis réessayez.",
        _ => null,
    };

    /// <summary>
    /// Vrai si une seconde tentative à une définition plus modeste a une chance
    /// d'aboutir. Un appareil débranché ou non autorisé le restera : insister
    /// ne ferait qu'ajouter une attente à l'échec.
    /// </summary>
    public static bool CanRetrySmaller(ScrcpyFailureKind kind) => kind
        is ScrcpyFailureKind.Encoder
        or ScrcpyFailureKind.VirtualDisplayRefused
        or ScrcpyFailureKind.Timeout
        or ScrcpyFailureKind.Unknown;

    [GeneratedRegex(@"New display:.*?\(id=(\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex NewDisplay();
}
