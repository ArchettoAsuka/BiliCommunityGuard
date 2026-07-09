using System.Text.Json;

namespace BiliCommunityGuard.App.Services;

public static class JsonElementExtensions
{
    public static bool TryGetPropertyValue(this JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public static string? GetStringOrNull(this JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    public static string? TryGetStringByPath(this JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetPropertyValue(segment, out current))
            {
                return null;
            }
        }

        return current.GetStringOrNull();
    }

    public static long? TryGetInt64ByPath(this JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetPropertyValue(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.Number when current.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(current.GetString(), out var value) => value,
            _ => null
        };
    }

    public static int? TryGetInt32ByPath(this JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetPropertyValue(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.Number when current.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(current.GetString(), out var value) => value,
            _ => null
        };
    }
}
