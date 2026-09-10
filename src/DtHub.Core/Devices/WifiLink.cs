using System.Globalization;

namespace DtHub.Core.Devices;

/// <summary>
/// Ce que le téléphone dit de sa liaison Wi-Fi.
///
/// Lu par <c>cmd wifi status</c>, qui coûte moins d'une demi-seconde et
/// quelques centaines d'octets, là où <c>dumpsys wifi</c> en rend des dizaines
/// de milliers pour les mêmes chiffres.
///
/// Pourquoi lire la liaison plutôt que mesurer un débit : la mesure a été
/// tentée. Cinq transferts de huit mégaoctets sur la même liaison, sans rien
/// d'autre en cours, ont rendu 7,7 puis 11,8, 21,0, 23,6 et 20,5 Mb/s. Du
/// simple au triple d'un instant à l'autre. Un sondage unique au démarrage
/// aurait donc figé la qualité sur un coup de dé, et un sondage assez long
/// pour être fiable aurait coûté plusieurs secondes à chaque lancement. Ce que
/// la liaison annonce, lui, est stable et gratuit.
/// </summary>
/// <param name="LinkSpeedMbps">Vitesse annoncée dans le sens téléphone vers PC.</param>
/// <param name="FrequencyMhz">Fréquence du canal, qui donne la bande.</param>
/// <param name="Standard">Norme négociée, telle quelle : « 11n », « 11ac »…</param>
/// <param name="Rssi">Puissance reçue en dBm, négative.</param>
/// <param name="RetryShare">Part des trames réémises, entre 0 et 1.</param>
public sealed record WifiLink(
    int LinkSpeedMbps,
    int FrequencyMhz,
    string Standard,
    int Rssi,
    double RetryShare)
{
    /// <summary>La bande encombrée, partagée avec les voisins et les micro-ondes.</summary>
    public bool Is24GHz => FrequencyMhz is >= 2400 and < 2500;

    /// <summary>
    /// Lit l'état rendu par <c>cmd wifi status</c>.
    ///
    /// Rend <c>null</c> dès qu'il manque l'essentiel : Wi-Fi éteint, appareil
    /// en USB, sortie d'une version d'Android qui nomme les choses autrement.
    /// Ne rien savoir est un cas ordinaire, pas une faute, et l'appelant sait
    /// s'en passer.
    /// </summary>
    public static WifiLink? Parse(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        // « Tx Link speed » et non « Link speed » : c'est le sens qui porte la
        // vidéo. La virgule initiale écarte « Max Supported Tx Link speed »,
        // qui annonce le plafond du matériel et non la liaison du moment.
        var speed = Number(status, ", Tx Link speed: ") ?? Number(status, "Link speed: ");
        var frequency = Number(status, "Frequency: ");

        if (speed is not > 0 || frequency is not > 0)
        {
            return null;
        }

        var success = Number(status, "successfulTxPackets: ") ?? 0;
        var retried = Number(status, "retriedTxPackets: ") ?? 0;

        return new WifiLink(
            speed.Value,
            frequency.Value,
            Text(status, "Wi-Fi standard: ") ?? "?",
            Number(status, "RSSI: ") ?? 0,
            success + retried > 0 ? retried / (double)(success + retried) : 0);
    }

    /// <summary>Le premier nombre entier qui suit une étiquette, signe compris.</summary>
    private static int? Number(string text, string label)
    {
        var at = text.IndexOf(label, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var start = at + label.Length;
        var end = start;

        if (end < text.Length && text[end] == '-')
        {
            end++;
        }

        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return int.TryParse(
            text.AsSpan(start, end - start),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    /// <summary>Le mot qui suit une étiquette, jusqu'à la virgule.</summary>
    private static string? Text(string text, string label)
    {
        var at = text.IndexOf(label, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var start = at + label.Length;
        var end = text.IndexOf(',', start);

        return (end < 0 ? text[start..] : text[start..end]).Trim();
    }
}
