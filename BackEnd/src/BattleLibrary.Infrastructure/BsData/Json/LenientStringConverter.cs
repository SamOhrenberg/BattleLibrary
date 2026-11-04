using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleLibrary.Infrastructure.BsData.Json;

// BSData fields that are usually strings (e.g. page) can serialize as numbers.
// Reads a string from a string, number or bool token.
internal sealed class LenientStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Cannot read string from {reader.TokenType}."),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
