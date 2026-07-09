using System.IO;
using System.Text.Json;
using BiliCommunityGuard.App.Models;

namespace BiliCommunityGuard.App.Services;

public sealed class CookieParser
{
    private static readonly string[] RequiredKeys = ["SESSDATA", "bili_jct", "DedeUserID"];

    public IReadOnlyList<CookieAccount> LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return ParseJsonArray(json);
    }

    public IReadOnlyList<CookieAccount> ParseJsonArray(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("cookie.json 根节点必须是 JSON 数组。");
        }

        var accounts = new List<CookieAccount>();
        var index = 0;

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"cookie.json 第 {index + 1} 项不是字符串。");
            }

            var rawCookie = element.GetString()?.Trim() ?? string.Empty;
            var fields = ParseCookieString(rawCookie);
            var missingFields = RequiredKeys
                .Where(key => !fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                .ToArray();

            accounts.Add(new CookieAccount
            {
                Index = index,
                RawCookie = rawCookie,
                Fields = fields,
                MissingFields = missingFields
            });

            index++;
        }

        return accounts;
    }

    private static IReadOnlyDictionary<string, string> ParseCookieString(string rawCookie)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in rawCookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim();
            var value = segment[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            values[key] = value;
        }

        return values;
    }
}

