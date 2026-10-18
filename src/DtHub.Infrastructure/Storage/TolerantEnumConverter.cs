using System.Text.Json;
using System.Text.Json.Serialization;

using DtHub.Core.Storage;

namespace DtHub.Infrastructure.Storage;

/// <summary>
/// Reads enumerations in plain text like the standard converter, but
/// does not reject an unknown name: it falls back to the declared
/// fallback.
/// </summary>
public sealed class TolerantEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly TEnum Fallback =
        typeof(TEnum).GetCustomAttributes(typeof(JsonFallbackAttribute), inherit: false)
            is [JsonFallbackAttribute declared, ..]
            ? (TEnum)declared.Value
            : default;

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Numbers are still accepted: old files carry them, and so
        // does a hand-edited file.
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
        {
            var value = (TEnum)Enum.ToObject(typeof(TEnum), number);

            return IsKnown(value) ? value : Fallback;
        }

        var name = reader.GetString();

        return Enum.TryParse<TEnum>(name, ignoreCase: true, out var parsed) && IsKnown(parsed)
            ? parsed
            : Fallback;
    }

    private static readonly bool IsFlags = typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false);

    private static readonly ulong DefinedBits =
        Enum.GetValues<TEnum>().Aggregate(0UL, (bits, member) => bits | Convert.ToUInt64(member, System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// A combination of flags has no member of its own: Control | Shift
    /// read back as the fallback made Ctrl+Shift+Tab a bare Tab. Every bit
    /// set must belong to a member, which still refuses a removed one.
    /// </summary>
    private static bool IsKnown(TEnum value) =>
        Enum.IsDefined(value)
        || (IsFlags && (Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture) & ~DefinedBits) == 0);

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>Builds the converter for any enumeration.</summary>
public sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert))!;
}
