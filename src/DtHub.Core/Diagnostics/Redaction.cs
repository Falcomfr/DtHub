using System.Text.RegularExpressions;

namespace DtHub.Core.Diagnostics;

/// <summary>
/// Retire d'un texte ce qui désigne une personne ou son matériel, avant qu'il
/// ne parte dans un rapport.
///
/// Le besoin est mesuré, non supposé. Sur sept fichiers de journal réels, sept
/// mille huit cent soixante-dix lignes, près de trois lignes sur dix portent une
/// donnée identifiante : l'adresse et le port du téléphone, son numéro de série
/// matériel, le nom d'utilisateur Windows dans un chemin. Les erreurs, elles,
/// font sept lignes sur cent. Envoyer le journal tel quel reviendrait à livrer
/// tout le reste pour obtenir celles-là.
///
/// Le numéro de série arrive par un chemin que personne n'a voulu : scrcpy
/// l'imprime dans sa sortie, et l'application recopie cette sortie mot pour mot
/// dans son journal. C'est pourquoi la biffure travaille sur le texte fini et
/// non à la source.
///
/// <see cref="Processes.ProcessRequest"/> masque déjà les codes d'appairage,
/// mais par égalité exacte : la règle ne mordrait pas sur « --serial=… », où la
/// valeur est collée à son option. Ici la biffure se fait par motif.
/// </summary>
public static partial class Redaction
{
    /// <summary>Ce qui remplace une valeur retirée.</summary>
    public const string Placeholder = "…";

    /// <summary>
    /// Rend le texte débarrassé de ce qui identifie.
    /// </summary>
    /// <param name="text">Le texte à nettoyer.</param>
    /// <param name="secrets">
    /// Valeurs que l'application connaît et qui doivent partir où qu'elles
    /// soient : numéros de série des appareils, adresses relevées, noms que la
    /// personne a donnés à ses comptes.
    /// </param>
    public static string Apply(string? text, IEnumerable<string>? secrets = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Les formes reconnaissables d'abord. L'ordre compte : biffer un
        // numéro de série connu avant de reconnaître le nom mDNS qui le
        // contient casserait la forme de ce nom, et le reste du nom
        // survivrait. Chaque motif emporte donc son tout avant qu'on ne
        // s'attaque aux morceaux.
        var clean = MdnsPattern().Replace(text, Placeholder);

        clean = AddressPattern().Replace(clean, Placeholder);
        clean = SerialOptionPattern().Replace(clean, "--serial=" + Placeholder);
        clean = DisplayPattern().Replace(clean, Placeholder);
        clean = UserPathPattern().Replace(clean, @"C:\Users\" + Placeholder + @"\");

        // Puis ce que l'application sait d'avance, par la chaîne la plus longue
        // pour la même raison : un nom contenu dans un autre part avec lui.
        if (secrets is not null)
        {
            foreach (var secret in secrets
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Trim().Length >= 4)
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(s => s.Length))
            {
                clean = clean.Replace(secret, Placeholder, StringComparison.OrdinalIgnoreCase);
            }
        }

        return clean;
    }

    /// <summary>
    /// Le nom du débogage sans fil, « adb-XXXX-YYYY._adb-tls-connect._tcp ».
    /// Il porte le numéro de série matériel, identifiant stable de l'appareil,
    /// et le dépôt le sait déjà : <c>MdnsDeviceName.HardwareSerialFrom</c> l'en
    /// extrait. Relevé quatre-vingt-quatorze fois dans les journaux.
    /// </summary>
    [GeneratedRegex(@"adb-[A-Za-z0-9]+-[A-Za-z0-9]+\._adb-tls-[a-z]+\._tcp", RegexOptions.None, 500)]
    private static partial Regex MdnsPattern();

    /// <summary>Une adresse IPv4, son port s'il en porte un.</summary>
    [GeneratedRegex(@"\b\d{1,3}(?:\.\d{1,3}){3}(?::\d{1,5})?\b", RegexOptions.None, 500)]
    private static partial Regex AddressPattern();

    /// <summary>La valeur collée à « --serial= », que l'égalité exacte manque.</summary>
    [GeneratedRegex(@"--serial=\S+", RegexOptions.None, 500)]
    private static partial Regex SerialOptionPattern();

    /// <summary>
    /// Le nom de périphérique d'un écran, « \\.\DISPLAY11 ». C'est une
    /// empreinte de machine sans valeur de diagnostic : la définition et la
    /// position, qui restent, disent tout ce qu'un placement demande.
    /// </summary>
    [GeneratedRegex(@"\\\\[.?]\\DISPLAY\d+", RegexOptions.IgnoreCase, 500)]
    private static partial Regex DisplayPattern();

    /// <summary>
    /// Le nom du compte Windows dans un chemin. Il arrive sans que personne
    /// l'écrive, par les chemins sous le profil de l'utilisateur : cent douze
    /// lignes des journaux relevés.
    /// </summary>
    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\/:*?""<>|\r\n]+\\", RegexOptions.IgnoreCase, 500)]
    private static partial Regex UserPathPattern();
}
