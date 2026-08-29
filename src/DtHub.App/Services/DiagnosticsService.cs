using System.Globalization;
using System.Text;

using DtHub.Core;
using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Scrcpy;
using DtHub.Core.Storage;

namespace DtHub.App.Services;

/// <summary>
/// Rassemble ce qu'il faut savoir pour comprendre un problème : versions,
/// système, appareils vus. Aucun secret n'y figure, le rapport doit pouvoir
/// être collé tel quel dans un ticket.
/// </summary>
public sealed class DiagnosticsService
{
    private readonly IAdbClient _adb;
    private readonly IScrcpyLocator _scrcpy;
    private readonly DeviceDiscoveryService _devices;
    private readonly IAppPaths _paths;

    public DiagnosticsService(
        IAdbClient adb,
        IScrcpyLocator scrcpy,
        DeviceDiscoveryService devices,
        IAppPaths paths)
    {
        _adb = adb;
        _scrcpy = scrcpy;
        _devices = devices;
        _paths = paths;
    }

    /// <summary>Rapport complet, prêt à être copié.</summary>
    public async Task<string> BuildReportAsync(CancellationToken cancellationToken = default)
    {
        var report = new StringBuilder();

        report.AppendLine(CultureInfo.InvariantCulture, $"{ProductInfo.Name} {ProductInfo.Version}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Windows : {Environment.OSVersion.VersionString}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Processeurs : {Environment.ProcessorCount}");
        report.AppendLine(CultureInfo.InvariantCulture, $".NET : {Environment.Version}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Données : {_paths.Root}");
        report.AppendLine();

        report.AppendLine(CultureInfo.InvariantCulture, $"scrcpy : {_scrcpy.Version}");
        report.AppendLine(CultureInfo.InvariantCulture, $"ADB : {await SafeAdbVersionAsync(cancellationToken).ConfigureAwait(false)}");
        report.AppendLine();

        report.AppendLine("Appareils :");

        var discovery = await SafeDiscoverAsync(cancellationToken).ConfigureAwait(false);

        if (discovery.Devices.Count == 0)
        {
            report.AppendLine("  aucun");
        }

        foreach (var device in discovery.Devices)
        {
            // Le numéro de série est un identifiant matériel : il reste utile
            // au diagnostic et n'est pas un secret, contrairement à un code
            // d'appairage, qui n'apparaît nulle part.
            report.AppendLine(
                CultureInfo.InvariantCulture,
                $"  {device.DisplayName} | {device.State} | {device.ConnectionKind} | "
                + $"Android {device.AndroidVersion ?? "?"} (SDK {device.SdkVersion?.ToString(CultureInfo.InvariantCulture) ?? "?"})");
        }

        foreach (var warning in discovery.Warnings)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  avertissement : {warning}");
        }

        return report.ToString();
    }

    private async Task<string> SafeAdbVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _adb.GetVersionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AdbException exception)
        {
            return $"indisponible ({exception.UserMessage})";
        }
    }

    private async Task<DeviceDiscoveryResult> SafeDiscoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AdbException exception)
        {
            return new DeviceDiscoveryResult([], [exception.UserMessage]);
        }
    }
}
