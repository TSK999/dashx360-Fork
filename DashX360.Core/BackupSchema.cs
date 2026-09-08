using System.Text.Json;

namespace XboxMetroLauncher.Utilities;

public static class BackupSchema
{
    public static bool Has(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public static JsonElement? Get(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object
        ? root.EnumerateObject().Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => (JsonElement?)p.Value.Clone()).FirstOrDefault() : null;
    public static HashSet<string> Sections(JsonElement root)
    {
        if (Get(root, "ExportVersion") is JsonElement version && (version.ValueKind != JsonValueKind.String || version.GetString() is not ("1" or "2")))
            throw new InvalidDataException("This backup version is not supported. No data was changed.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("The backup contains duplicate fields.");
            var expected = property.Name.ToUpperInvariant() switch
            {
                "SETTINGS" or "PROFILE" or "LIBRARY" or "FRIENDS" => JsonValueKind.Object,
                "CUSTOMTHEMES" or "GAMEARTWORK" => JsonValueKind.Array,
                _ => JsonValueKind.Undefined
            };
            if (expected != JsonValueKind.Undefined && property.Value.ValueKind != expected)
                throw new InvalidDataException(property.Name + " must contain valid data, not null.");
        }
        return names;
    }
}
