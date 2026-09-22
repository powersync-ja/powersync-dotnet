using Newtonsoft.Json;

namespace PowerSync.Common.Utils.Converters;

/// <summary>
/// Converts a long to and from a string when converting JSON values. Used for
/// checkpoint request IDs and write checkpoints, which are represented as longs
/// in the SDK but as strings on the wire and in the core extension.
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
