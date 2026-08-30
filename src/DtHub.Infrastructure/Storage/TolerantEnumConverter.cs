using System.Text.Json;
using System.Text.Json.Serialization;

using DtHub.Core.Storage;

namespace DtHub.Infrastructure.Storage;

/// <summary>
/// Lit les énumérations en clair comme le convertisseur standard, mais ne
/// refuse pas un nom inconnu : il retombe sur le repli déclaré.
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
        // Les nombres restent acceptés : d'anciens fichiers en portent, et un
        // fichier modifié à la main aussi.
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

/// <summary>Fabrique le convertisseur pour n'importe quelle énumération.</summary>
public sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert))!;
}
