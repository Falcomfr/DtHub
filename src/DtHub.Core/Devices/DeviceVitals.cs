namespace DtHub.Core.Devices;

/// <summary>
/// The four readings taken of a device, kept together.
///
/// **They were already being read and then thrown away.** The health
/// sweep awaits all four per device, hands them to
/// <see cref="DeviceHealth.Review" /> which turns them into sentences,
/// and drops the objects. So "the phone is at 64 %, warm, and on
/// 2.4 GHz" could only be told as three warnings or not at all: there
/// was no way to simply show the state of a phone that had nothing
/// wrong with it.
///
/// This record carries them to the screen without a single extra call
/// to the device. It changes nothing about the findings, which stay
/// the business of <see cref="DeviceHealth" />: the sentences say what
/// is wrong, this says what is.
///
/// It is a record, and that matters: value equality lets the view
/// model drop an identical sweep without raising anything, the same
/// way it already does for the battery alone.
/// </summary>
/// <param name="Battery">Charge and whether it is plugged in.</param>
/// <param name="Heat">Thermal state, never a temperature in degrees.</param>
/// <param name="Storage">Free space on the data partition.</param>
/// <param name="Link">The Wi-Fi link, absent over USB.</param>
public sealed record DeviceVitals(
    BatteryReading? Battery,
    ThermalReading? Heat,
    StorageReading? Storage,
    WifiLink? Link);
