using DtHub.Core.Hotkeys;

namespace DtHub.Core.Settings;

/// <summary>Politique appliquée aux sessions encore ouvertes à la fermeture.</summary>
public enum ExitPolicy
{
    /// <summary>Demander à l'utilisateur.</summary>
    Ask,

    /// <summary>Fermer les sessions avec l'application.</summary>
    CloseSessions,

    /// <summary>Laisser les sessions ouvertes.</summary>
    KeepSessions,
}

/// <summary>Thème de l'interface.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Forme persistée de <c>settings.json</c>. Les propriétés sont mutables et
/// portent des valeurs par défaut viables : un fichier absent, partiel ou
/// modifié à la main doit donner une configuration utilisable.
/// </summary>
public sealed class AppSettingsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // Général
    public string? DefaultProfileId { get; set; }
    public string? DefaultDeviceId { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;
    public ExitPolicy ExitPolicy { get; set; } = ExitPolicy.Ask;

    /// <summary>Reconnecter les appareils mémorisés au démarrage.</summary>
    public bool ReconnectOnStartup { get; set; } = true;

    // Fenêtres
    public int DefaultSizeIndex { get; set; } = 2;
    public List<int> SizePercentages { get; set; } = [60, 70, 80, 90];
    public string? PreferredMonitorDeviceName { get; set; }

    // scrcpy
    public int MaxFps { get; set; } = 45;
    public int VideoBitrateKbps { get; set; } = 4000;
    public bool AudioEnabled { get; set; }
    public bool ClipboardSyncEnabled { get; set; } = true;
    public int VirtualDisplayWidth { get; set; } = 1080;
    public int VirtualDisplayHeight { get; set; } = 1920;
    public int VirtualDisplayDpi { get; set; } = 320;
    public ScrcpyKeyboardModeSetting KeyboardMode { get; set; } = ScrcpyKeyboardModeSetting.Sdk;

    // Applications
    public bool ShowSystemApps { get; set; }

    /// <summary>Clés de favoris, au format <c>appareil|utilisateur|paquet</c>.</summary>
    public List<string> FavoriteApps { get; set; } = [];

    // Appareils
    /// <summary>Chemin ADB imposé par l'utilisateur, sinon celui de DT Hub.</summary>
    public string? CustomAdbPath { get; set; }

    // Mises à jour
    public bool CheckUpdatesAutomatically { get; set; } = true;

    // Raccourcis
    public List<StoredHotkey> Hotkeys { get; set; } = [];
}

/// <summary>Mode clavier, dupliqué ici pour ne pas persister un type du moteur scrcpy.</summary>
public enum ScrcpyKeyboardModeSetting
{
    Sdk,
    Uhid,
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
    /// plus. Un fichier écrit par une version ultérieure ne doit pas faire
    /// échouer la lecture.
    /// </summary>
    public HotkeyBinding? ToBinding() =>
        Enum.TryParse<HotkeyAction>(Action, ignoreCase: true, out var action)
            ? new HotkeyBinding { Action = action, VirtualKey = VirtualKey, Modifiers = Modifiers }
            : null;
}
