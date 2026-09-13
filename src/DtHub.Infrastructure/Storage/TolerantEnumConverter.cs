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

            return Enum.IsDefined(value) ? value : Fallback;
        }

        var name = reader.GetString();

        return Enum.TryParse<TEnum>(name, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : Fallback;
    }

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
