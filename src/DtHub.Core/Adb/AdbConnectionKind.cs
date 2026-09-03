namespace DtHub.Core.Adb;

/// <summary>Manière dont l'appareil est rattaché à l'hôte.</summary>
public enum AdbConnectionKind
{
    Unknown = 0,

    /// <summary>Câble USB.</summary>
    Usb,

    /// <summary>Débogage sans fil, le numéro de série est une adresse et un port.</summary>
    Wireless,

    /// <summary>Émulateur local, hors périmètre fonctionnel mais détecté proprement.</summary>
    Emulator,
}
