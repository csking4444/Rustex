using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rustex.Api.Serialization;

/// <summary>Serializes ulong as a JSON string instead of a number. Steam64 ids (e.g.
/// 76561198012345678) exceed Number.MAX_SAFE_INTEGER (2^53-1), so the default numeric
/// serialization silently corrupts the last couple of digits once JS parses it — every SteamId
/// field returned to the frontend (team members, chat, smart devices) needs this.</summary>
public sealed class UlongStringConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? ulong.Parse(reader.GetString()!) : reader.GetUInt64();

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public sealed class NullableUlongStringConverter : JsonConverter<ulong?>
{
    public override ulong? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null
        : reader.TokenType == JsonTokenType.String ? ulong.Parse(reader.GetString()!)
        : reader.GetUInt64();

    public override void Write(Utf8JsonWriter writer, ulong? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToString());
    }
}
