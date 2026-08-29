using System.Globalization;

namespace DtHub.Core.Scrcpy;

/// <summary>
/// Construit la ligne de commande scrcpy. Fonction pure, entièrement
/// vérifiable : c'est la pièce la plus facile à casser sans s'en apercevoir,
/// puisqu'une option mal nommée ne se voit qu'à l'exécution.
/// </summary>
public static class ScrcpyCommandBuilder
{
    /// <summary>Préfixe des titres de fenêtre, qui sert aussi à les retrouver.</summary>
    public const string WindowTitlePrefix = "DtHub";

    /// <summary>
    /// Titre unique d'une session. Il porte l'identifiant de session, ce qui
    /// permet de retrouver la fenêtre sans se tromper de voisine.
    /// </summary>
    public static string BuildWindowTitle(string sessionId, string? friendlyName = null) =>
        string.IsNullOrWhiteSpace(friendlyName)
            ? $"{WindowTitlePrefix} [{sessionId}]"
            : $"{friendlyName} - {WindowTitlePrefix} [{sessionId}]";

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

        List<string> arguments =
        [
            "--serial", serial,
            "--window-title", windowTitle,
            "--max-fps", sanitized.MaxFps.ToString(CultureInfo.InvariantCulture),
            "--video-bit-rate", sanitized.VideoBitrateArgument,
            "--keyboard", sanitized.KeyboardMode == ScrcpyKeyboardMode.Uhid ? "uhid" : "sdk",
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
            arguments.Add("--video-codec");
            arguments.Add(sanitized.VideoCodec);
        }

        if (sanitized.UseVirtualDisplay)
        {
            arguments.Add("--new-display");
            arguments.Add(sanitized.VirtualDisplayArgument);

            if (sanitized.DisableVirtualDisplayDecorations)
            {
                arguments.Add("--no-vd-system-decorations");
            }
        }

        if (windowPosition is { } placement)
        {
            arguments.AddRange(
            [
                "--window-x", placement.X.ToString(CultureInfo.InvariantCulture),
                "--window-y", placement.Y.ToString(CultureInfo.InvariantCulture),
                "--window-width", placement.Width.ToString(CultureInfo.InvariantCulture),
                "--window-height", placement.Height.ToString(CultureInfo.InvariantCulture),
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

        return ["--serial", serial, "--list-apps"];
    }
}

/// <summary>Position et taille d'une fenêtre, en pixels écran.</summary>
public readonly record struct ScrcpyWindowPlacement(int X, int Y, int Width, int Height);
