using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleLibrary.Infrastructure.BsData.Json;

// BSData serializes numeric fields as strings, and uses "" for "no value".
// Reads a number from either a JSON number or string; empty/unparseable -> 0.
internal sealed class LenientDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDouble();
        }
        var s = reader.GetString();
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
