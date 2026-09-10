using System.Runtime.InteropServices;

using DtHub.Core.Devices;

namespace DtHub.Infrastructure.Devices;

/// <summary>
/// Demande à Windows ce qu'il reproche à ses périphériques USB.
///
/// C'est l'étage en dessous d'ADB, et il n'était pas regardé. Un câble qui ne
/// transmet pas les données ne produit aucune ligne dans <c>adb devices</c> :
/// l'application n'avait alors rien à dire, alors que Windows savait très bien
/// qu'un appareil venait d'arriver et qu'il n'avait pas su lire son
/// descripteur.
///
/// Par SetupAPI plutôt que par WMI : pas de paquet supplémentaire, pas de coût
/// de démarrage, et c'est le même genre d'appel que les quarante autres du
/// dépôt. Aucune élévation n'est nécessaire, et c'est vérifié : le relevé rend
/// le même code de problème depuis un compte ordinaire.
///
/// La lecture est toujours sans conséquence. Elle n'ouvre rien, ne change rien,
/// et un échec rend une liste vide : ne pas savoir est un état ordinaire, et
/// l'application marche très bien sans cette information.
/// </summary>
public sealed class WindowsUsbInspector : IUsbEnumerationInspector
{
    public IReadOnlyList<UsbFault> Faults()
    {
        var set = SetupDiGetClassDevs(0, "USB", 0, DigcfPresent | DigcfAllClasses);

        if (set == InvalidHandle)
        {
            return [];
        }

        try
        {
            List<UsbFault> faults = [];

            var info = default(DeviceInfoData);
            info.Size = Marshal.SizeOf<DeviceInfoData>();

            for (var index = 0u; SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                if (CmGetDevNodeStatus(out _, out var problem, info.DevInst, 0) != Success
                    || problem == 0)
                {
                    continue;
                }

                faults.Add(new UsbFault(UsbFault.KindOf((int)problem), (int)problem, InstanceId(set, ref info)));
            }

            return faults;
        }
        catch (DllNotFoundException)
        {
            // Une bibliothèque du système absente : rien à diagnostiquer, et
            // surtout rien qui justifie d'empêcher l'application de tourner.
            return [];
        }
        catch (EntryPointNotFoundException)
        {
            return [];
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>
    /// L'identifiant du périphérique, pour le journal. Il ne nomme personne :
    /// sur un descripteur illisible il vaut d'ailleurs
    /// <c>USB\VID_0000&amp;PID_0002</c>, Windows n'ayant rien pu lire.
    /// </summary>
    private static string InstanceId(nint set, ref DeviceInfoData info)
    {
        var buffer = new char[256];

        return SetupDiGetDeviceInstanceId(set, ref info, buffer, buffer.Length, out var used)
            && used > 1
                ? new string(buffer, 0, used - 1)
                : string.Empty;
    }

    private const int DigcfPresent = 0x00000002;
    private const int DigcfAllClasses = 0x00000004;
    private const int Success = 0;
    private static readonly nint InvalidHandle = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        public int Size;
        public Guid ClassGuid;
        public uint DevInst;
        public nint Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW")]
    private static extern nint SetupDiGetClassDevs(nint classGuid, string? enumerator, nint parent, int flags);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref DeviceInfoData info);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInstanceIdW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(
        nint set, ref DeviceInfoData info, [Out] char[] id, int size, out int used);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint set);

    /// <summary>
    /// Le code de problème d'un nœud de périphérique. Quarante-trois vaut
    /// « descripteur illisible », vingt-huit « aucun pilote ».
    /// </summary>
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Status")]
    private static extern int CmGetDevNodeStatus(
        out uint status, out uint problem, uint devInst, int flags);
}
