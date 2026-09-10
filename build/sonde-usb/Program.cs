// Sonde de développement : ce que Windows reproche à ses périphériques USB.
//
// Elle passe par le code livré, WindowsUsbInspector, et non par une
// réimplémentation qui pourrait se tromper d'accord avec elle-même. Sert à
// vérifier que le relevé fonctionne depuis un compte ordinaire, sans élévation,
// et qu'il range bien un code de problème dans sa famille.
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
