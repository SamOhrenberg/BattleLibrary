using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleLibrary.Infrastructure.BsData.Json;

// BSData int fields (revision, min, max) can serialize as strings, incl. "".
// Reads an int from a number or string; empty/unparseable -> 0.
internal sealed class LenientIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt32();
        }
        var s = reader.GetString();
        return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
