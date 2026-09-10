namespace DtHub.Core.Devices;

/// <summary>
/// Ce que Windows reproche à un périphérique USB, ramené aux familles qu'un
/// utilisateur peut comprendre et corriger.
///
/// C'est l'étage en dessous d'ADB. Quand un téléphone n'énumère pas, ADB n'a
/// rien à dire : pour lui il n'y a simplement aucun appareil. Windows, lui,
/// sait très bien ce qui s'est passé, et le dit par un code de problème.
/// </summary>
public enum UsbFaultKind
{
    /// <summary>Rien à signaler.</summary>
    None = 0,

    /// <summary>
    /// Code 43 : le descripteur du périphérique n'a pas pu être lu, donc
    /// Windows ne sait même pas de quel appareil il s'agit. Le câble, le port
    /// ou le connecteur, jamais le téléphone lui-même.
    /// </summary>
    Unreadable,

    /// <summary>
    /// Code 28 : l'appareil est identifié mais aucun pilote ne lui répond.
    /// C'est le cas de l'interface ADB sur un poste où elle n'a jamais servi.
    /// </summary>
    DriverMissing,

    /// <summary>Un autre défaut, nommé par son code faute de mieux.</summary>
    Other,
}

/// <summary>
/// Un périphérique USB en défaut, tel que Windows le rapporte.
/// </summary>
/// <param name="Kind">La famille du défaut.</param>
/// <param name="ProblemCode">Le code de problème brut, pour le journal.</param>
/// <param name="DeviceId">
/// L'identifiant du périphérique. Il ne nomme personne : sur un descripteur
/// illisible, il vaut d'ailleurs <c>USB\VID_0000&amp;PID_0002</c>.
/// </param>
public sealed record UsbFault(UsbFaultKind Kind, int ProblemCode, string DeviceId)
{
    /// <summary>Codes de problème que nous savons expliquer.</summary>
    public const int FailedPostStart = 43;
    public const int DriverNotInstalled = 28;

    /// <summary>Range un code de problème dans sa famille.</summary>
    public static UsbFaultKind KindOf(int problemCode) => problemCode switch
    {
        0 => UsbFaultKind.None,
        FailedPostStart => UsbFaultKind.Unreadable,
        DriverNotInstalled => UsbFaultKind.DriverMissing,
        _ => UsbFaultKind.Other,
    };
}

/// <summary>
/// Lit l'état des périphériques USB auprès de Windows.
///
/// Derrière une interface parce que c'est du Win32 : le noyau doit pouvoir
/// raisonner sur un défaut sans qu'il y ait de vrai port USB en face.
/// </summary>
public interface IUsbEnumerationInspector
{
    /// <summary>
    /// Les périphériques USB que Windows n'a pas su démarrer, s'il y en a.
    /// Rend une liste vide plutôt qu'une erreur : ne pas savoir est un état
    /// ordinaire, et l'application marche très bien sans cette information.
    /// </summary>
    IReadOnlyList<UsbFault> Faults();
}
