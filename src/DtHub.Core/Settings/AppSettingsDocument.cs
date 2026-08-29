using DtHub.Core.Hotkeys;
using DtHub.Core.Windows;

namespace DtHub.Core.Settings;

/// <summary>
/// Forme persistée de <c>settings.json</c>. Les valeurs par défaut sont
/// viables : un fichier absent, partiel ou modifié à la main doit donner une
/// configuration utilisable.
/// </summary>
public sealed class AppSettingsDocument
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// Vrai une fois la mise en route faite. C'est ce qui distingue le premier
    /// lancement, qui pose la question, des suivants, qui lancent directement.
    /// </summary>
    public bool SetupCompleted { get; set; }

    /// <summary>Instances mémorisées, y compris celles qui ne sont pas cochées.</summary>
    public List<StoredInstance> Instances { get; set; } = [];

    // Fenêtres

    /// <summary>Position du bloc de fenêtres de jeu dans l'écran.</summary>
    public WindowAnchor GameAnchor { get; set; } = WindowAnchor.MiddleLeft;

    /// <summary>
    /// Tailles proposées, en pourcentage de la zone utilisable de l'écran.
    /// Elles sont donc proportionnelles à l'écran employé.
    /// </summary>
    public List<int> SizePercentages { get; set; } = [55, 70, 85, 100];

    /// <summary>Taille retenue, par son indice. La dernière est le plein écran.</summary>
    public int SizeIndex { get; set; } = 1;

    /// <summary>Écran Windows utilisé, <c>null</c> pour l'écran principal.</summary>
    public string? PreferredMonitorDeviceName { get; set; }

    // Mirroring

    public int MaxFps { get; set; } = 45;
    public int VideoBitrateKbps { get; set; } = 4000;
    public bool AudioEnabled { get; set; }
    public bool ClipboardSyncEnabled { get; set; } = true;
    // Le jeu s'affiche en paysage : un écran virtuel vertical le centrerait
    // en 16:9 au milieu d'une fenêtre haute, avec deux larges bandes noires.
    public int VirtualDisplayWidth { get; set; } = 1920;
    public int VirtualDisplayHeight { get; set; } = 1080;
    public int VirtualDisplayDpi { get; set; } = 240;

    /// <summary>Paquet du jeu. Réglable pour survivre à un changement amont.</summary>
    public string PackageName { get; set; } = Dofus.DofusPackages.DofusTouch;

    // Raccourcis
    public List<StoredHotkey> Hotkeys { get; set; } = [];
}

/// <summary>Une instance mémorisée entre deux lancements.</summary>
public sealed class StoredInstance
{
    public string DeviceId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string PackageName { get; set; } = string.Empty;

    /// <summary>Nom du profil Android au moment de la découverte, pour l'affichage hors ligne.</summary>
    public string UserName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Nom choisi par l'utilisateur.</summary>
    public string? CustomName { get; set; }

    public string? LaunchComponent { get; set; }

    /// <summary>Vrai si l'instance fait partie du lancement automatique.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Rang d'affichage et d'ouverture.</summary>
    public int Order { get; set; }

    public string Key => $"{DeviceId}|{UserId}|{PackageName}";
}

/// <summary>Forme persistée d'un raccourci.</summary>
public sealed class StoredHotkey
{
    public string Action { get; set; } = string.Empty;
    public int VirtualKey { get; set; }
    public HotkeyModifiers Modifiers { get; set; }

    public static StoredHotkey From(HotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return new StoredHotkey
        {
            Action = binding.Action.ToString(),
            VirtualKey = binding.VirtualKey,
            Modifiers = binding.Modifiers,
        };
    }

    /// <summary>
    /// Reconstruit un raccourci, ou rend <c>null</c> si l'action n'existe
    /// plus. Un fichier écrit par une autre version ne doit pas faire échouer
    /// la lecture.
    /// </summary>
    public HotkeyBinding? ToBinding() =>
        Enum.TryParse<HotkeyAction>(Action, ignoreCase: true, out var action)
            ? new HotkeyBinding { Action = action, VirtualKey = VirtualKey, Modifiers = Modifiers }
            : null;
}
