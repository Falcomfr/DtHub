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
    public static string BuildWindowTitle(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? DtHub.Core.ProductInfo.Name
            : $"{DtHub.Core.ProductInfo.Name} {name.Trim()}";

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

        if (sanitized.UseVirtualDisplay)
        {
            arguments.Add(Option("new-display", sanitized.VirtualDisplayArgument));

            if (sanitized.DisableVirtualDisplayDecorations)
            {
                arguments.Add("--no-vd-system-decorations");
            }
        }

        if (windowPosition is { } placement)
        {
            arguments.AddRange(
            [
                Option("window-x", placement.X.ToString(CultureInfo.InvariantCulture)),
                Option("window-y", placement.Y.ToString(CultureInfo.InvariantCulture)),
                Option("window-width", placement.Width.ToString(CultureInfo.InvariantCulture)),
                Option("window-height", placement.Height.ToString(CultureInfo.InvariantCulture)),
            ]);
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
}

/// <summary>Position et taille d'une fenêtre, en pixels écran.</summary>
public readonly record struct ScrcpyWindowPlacement(int X, int Y, int Width, int Height);
