using System.Text.Json;

namespace DtHub.Core.IPhone;

/// <summary>
/// An iPhone or iPad plugged into the PC, as Apple's USB service
/// (usbmuxd, from the Apple Devices app) reports it.
/// </summary>
public sealed record IPhoneDevice(string Udid, string Name, string IosVersion, string ConnectionType)
{
    /// <summary>
    /// iOS 27 is the first to give a computer the screen and the touches
    /// (Xcode 27's Device Hub); older devices refuse the stream.
    /// </summary>
    public const int MinimumIosMajor = 27;

    public bool CanMirror =>
        int.TryParse(IosVersion.Split('.')[0], out var major) && major >= MinimumIosMajor;

    /// <summary>Reads the JSON printed by <c>pymobiledevice3 usbmux list</c>.</summary>
    /// <exception cref="JsonException">The output is not that list.</exception>
    public static IReadOnlyList<IPhoneDevice> ParseList(string json)
    {
        using var document = JsonDocument.Parse(json);

        return
        [
            .. document.RootElement.EnumerateArray().Select(device => new IPhoneDevice(
                Text(device, "Identifier"),
                Text(device, "DeviceName").Trim(),
                Text(device, "ProductVersion"),
                Text(device, "ConnectionType"))),
        ];
    }

    private static string Text(JsonElement device, string name) =>
        device.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
}
