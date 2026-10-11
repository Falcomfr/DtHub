using System.Runtime.InteropServices;

using DtHub.Core.Devices;

namespace DtHub.Infrastructure.Devices;

/// <summary>
/// Asks Windows what it holds against its USB devices.
///
/// This is the layer below ADB, and it was not being looked at. A
/// cable that does not carry data produces no line at all in
/// <c>adb devices</c>: the application then had nothing to say, even
/// though Windows knew perfectly well that a device had just arrived
/// and that it had failed to read its descriptor.
///
/// Through SetupAPI rather than WMI: no extra package, no startup
/// cost, and it is the same kind of call as the other forty in the
/// repository. No elevation is required, and this has been
/// verified: the reading returns the same problem code from an
/// ordinary account.
///
/// The reading is always without consequence. It opens nothing,
/// changes nothing, and a failure returns an empty list: not knowing
/// is a normal state, and the application works perfectly well
/// without this information.
/// </summary>
public sealed class WindowsUsbInspector : IUsbEnumerationInspector
{
    public IReadOnlyList<UsbFault> Faults() =>
        [.. Enumerate().Where(d => d.Problem != 0)
            .Select(d => new UsbFault(UsbFault.KindOf((int)d.Problem), (int)d.Problem, d.Device.Id))];

    public IReadOnlyList<UsbPresence> Present() => [.. Enumerate().Select(d => d.Device)];

    /// <summary>
    /// The Android USB interface class adb enumerates, ANDROID_USB_CLASS_ID
    /// in its sources: any driver it can use declares it, Google's and the
    /// makers' alike.
    /// </summary>
    private static readonly Guid AndroidUsbInterface = new("F72FE0D4-CBCB-407D-8814-9ED673D0DD6B");

    public int AdbInterfaces()
    {
        var guid = AndroidUsbInterface;
        var set = SetupDiGetClassDevsByGuid(ref guid, null, 0, DigcfPresent | DigcfDeviceInterface);

        if (set == InvalidHandle)
        {
            return 0;
        }

        try
        {
            var data = default(DeviceInterfaceData);
            data.Size = Marshal.SizeOf<DeviceInterfaceData>();

            var count = 0;

            while (SetupDiEnumDeviceInterfaces(set, 0, ref guid, (uint)count, ref data))
            {
                count++;
            }

            return count;
        }
        catch (DllNotFoundException)
        {
            // As for the faults: not knowing, the count says none.
            return 0;
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>
    /// Every present USB device, with its problem code, zero when it runs.
    /// </summary>
    private static List<(uint Problem, UsbPresence Device)> Enumerate()
    {
        var set = SetupDiGetClassDevs(0, "USB", 0, DigcfPresent | DigcfAllClasses);

        if (set == InvalidHandle)
        {
            return [];
        }

        try
        {
            List<(uint Problem, UsbPresence Device)> devices = [];

            var info = default(DeviceInfoData);
            info.Size = Marshal.SizeOf<DeviceInfoData>();

            for (var index = 0u; SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                if (CmGetDevNodeStatus(out _, out var problem, info.DevInst, 0) != Success)
                {
                    continue;
                }

                devices.Add((problem, new UsbPresence(
                    InstanceId(set, ref info),
                    Property(set, ref info, SpdrpCompatibleIds),
                    Property(set, ref info, SpdrpService) is [var service, ..] ? service : null)));
            }

            return devices;
        }
        catch (DllNotFoundException)
        {
            // A missing system library: nothing to diagnose, and
            // above all nothing that justifies preventing the
            // application from running.
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
    /// The device's identifier, for the log. It names no one: on an
    /// unreadable descriptor it is in fact worth
    /// <c>USB\VID_0000&amp;PID_0002</c>, since Windows was unable to
    /// read anything.
    /// </summary>
    private static string InstanceId(nint set, ref DeviceInfoData info)
    {
        var buffer = new char[256];

        return SetupDiGetDeviceInstanceId(set, ref info, buffer, buffer.Length, out var used)
            && used > 1
                ? new string(buffer, 0, used - 1)
                : string.Empty;
    }

    /// <summary>
    /// A text property of the device, split when it is a list. Empty when
    /// it has none: a device without a driver has no service.
    /// </summary>
    private static string[] Property(nint set, ref DeviceInfoData info, int property)
    {
        var buffer = new byte[2048];

        return SetupDiGetDeviceRegistryProperty(set, ref info, property, out _, buffer, buffer.Length, out var used)
            && used > 2
                ? System.Text.Encoding.Unicode.GetString(buffer, 0, used).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                : [];
    }

    private const int SpdrpCompatibleIds = 0x00000002;
    private const int SpdrpService = 0x00000004;
    private const int DigcfDeviceInterface = 0x00000010;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW")]
    private static extern nint SetupDiGetClassDevsByGuid(ref Guid classGuid, string? enumerator, nint parent, int flags);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        nint set, nint info, ref Guid interfaceClass, uint index, ref DeviceInterfaceData data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceRegistryPropertyW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        nint set, ref DeviceInfoData info, int property, out int type, [Out] byte[] buffer, int size, out int used);

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
    /// The problem code of a device node. Forty three means
    /// "unreadable descriptor", twenty eight "no driver".
    /// </summary>
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Status")]
    private static extern int CmGetDevNodeStatus(
        out uint status, out uint problem, uint devInst, int flags);
}
