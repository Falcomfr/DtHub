using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Guidance;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>
/// What to do when the image shows but nothing responds.
///
/// This is the most common symptom in the field, and the most
/// helpless one: scrcpy does not fail, no message appears, the click
/// simply does nothing. Noted on a competing product's support forum
/// that takes the same path, about a dozen people raise it over
/// several weeks, and the answer travels by word of mouth without
/// ever being written down anywhere.
///
/// The cause comes down to a setting whose subtitle says it all:
/// "Accorder les autorisations et simulation d'entrée via le
/// débogage USB" (Grant permissions and input simulation via USB
/// debugging). Without it, ADB displays but does not inject.
///
/// The help page used to name the cause without being able to
/// confirm it. It can now ask the device the question, on demand,
/// and return one of the only three verdicts it can hold to.
/// </summary>
public sealed partial class InputHelpViewModel : ObservableObject
{
    private readonly DeviceDiscoveryService _devices;

    public InputHelpViewModel(DeviceDiscoveryService devices)
    {
        _devices = devices;
        _brand = PhoneBrands.Standard;
    }

    /// <summary>
    /// Brands offered, grouped when the procedure is the same.
    /// </summary>
    public IReadOnlyList<PhoneBrand> Brands { get; } = PhoneBrands.All;

    [ObservableProperty]
    private PhoneBrand _brand;

    /// <summary>True when the brand adds a pitfall of its own.</summary>
    public bool HasWarning => !string.IsNullOrWhiteSpace(Brand.Warning);

    /// <summary>
    /// Serial number of the queried device, empty if there is none.
    /// </summary>
    private string _serial = string.Empty;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _verdict = string.Empty;

    /// <summary>
    /// True when a verdict is there and deserves to be shown.
    /// </summary>
    public bool HasVerdict => !string.IsNullOrWhiteSpace(Verdict);

    /// <summary>
    /// True when the verdict is the one that names the cause.
    /// </summary>
    [ObservableProperty]
    private bool _verdictIsRefusal;

    /// <summary>Preselects the brand of the connected device.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(true);

            var connected = discovery.Devices.FirstOrDefault(d => d.IsConnected);

            _serial = connected?.Serial ?? string.Empty;

            var manufacturer = discovery.Devices
                .Select(d => d.Manufacturer)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));

            if (manufacturer is not null)
            {
                Brand = PhoneBrands.FromManufacturer(manufacturer);
            }
        }
        catch (AdbException)
        {
            // Without a reachable device, the standard procedure
            // will do.
        }
    }

    /// <summary>
    /// Asks the device whether it accepts input simulation.
    ///
    /// The device is read again on every test: between opening the
    /// help page and the click, the cable may have been plugged in
    /// or the setting changed, and that is exactly what we have come
    /// to check.
    /// </summary>
    [RelayCommand]
    private async Task TestAsync(CancellationToken cancellationToken)
    {
        IsTesting = true;
        Verdict = string.Empty;
        VerdictIsRefusal = false;
        OnPropertyChanged(nameof(HasVerdict));

        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(true);

            if (string.IsNullOrEmpty(_serial))
            {
                Verdict = Strings.Get("DeadInputTestNoDevice");
                return;
            }

            var answer = await _devices
                .CheckInputInjectionAsync(_serial, cancellationToken)
                .ConfigureAwait(true);

            VerdictIsRefusal = answer == InputInjection.Denied;

            Verdict = Strings.Get(answer switch
            {
                InputInjection.Works => "DeadInputTestWorks",
                InputInjection.Denied => "DeadInputTestDenied",
                _ => "DeadInputTestUnknown",
            });
        }
        finally
        {
            IsTesting = false;
            OnPropertyChanged(nameof(HasVerdict));
        }
    }

    partial void OnBrandChanged(PhoneBrand value) => OnPropertyChanged(nameof(HasWarning));
}
