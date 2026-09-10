using DtHub.Core.Adb;
using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>Ce que l'application peut dire de la liaison avec le téléphone.</summary>
public enum ConnectionVerdict
{
    /// <summary>Un téléphone au moins répond : il n'y a rien à expliquer.</summary>
    Ready = 0,

    /// <summary>Les outils Android n'ont pas pu démarrer.</summary>
    ToolsMissing,

    /// <summary>Un téléphone est là, mais il n'a pas encore autorisé ce PC.</summary>
    WaitingAuthorization,

    /// <summary>Le pilote USB refuse l'accès à l'appareil.</summary>
    DriverRefused,

    /// <summary>Un appareil est branché mais Windows n'a pas pu lire son identité.</summary>
    UsbUnreadable,

    /// <summary>Un appareil est branché mais aucun pilote ne lui répond.</summary>
    UsbDriverMissing,

    /// <summary>Un appareil est branché et Windows l'a refusé pour une autre raison.</summary>
    UsbOther,

    /// <summary>Rien de branché, rien de joignable.</summary>
    NoDevice,
}

/// <summary>
/// Dit d'un mot où en est la liaison, et pourquoi elle n'aboutit pas.
///
/// Fonction pure, comme <see cref="AdbErrorInterpreter"/> dont elle prolonge le
/// travail d'un étage vers le bas. Toute la richesse d'ADB s'arrête là où il ne
/// voit plus rien : un câble qui ne transmet pas les données ne produit aucune
/// ligne dans <c>adb devices</c>, et l'application n'avait alors rien à dire
/// alors que Windows, lui, savait tout.
///
/// L'ordre des règles est celui dans lequel les causes se succèdent, de la plus
/// proche du succès à la plus lointaine : un téléphone qui répond l'emporte sur
/// un autre qui attend une autorisation, et un appareil qu'ADB voit l'emporte
/// sur un défaut d'énumération, qui ne peut alors concerner qu'un autre port.
/// </summary>
public static class ConnectionCheck
{
    /// <summary>
    /// Le verdict, à partir de ce que rend <c>adb devices</c> et de ce que
    /// Windows sait de ses ports USB.
    /// </summary>
    /// <param name="states">L'état de chaque appareil vu, dans n'importe quel ordre.</param>
    /// <param name="faults">Les périphériques USB que Windows n'a pas su démarrer.</param>
    /// <param name="toolsReady">Faux si l'exécutable ADB est absent ou muet.</param>
    public static ConnectionVerdict Of(
        IReadOnlyList<AdbDeviceState> states,
        IReadOnlyList<UsbFault> faults,
        bool toolsReady)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(faults);

        if (!toolsReady)
        {
            return ConnectionVerdict.ToolsMissing;
        }

        if (states.Contains(AdbDeviceState.Device))
        {
            return ConnectionVerdict.Ready;
        }

        if (states.Any(s => s is AdbDeviceState.Unauthorized or AdbDeviceState.Authorizing))
        {
            return ConnectionVerdict.WaitingAuthorization;
        }

        if (states.Contains(AdbDeviceState.NoPermissions))
        {
            return ConnectionVerdict.DriverRefused;
        }

        // ADB ne voit rien : c'est seulement ici que l'avis de Windows sert.
        // Le plus explicite des défauts l'emporte, un poste ordinaire ayant
        // souvent un périphérique en défaut qui n'a rien à voir avec nous.
        return Worst(faults) switch
        {
            UsbFaultKind.Unreadable => ConnectionVerdict.UsbUnreadable,
            UsbFaultKind.DriverMissing => ConnectionVerdict.UsbDriverMissing,
            UsbFaultKind.Other => ConnectionVerdict.UsbOther,
            _ => ConnectionVerdict.NoDevice,
        };
    }

    /// <summary>
    /// Le défaut le plus parlant du lot. Un descripteur illisible passe avant
    /// un pilote manquant, qui passe avant un code qu'on ne sait que citer.
    /// </summary>
    private static UsbFaultKind Worst(IReadOnlyList<UsbFault> faults)
    {
        if (faults.Any(f => f.Kind == UsbFaultKind.Unreadable))
        {
            return UsbFaultKind.Unreadable;
        }

        if (faults.Any(f => f.Kind == UsbFaultKind.DriverMissing))
        {
            return UsbFaultKind.DriverMissing;
        }

        return faults.Any(f => f.Kind == UsbFaultKind.Other)
            ? UsbFaultKind.Other
            : UsbFaultKind.None;
    }

    /// <summary>Une phrase pour l'écran, jamais un code ni une sortie brute.</summary>
    public static string Describe(ConnectionVerdict verdict) => Strings.Get(verdict switch
    {
        ConnectionVerdict.Ready => "CheckReady",
        ConnectionVerdict.ToolsMissing => "AdbUnavailable",
        ConnectionVerdict.WaitingAuthorization => "CheckWaitingAuthorization",
        ConnectionVerdict.DriverRefused => "CheckDriverRefused",
        ConnectionVerdict.UsbUnreadable => "CheckUsbUnreadable",
        ConnectionVerdict.UsbDriverMissing => "CheckUsbDriverMissing",
        ConnectionVerdict.UsbOther => "CheckUsbOther",
        _ => "CheckNoDevice",
    });

    /// <summary>
    /// Vrai si le verdict mérite d'être affiché.
    ///
    /// Deux verdicts n'ont rien à dire. Le premier est évident : un téléphone
    /// qui répond n'appelle aucun commentaire.
    ///
    /// Le second l'est moins. N'avoir aucun appareil n'est pas une panne :
    /// c'est l'état de repos de l'application, celui qu'on trouve en l'ouvrant
    /// sans avoir rien branché. Le dire dans un bloc d'alerte revient à
    /// signaler un problème là où il n'y a qu'une absence, et la liste des
    /// comptes le dit déjà juste en dessous, plus brièvement et au bon endroit.
    /// Deux blocs se répondaient donc, l'un long, l'autre court, pour le même
    /// néant.
    ///
    /// Restent les cas où quelque chose est là et ne marche pas : un appareil
    /// vu mais pas encore autorisé, un pilote qui refuse, un descripteur
    /// illisible, les outils absents. Ceux-là, personne ne peut les deviner, et
    /// c'est pour eux que le bloc existe.
    /// </summary>
    public static bool NeedsExplaining(ConnectionVerdict verdict) =>
        verdict is not (ConnectionVerdict.Ready or ConnectionVerdict.NoDevice);

    /// <summary>
    /// Vrai si la fiche de dépannage du câble a quelque chose à apporter.
    /// Elle ne s'affiche pas quand le téléphone répond, ni quand la balle est
    /// dans le camp du téléphone.
    /// </summary>
    public static bool NeedsCableHelp(ConnectionVerdict verdict) =>
        verdict is ConnectionVerdict.UsbUnreadable
            or ConnectionVerdict.UsbDriverMissing
            or ConnectionVerdict.UsbOther
            or ConnectionVerdict.NoDevice;
}
