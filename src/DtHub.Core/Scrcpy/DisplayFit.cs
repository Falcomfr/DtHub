using System.Globalization;
using System.Text.RegularExpressions;

using DtHub.Core.Localization;

namespace DtHub.Core.Scrcpy;

/// <summary>
/// Rapproche la définition demandée de celle qui sert vraiment.
///
/// <see cref="DisplayLadder.For"/> retient le premier palier <b>au-dessus de la
/// fenêtre</b>, puis le plafond de qualité ne fait que le rabaisser. Un plafond
/// choisi au-delà de la taille des fenêtres ne change donc rien du tout : le
/// même afficheur est demandé, au même prix.
///
/// Sans le dire, l'interface laisse croire l'inverse. Mesuré sur le téléphone
/// de référence, l'écart entre 1920x1080 et 2560x1440 vaut 0,63 sur 255 dans
/// une fenêtre de 1428 de haut, pour soixante-dix-huit pour cent de débit en
/// plus ; dans une fenêtre de 1800, il vaut 3,14 et se voit. Le bon réglage
/// dépend donc de la taille des fenêtres, et lui seul peut le dire.
/// </summary>
public static partial class DisplayFit
{
    /// <summary>
    /// Définition réellement demandée, lue sur la ligne de commande d'une
    /// session. <c>null</c> si elle n'y figure pas.
    ///
    /// La ligne de commande est le seul témoin qui ne puisse pas mentir : elle
    /// est ce que scrcpy a reçu, et non ce que l'on croit lui avoir donné.
    /// </summary>
    public static (int Width, int Height)? FromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var match = DisplayPattern().Match(commandLine);

        return match.Success
               && int.TryParse(match.Groups["w"].Value, CultureInfo.InvariantCulture, out var width)
               && int.TryParse(match.Groups["h"].Value, CultureInfo.InvariantCulture, out var height)
               && width > 0 && height > 0
            ? (width, height)
            : null;
    }

    /// <summary>
    /// Ce qui tourne, et si le plafond choisi y change quelque chose.
    ///
    /// Rend une phrase vide quand rien n'est ouvert : on ne devine pas la
    /// taille des fenêtres à venir.
    /// </summary>
    /// <param name="chosenHeight">Plafond de hauteur choisi dans le panneau.</param>
    /// <param name="used">Définition réellement demandée à scrcpy.</param>
    public static string Describe(int chosenHeight, (int Width, int Height)? used)
    {
        if (used is not { Width: > 0, Height: > 0 } display)
        {
            return string.Empty;
        }

        var phrase = Strings.Format("DisplayFitSentence", display.Width, display.Height);

        // Le plafond ne peut que rabaisser : au-dessus de ce qui sert, il est
        // sans effet, et le dire évite de payer pour rien en le montant.
        return chosenHeight > display.Height
            ? phrase + Strings.Get("DisplayFitCapped")
            : phrase;
    }

    [GeneratedRegex(@"--new-display=(?<w>\d+)x(?<h>\d+)")]
    private static partial Regex DisplayPattern();
}
