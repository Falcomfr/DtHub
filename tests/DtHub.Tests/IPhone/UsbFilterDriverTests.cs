using System.ComponentModel;

using DtHub.Core.Dependencies;
using DtHub.Core.Devices;
using DtHub.Core.IPhone;
using DtHub.Infrastructure.IPhone;

namespace DtHub.Tests.IPhone;

public class UsbFilterDriverTests
{
    private const string Iphone = @"USB\VID_05AC&PID_12A8";

    [Theory]
    [InlineData(@"USB\VID_05AC&PID_12A8", true)]
    [InlineData(@"USB\VID_046D&PID_C52B", false)]
    [InlineData(@"USB\VID_05AC&PID_12A8\serial", false)]
    [InlineData("", false)]
    [InlineData("USB\\VID_05AC&PID_12A8\n", false)]
    [InlineData("USB\\VID_05AC&PID_12a8", false)]
    [InlineData("USB\\VID_05AC&PID_12A8&MI_00", false)]
    public void Seul_un_identifiant_Apple_est_accepte(string id, bool expected) =>
        Assert.Equal(expected, UsbFilterDriver.IsAppleHardwareId(id));

    [Fact]
    public void L_identifiant_parent_perd_le_numero_de_serie() =>
        Assert.Equal(Iphone, UsbFilterDriver.ParentHardwareId(@"USB\VID_05AC&PID_12A8\00008120001A"));

    [Fact]
    public void Les_arguments_visent_un_seul_appareil()
    {
        Assert.Equal(["install", "--device=" + Iphone], UsbFilterDriver.Arguments(true, Iphone));
        Assert.Equal(["uninstall", "--device=" + Iphone], UsbFilterDriver.Arguments(false, Iphone));
    }

    [Fact]
    public async Task Un_identifiant_etranger_est_refuse_sans_rien_lancer()
    {
        var calls = 0;
        var driver = Driver([], (_, _) => { calls++; return Task.FromResult(0); });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            driver.InstallAsync([Iphone, @"USB\VID_046D&PID_C52B"], default));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Un_refus_de_Windows_n_est_pas_un_echec()
    {
        var driver = Driver([], (_, _) => throw new Win32Exception(1223));

        Assert.Equal(UsbFilterResult.Refused, await driver.InstallAsync([Iphone], default));
    }

    [Fact]
    public async Task Les_appareils_sont_traites_un_par_un_et_un_echec_arrete_la_suite()
    {
        List<string> seen = [];
        var driver = Driver([], (_, args) => { seen.Add(args[1]); return Task.FromResult(1); });

        var result = await driver.UninstallAsync([Iphone, @"USB\VID_05AC&PID_12AB"], default);

        Assert.Equal(UsbFilterResult.Failed, result);
        Assert.Equal(["--device=" + Iphone], seen);
    }

    [Fact]
    public void Seuls_les_parents_Apple_composites_sont_presents_et_filtres()
    {
        UsbPresence[] usb =
        [
            new(@"USB\VID_05AC&PID_12A8\A", [], "usbccgp", ["libusb0"]),
            new(@"USB\VID_05AC&PID_12A8&MI_00\A", [], "usbccgp"),
            new(@"USB\VID_05AC&PID_12AB\B", [], "WinUSB"),
            new(@"USB\VID_046D&PID_C52B\C", [], "usbccgp", ["libusb0"]),
        ];
        var driver = Driver(usb, (_, _) => Task.FromResult(0));

        Assert.Equal([Iphone], driver.PresentAppleDevices());
        Assert.True(driver.IsFiltered(Iphone));
        Assert.False(driver.IsFiltered(@"USB\VID_046D&PID_C52B"));
    }

    [Fact]
    public void Un_modele_n_est_filtre_que_si_tous_ses_appareils_presents_le_sont()
    {
        // Two iPhones of one model: the filter is per model, the second may predate it.
        UsbPresence[] usb =
        [
            new(@"USB\VID_05AC&PID_12A8\A", [], "usbccgp", ["libusb0"]),
            new(@"USB\VID_05AC&PID_12A8\B", [], "usbccgp"),
        ];
        var driver = Driver(usb, (_, _) => Task.FromResult(0));

        Assert.False(driver.IsFiltered(Iphone));
    }

    private static UsbFilterDriver Driver(UsbPresence[] usb, Func<string, string[], Task<int>> run) =>
        new(new FakeUsb(usb), new Provisioner(), run);

    private sealed class FakeUsb(UsbPresence[] present) : IUsbEnumerationInspector
    {
        public IReadOnlyList<UsbFault> Faults() => [];

        public IReadOnlyList<UsbPresence> Present() => present;

        public int AdbInterfaces() => 0;
    }

    private sealed class Provisioner : IDependencyProvisioner
    {
        public Task<string> EnsureAvailableAsync(
            ExternalDependency dependency,
            IProgress<ProvisioningProgress>? progress = null,
            CancellationToken cancellationToken = default) => Task.FromResult("install-filter.exe");

        public string? TryGetExistingPath(ExternalDependency dependency) => null;
    }
}
