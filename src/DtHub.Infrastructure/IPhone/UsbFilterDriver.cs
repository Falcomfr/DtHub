using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

using DtHub.Core.Dependencies;
using DtHub.Core.Devices;
using DtHub.Core.IPhone;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.Infrastructure.IPhone;

/// <summary>
/// Installs or removes the libusb-win32 filter on Apple devices through
/// the project's <c>install-filter.exe</c>, run elevated.
///
/// What runs elevated is a third-party installer, so every hardware id is
/// checked against the Apple pattern before anything is provisioned or
/// started: a foreign id never reaches it.
/// </summary>
public sealed partial class UsbFilterDriver : IUsbFilterDriver
{
    private const int ElevationRefused = 1223;

    private readonly IUsbEnumerationInspector _usb;
    private readonly IDependencyProvisioner _provisioner;
    private readonly Func<string, string[], Task<int>> _runElevated;

    public UsbFilterDriver(IUsbEnumerationInspector usb, IDependencyProvisioner provisioner)
        : this(usb, provisioner, RunElevatedAsync)
    {
    }

    /// <summary>For tests: the elevated run is replaced by <paramref name="runElevated"/> (file, arguments, exit code).</summary>
    public UsbFilterDriver(
        IUsbEnumerationInspector usb,
        IDependencyProvisioner provisioner,
        Func<string, string[], Task<int>> runElevated)
    {
        _usb = usb;
        _provisioner = provisioner;
        _runElevated = runElevated;
    }

    [GeneratedRegex(@"^USB\\VID_05AC&PID_[0-9A-F]{4}\z")]
    private static partial Regex AppleId();

    /// <summary><c>USB\VID_05AC&amp;PID_xxxx</c> and nothing else: no serial, no interface.</summary>
    public static bool IsAppleHardwareId(string id) => AppleId().IsMatch(id);

    /// <summary><c>USB\VID_x&amp;PID_y\serial</c> without its serial.</summary>
    public static string ParentHardwareId(string pnpDeviceId) =>
        pnpDeviceId.Split('\\') is { Length: >= 2 } parts ? parts[0] + '\\' + parts[1] : pnpDeviceId;

    public static string[] Arguments(bool install, string hardwareId) =>
        [install ? "install" : "uninstall", "--device=" + hardwareId];

    private IEnumerable<UsbPresence> AppleParents() =>
        _usb.Present().Where(p =>
            IsAppleHardwareId(ParentHardwareId(p.Id))
            && string.Equals(p.Service, "usbccgp", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> PresentAppleDevices() =>
        [.. AppleParents().Select(p => ParentHardwareId(p.Id)).Distinct(StringComparer.OrdinalIgnoreCase)];

    public bool IsFiltered(string hardwareId) =>
        AppleParents().Any(p =>
            string.Equals(ParentHardwareId(p.Id), hardwareId, StringComparison.OrdinalIgnoreCase)
            && p.Filters.Contains("libusb0", StringComparer.OrdinalIgnoreCase));

    public string? LibUsb0Path()
    {
        var exe = _provisioner.TryGetExistingPath(DependencyManifest.Get(DependencyManifest.LibUsbWin32Key));
        var dll = exe is null ? null : Path.Combine(Path.GetDirectoryName(exe)!, "libusb0.dll");

        return dll is not null && File.Exists(dll) ? dll : null;
    }

    public Task<UsbFilterResult> InstallAsync(IReadOnlyList<string> hardwareIds, CancellationToken cancellationToken) =>
        RunAsync(true, hardwareIds, cancellationToken);

    public Task<UsbFilterResult> UninstallAsync(IReadOnlyList<string> hardwareIds, CancellationToken cancellationToken) =>
        RunAsync(false, hardwareIds, cancellationToken);

    private async Task<UsbFilterResult> RunAsync(bool install, IReadOnlyList<string> hardwareIds, CancellationToken cancellationToken)
    {
        foreach (var id in hardwareIds)
        {
            if (!IsAppleHardwareId(id))
            {
                throw new ArgumentException("Not an Apple hardware id: " + id, nameof(hardwareIds));
            }
        }

        if (hardwareIds.Count == 0)
        {
            return UsbFilterResult.Done;
        }

        var exe = await _provisioner.EnsureAvailableAsync(
            DependencyManifest.Get(DependencyManifest.LibUsbWin32Key), null, cancellationToken).ConfigureAwait(false);

        foreach (var id in hardwareIds)
        {
            try
            {
                if (await _runElevated(exe, Arguments(install, id)).ConfigureAwait(false) != 0)
                {
                    return UsbFilterResult.Failed;
                }
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ElevationRefused)
            {
                // Declining the UAC prompt is the player's choice, not a fault: nothing to log.
                return UsbFilterResult.Refused;
            }
        }

        return UsbFilterResult.Done;
    }

    private static async Task<int> RunElevatedAsync(string fileName, string[] arguments)
    {
        // shortcut: ShellExecute takes the string as-is, only spaces and quotes are quoted.
        var line = string.Join(' ', arguments.Select(a => a.Any(c => c is ' ' or '"') ? '"' + a.Replace("\"", "\\\"") + '"' : a));

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = line,
            UseShellExecute = true,
            Verb = "runas",
        }) ?? throw new InvalidOperationException("install-filter did not start.");

        await process.WaitForExitAsync().ConfigureAwait(false);

        return process.ExitCode;
    }
}
