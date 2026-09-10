using System.Globalization;

using DtHub.Core.Localization;

namespace DtHub.Core.Settings;

/// <summary>
/// Les règles des sessions nommées, sans état ni entrée-sortie.
///
/// Un profil est une liste de comptes, tenue à part de l'ensemble de démarrage.
/// La distinction est le fond de l'affaire : cet ensemble se déforme à chaque
/// geste, un lancement réussi y ajoutant les comptes ouverts et le bouton
/// « fermer » les en retirant. Un profil déduit à la volée de cet ensemble se
/// serait donc réécrit tout seul, et n'aurait rien retenu du tout.
/// </summary>
public static class LaunchProfiles
{
    /// <summary>
    /// Au-delà, le nom déborde de la liste déroulante. La coupe vaut mieux que
    /// le refus : personne ne compte les lettres en tapant.
    /// </summary>
    public const int MaxNameLength = 40;

    /// <summary>Nombre de comptes nommés dans un résumé avant de les compter.</summary>
    private const int NamedAtMost = 3;

    /// <summary>
    /// Le nom tel qu'il sera retenu, ou <c>null</c> s'il n'en est pas un.
    ///
    /// Les espaces de bord partent : « Duo » et « Duo  » sont le même profil, et
    /// laisser passer la différence en créerait deux qu'on ne saurait pas
    /// distinguer à l'écran.
    /// </summary>
    public static string? Normalize(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength].TrimEnd() : trimmed;
    }

    /// <summary>
    /// Ce que porte le bouton des profils.
    ///
    /// Le nom du profil retenu pour le démarrage quand il y en a un, le mot
    /// générique sinon. Rien ne distinguait « aucune session nommée » de
    /// « Duo haute s'appliquera au prochain démarrage », et il fallait ouvrir la
    /// bulle pour le savoir. Un profil décide pourtant des comptes qui
    /// s'ouvrent, de la qualité, du zoom et de l'ancrage : nous avons nous-mêmes
    /// cherché longtemps pourquoi la qualité repassait en maximale à chaque
    /// lancement, faute que rien ne le laisse voir.
    ///
    /// Le nom passe par <see cref="Normalize" />, qui écarte les espaces de bord
    /// et un nom vide : un bouton qui n'afficherait que du blanc serait pire que
    /// le mot générique.
    /// </summary>
    public static string ButtonLabel(string? defaultProfile) =>
        Normalize(defaultProfile) ?? Strings.Get("Profiles");

    /// <summary>
    /// Le profil de ce nom, ou <c>null</c>.
    ///
    /// La casse est ignorée : « Duo » et « duo » seraient deux entrées
    /// indiscernables dans la liste, et l'on ne saurait plus laquelle on ouvre.
    /// Ordinale, et non culturelle : un nom de profil est un identifiant, non du
    /// texte à trier, et le repli d'une culture à l'autre n'a rien à y faire.
    /// </summary>
    public static StoredLaunchProfile? Find(
        IEnumerable<StoredLaunchProfile>? profiles,
        string? name)
    {
        if (profiles is null || Normalize(name) is not { } wanted)
        {
            return null;
        }

        return profiles.FirstOrDefault(
            p => string.Equals(p.Name?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Les comptes de ce profil qui existent encore.
    ///
    /// Un profil peut nommer un compte supprimé du téléphone depuis. L'écarter
    /// plutôt que d'échouer est le seul comportement tenable : le profil garde
    /// sa raison d'être, et les comptes restants s'ouvrent.
    /// </summary>
    public static IReadOnlyList<string> KeysFor(
        IEnumerable<StoredLaunchProfile>? profiles,
        string? name,
        IEnumerable<StoredInstance>? instances)
    {
        if (Find(profiles, name) is not { } profile || profile.InstanceKeys is null)
        {
            return [];
        }

        var known = new HashSet<string>(
            instances?.Select(i => i.Key) ?? [], StringComparer.Ordinal);

        return [.. profile.InstanceKeys.Where(known.Contains).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Ce que le profil contient, en une ligne montrable.
    ///
    /// Les noms tant qu'ils tiennent, un compte au-delà : « XSpace + Principal »
    /// se lit, « A + B + C + D + E » ne se lit plus.
    /// </summary>
    public static string Describe(
        StoredLaunchProfile? profile,
        IEnumerable<StoredInstance>? instances)
    {
        if (profile?.InstanceKeys is not { Count: > 0 })
        {
            return Strings.Get("NoAccount");
        }

        List<StoredInstance> all = [.. instances ?? []];

        List<string> names = [.. profile.InstanceKeys
            .Select(key => all.Find(i => string.Equals(i.Key, key, StringComparison.Ordinal)))
            .Where(i => i is not null)
            .Select(i => NameOf(i!))];

        if (names.Count == 0)
        {
            return Strings.Get("NoKnownAccount");
        }

        var accountsText = names.Count <= NamedAtMost
            ? string.Join(" + ", names)
            : Strings.Format("AccountCount", names.Count);

        // La qualité n'est dite que si elle sort de l'ordinaire : la rappeler à
        // chaque profil noierait le nom des comptes, qui est ce qu'on cherche.
        return profile.Quality == StreamQuality.Medium
            ? accountsText
            : $"{accountsText}, {QualityLabel(profile.Quality)}";
    }

    /// <summary>
    /// Ce qu'un profil s'apprête à retenir, dit avant de le créer.
    ///
    /// « Enregistrer » ne laissait rien deviner de ce qui part avec le nom :
    /// on croyait ne retenir que des comptes, et l'ouverture du profil
    /// replaçait les fenêtres et changeait la qualité. La phrase le dit à
    /// l'avance, avec les valeurs du moment plutôt qu'une liste figée, pour
    /// qu'on reconnaisse son propre réglage.
    /// </summary>
    public static string Announce(
        int accounts,
        StreamQuality quality,
        GameZoom zoom,
        int tabbed,
        bool audio)
    {
        // L'article vit dans le gabarit de chaque langue et non dans
        // l'étiquette : « la qualité haute » en français, « high quality » sans
        // article en anglais. Coller l'article au mot rendait la phrase
        // intraduisible, le genre n'étant pas le même d'une langue à l'autre.
        var accountsText = accounts <= 1
            ? Strings.Get("ProfileAccountsOne")
            : Strings.Format("ProfileAccountsMany", accounts);

        var phrase = Strings.Format(
            "ProfileAnnounce",
            accountsText,
            QualityLabel(quality),
            ZoomLabel(zoom),
            Strings.Get(audio ? "SoundToPc" : "SoundOff"));

        if (tabbed <= 0)
        {
            return phrase;
        }

        var onglets = tabbed <= 1
            ? Strings.Get("ProfileTabbedOne")
            : Strings.Format("ProfileTabbedMany", tabbed);

        return $"{phrase} {onglets}";
    }

    /// <summary>Le mot de la distance, tel qu'il paraît dans le panneau.</summary>
    private static string ZoomLabel(GameZoom zoom) => zoom switch
    {
        GameZoom.Widest => Strings.Get("ZoomVeryFarWord"),
        GameZoom.Wide => Strings.Get("ZoomFarWord"),
        GameZoom.Close => Strings.Get("ZoomCloseWord"),
        _ => Strings.Get("ZoomNormalWord"),
    };

    /// <summary>Le mot du palier, tel qu'il paraît dans le panneau.</summary>
    private static string QualityLabel(StreamQuality quality) => quality switch
    {
        StreamQuality.Low => Strings.Get("QualityLowWord"),
        StreamQuality.Maximum => Strings.Get("QualityHighWord"),
        StreamQuality.Custom => Strings.Get("QualityCustomWord"),
        _ => Strings.Get("QualityMediumWord"),
    };

    /// <summary>Le nom choisi par l'utilisateur, à défaut celui du profil Android.</summary>
    private static string NameOf(StoredInstance instance) =>
        string.IsNullOrWhiteSpace(instance.CustomName)
            ? instance.UserName
            : instance.CustomName.Trim();
}
