using System.Globalization;

namespace DtHub.Core.Scrcpy;

/// <summary>
/// Construit la ligne de commande scrcpy. Fonction pure, entièrement
/// vérifiable : c'est la pièce la plus facile à casser sans s'en apercevoir,
/// puisqu'une option mal nommée ne se voit qu'à l'exécution.
/// </summary>
public static class ScrcpyCommandBuilder
{
    /// <summary>
    /// Titre de la fenêtre de jeu : le nom du produit suivi du nom choisi par
    /// l'utilisateur. Aucun identifiant technique n'y figure, la fenêtre étant
    /// retrouvée par son processus.
    /// </summary>
    public static string BuildWindowTitle(string? name, string? hint = null)
    {
        var title = string.IsNullOrWhiteSpace(name)
            ? DtHub.Core.ProductInfo.Name
            : $"{DtHub.Core.ProductInfo.Name} {name.Trim()}";

        return string.IsNullOrWhiteSpace(hint) ? title : $"{title}  ({hint.Trim()})";
    }

    /// <summary>Arguments de lancement d'une session de mirroring.</summary>
    /// <param name="serial">Numéro de série ADB de l'appareil visé.</param>
    /// <param name="windowTitle">Titre unique, utilisé ensuite pour retrouver la fenêtre.</param>
    /// <param name="options">Réglages de la session.</param>
    /// <param name="windowPosition">Position et taille initiales, si elles sont connues.</param>
    public static IReadOnlyList<string> BuildMirrorArguments(
        string serial,
        string windowTitle,
        ScrcpyOptions options,
        ScrcpyWindowPlacement? windowPosition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(options);

        var sanitized = options.Sanitized();

        // Toutes les valeurs sont accolées par « = ». Trois options de scrcpy
        // acceptent une valeur facultative, dont --new-display : pour
        // celles-là, getopt n'accepte la valeur qu'accolée, et une valeur
        // séparée par une espace est prise pour un argument parasite. La forme
        // accolée fonctionne dans les deux cas, on l'emploie partout.
        List<string> arguments =
        [
            Option("serial", serial),
            Option("window-title", windowTitle),
            Option("max-fps", sanitized.MaxFps.ToString(CultureInfo.InvariantCulture)),
            Option("video-bit-rate", sanitized.VideoBitrateArgument),
            Option("keyboard", sanitized.KeyboardMode == ScrcpyKeyboardMode.Uhid ? "uhid" : "sdk"),

            // Les clics secondaires ne font rien par défaut. scrcpy associe
            // sinon le clic droit à RETOUR : sur un afficheur qui ne porte que
            // le jeu, ce retour quitte l'activité et laisse un écran noir. Les
            // quatre actions restent accessibles en maintenant Maj.
            Option("mouse-bind", "----:bhsn"),
        ];

        if (sanitized.PreferText)
        {
            arguments.Add("--prefer-text");
        }

        if (!sanitized.AudioEnabled)
        {
            arguments.Add("--no-audio");
        }

        if (!sanitized.ClipboardSyncEnabled)
        {
            arguments.Add("--no-clipboard-autosync");
        }

        if (sanitized.KeepDeviceAwake)
        {
            arguments.Add("--keep-active");
        }

        if (!string.IsNullOrWhiteSpace(sanitized.VideoCodec))
        {
            arguments.Add(Option("video-codec", sanitized.VideoCodec));
        }

        var flex = sanitized is { UseVirtualDisplay: true, FlexDisplay: true };

        if (sanitized.UseVirtualDisplay)
        {
            // Avec l'ajustement continu, la taille de la fenêtre se règle par
            // la définition de l'afficheur : scrcpy refuse --window-width et
            // --window-height dans ce mode.
            //
            // La largeur vient de la fenêtre, la hauteur des réglages : c'est
            // elle qui fixe la hauteur que le jeu acceptera de dessiner, et
            // elle est calée sur l'écran au lancement. Rien ne se décide ici.
            arguments.Add(Option(
                "new-display",
                flex && windowPosition is { Width: > 0 } size
                    ? DisplayArgument(
                          size.Width, sanitized.VirtualDisplayHeight, sanitized.VirtualDisplayDpi)
                    : sanitized.VirtualDisplayArgument));

            if (sanitized.DisableVirtualDisplayDecorations)
            {
                arguments.Add("--no-vd-system-decorations");
            }

            if (flex)
            {
                arguments.Add("--flex-display");
            }
        }

        if (windowPosition is { } placement)
        {
            arguments.AddRange(
            [
                Option("window-x", placement.X.ToString(CultureInfo.InvariantCulture)),
                Option("window-y", placement.Y.ToString(CultureInfo.InvariantCulture)),
            ]);

            if (!flex)
            {
                arguments.AddRange(
                [
                    Option("window-width", placement.Width.ToString(CultureInfo.InvariantCulture)),
                    Option("window-height", placement.Height.ToString(CultureInfo.InvariantCulture)),
                ]);
            }
        }

        // Volontairement absent : --kill-adb-on-close. Le serveur ADB est
        // partagé avec le reste de la machine et ne doit pas tomber avec une
        // session de DT Hub.
        return arguments;
    }

    /// <summary>Arguments pour récupérer la liste nommée des applications.</summary>
    public static IReadOnlyList<string> BuildListAppsArguments(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        return [Option("serial", serial), "--list-apps"];
    }

    /// <summary>Une option longue et sa valeur, accolées.</summary>
    private static string Option(string name, string value) => $"--{name}={value}";

    /// <summary>
    /// Définition d'afficheur au format attendu. Les dimensions sont ramenées
    /// à des nombres pairs : les encodeurs vidéo refusent les côtés impairs.
    /// </summary>
    private static string DisplayArgument(int width, int height, int dpi) => string.Create(
        CultureInfo.InvariantCulture,
        $"{Math.Max(2, width - (width % 2))}x{Math.Max(2, height - (height % 2))}/{dpi}");
}

/// <summary>Position et taille d'une fenêtre, en pixels écran.</summary>
public readonly record struct ScrcpyWindowPlacement(int X, int Y, int Width, int Height);
