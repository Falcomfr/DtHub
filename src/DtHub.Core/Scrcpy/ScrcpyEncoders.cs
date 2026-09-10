using System.Globalization;

namespace DtHub.Core.Scrcpy;

/// <summary>Un encodeur vidéo tel que l'appareil le déclare.</summary>
/// <param name="Codec">Le codec qu'il sert, « h264 » ou « h265 ».</param>
/// <param name="Name">Son nom, à passer à <c>--video-encoder</c>.</param>
/// <param name="Hardware">Vrai s'il est matériel.</param>
/// <param name="IsAlias">Vrai s'il n'est qu'un autre nom d'un encodeur déjà listé.</param>
public readonly record struct VideoEncoder(string Codec, string Name, bool Hardware, bool IsAlias);

/// <summary>
/// Ce que l'appareil sait encoder, et avec quoi.
///
/// Un encodeur logiciel sur un téléphone donne une image qui saccade et un
/// appareil qui chauffe, pour le même travail. La question se pose donc
/// vraiment, et elle n'a pas la même réponse partout : sur l'appareil de
/// référence, h264 et h265 ont chacun un encodeur matériel, alors qu'av1 et
/// vp8 n'ont que du logiciel.
///
/// **scrcpy fait le plus dur.** Sa sortie `--list-encoders` étiquette
/// elle-même chaque entrée, relevé au caractère près sur un Xiaomi 13T Pro
/// sous Android 16 :
///
/// <code>
///     --video-codec=h264 --video-encoder=c2.mtk.avc.encoder     (hw) [vendor]
///     --video-codec=h264 --video-encoder=c2.android.avc.encoder (sw)
/// </code>
///
/// Il n'y a donc rien à deviner et aucune interrogation d'API à faire : il
/// suffit de lire.
/// </summary>
public static class ScrcpyEncoders
{
    private const string CodecMarker = "--video-codec=";
    private const string EncoderMarker = "--video-encoder=";

    /// <summary>
    /// Les encodeurs vidéo déclarés. Les lignes d'encodeurs audio sont
    /// écartées d'elles-mêmes : elles ne portent pas <c>--video-codec=</c>.
    /// </summary>
    public static IReadOnlyList<VideoEncoder> Parse(string? listing)
    {
        if (string.IsNullOrWhiteSpace(listing))
        {
            return [];
        }

        List<VideoEncoder> encoders = [];

        foreach (var line in listing.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var codecAt = line.IndexOf(CodecMarker, StringComparison.Ordinal);
            var encoderAt = line.IndexOf(EncoderMarker, StringComparison.Ordinal);

            if (codecAt < 0 || encoderAt < 0)
            {
                continue;
            }

            var codec = Word(line, codecAt + CodecMarker.Length);
            var name = Word(line, encoderAt + EncoderMarker.Length);

            if (codec.Length == 0 || name.Length == 0)
            {
                continue;
            }

            encoders.Add(new VideoEncoder(
                codec,
                name,
                line.Contains("(hw)", StringComparison.OrdinalIgnoreCase),
                line.Contains("(alias for", StringComparison.OrdinalIgnoreCase)));
        }

        return encoders;
    }

    /// <summary>
    /// L'encodeur à imposer pour ce codec, ou <c>null</c> quand il n'y a rien
    /// à faire.
    ///
    /// **On n'impose que lorsque c'est utile.** Un appareil dont le premier
    /// encodeur listé est déjà matériel n'a besoin de rien : forcer un nom
    /// n'apporterait aucune image de plus et ajouterait une façon d'échouer.
    /// Un appareil qui n'a que du logiciel n'a rien à imposer non plus, il n'y
    /// a pas d'alternative.
    ///
    /// Les alias sont écartés : ce sont d'autres noms d'un encodeur déjà
    /// listé, et en choisir un ne changerait rien qu'un risque.
    /// </summary>
    public static string? Force(IReadOnlyList<VideoEncoder> encoders, string? codec)
    {
        ArgumentNullException.ThrowIfNull(encoders);

        var forCodec = encoders
            .Where(e => !e.IsAlias && Matches(e.Codec, codec))
            .ToList();

        if (forCodec.Count == 0 || forCodec[0].Hardware)
        {
            return null;
        }

        return forCodec.FirstOrDefault(e => e.Hardware).Name is { Length: > 0 } hardware ? hardware : null;
    }

    /// <summary>
    /// Vrai quand ce codec n'a aucun encodeur matériel sur cet appareil, donc
    /// que l'image saccadera quoi qu'on fasse.
    ///
    /// Rendre faux quand la liste est vide : ne pas savoir n'est pas savoir
    /// que c'est mauvais, et alarmer sur une ignorance serait pire que se
    /// taire.
    /// </summary>
    public static bool SoftwareOnly(IReadOnlyList<VideoEncoder> encoders, string? codec)
    {
        ArgumentNullException.ThrowIfNull(encoders);

        var forCodec = encoders.Where(e => Matches(e.Codec, codec)).ToList();

        return forCodec.Count > 0 && !forCodec.Exists(e => e.Hardware);
    }

    /// <summary>Les codecs qui ont un encodeur matériel, dans l'ordre déclaré.</summary>
    public static IReadOnlyList<string> HardwareCodecs(IReadOnlyList<VideoEncoder> encoders)
    {
        ArgumentNullException.ThrowIfNull(encoders);

        return [.. encoders.Where(e => e.Hardware).Select(e => e.Codec).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static bool Matches(string codec, string? wanted) =>
        string.Equals(codec, (wanted ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Le mot qui suit une position, jusqu'au prochain blanc.</summary>
    private static string Word(string line, int start)
    {
        var end = start;

        while (end < line.Length && !char.IsWhiteSpace(line[end]))
        {
            end++;
        }

        return line[start..end].Trim();
    }
}
