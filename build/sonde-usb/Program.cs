// Development probe: what Windows holds against its USB devices.
//
// It goes through the shipped code, WindowsUsbInspector, rather
// than through a reimplementation that could be wrong in a way
// that agrees with itself. Used to verify that the scan works from
// an ordinary account, without elevation, and that it correctly
// files a problem code under its family.
//
//   dotnet.exe run --project build/sonde-usb
using DtHub.Core.Devices;
using DtHub.Infrastructure.Devices;

var faults = new WindowsUsbInspector().Faults();

Console.WriteLine($"élevé : {Environment.IsPrivilegedProcess}");
Console.WriteLine($"{faults.Count} périphérique(s) USB en défaut");
Console.WriteLine();

foreach (var fault in faults)
{
    Console.WriteLine($"  code {fault.ProblemCode,3}  {fault.Kind,-14}  {fault.DeviceId}");
}
