using Newtonsoft.Json;

namespace PowerSync.Common.Utils.Converters;

/// <summary>
/// Converts a long to and from a string when converting JSON values. Used
/// for converting checkpoint request IDs from a long to a string before being
/// passed to the core extension.
///
/// TODO: This is not currently in use because checkpoint request IDs are
///       currently represented as strings, however this is going to change
///       in the 1.0 release.
/// </summary>
internal class StringLongConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(long) || objectType == typeof(long?);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteValue(value.ToString());
        }
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null!;

        var val = reader.Value?.ToString();

        if (long.TryParse(val, out long result))
        {
            return result;
        }

        throw new JsonSerializationException($"Cannot convert value {val} to long.");
    }
}
