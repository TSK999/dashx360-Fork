using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XboxMetroLauncher.Services;

internal sealed class FlexibleTimeSpanJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected a string value for a TimeSpan.");
        }

        string? text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return TimeSpan.Zero;
        }

        if (TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out TimeSpan value))
        {
            return value;
        }

        // Older DashX360 libraries stored play time as total-hours:mm:ss, so values
        // above 23 hours (for example 42:30:00) are not valid canonical TimeSpan JSON.
        string[] parts = text.Split(':');
        if (parts.Length == 3
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long totalHours)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            && double.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double seconds)
            && totalHours >= 0
            && minutes is >= 0 and < 60
            && seconds is >= 0 and < 60)
        {
            return TimeSpan.FromHours(totalHours)
                + TimeSpan.FromMinutes(minutes)
                + TimeSpan.FromSeconds(seconds);
        }

        throw new JsonException($"'{text}' is not a supported TimeSpan value.");
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));
    }
}
