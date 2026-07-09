using System.IO;
namespace BiliCommunityGuard.App.Services;

public sealed class BlacklistLoader
{
    public IReadOnlyList<long> LoadFromFile(string path)
    {
        var results = new HashSet<long>();

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (!long.TryParse(trimmed, out var userId))
            {
                throw new InvalidDataException($"blacklist.txt 中存在非法 UID: {trimmed}");
            }

            results.Add(userId);
        }

        return results.OrderBy(value => value).ToArray();
    }
}

