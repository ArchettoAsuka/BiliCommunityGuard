using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BiliCommunityGuard.App.Models;

namespace BiliCommunityGuard.App.Services;

public sealed class WbiSigner
{
    private static readonly int[] MixinKeyEncTab =
    [
        46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
        27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13,
        37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4,
        22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52
    ];

    private readonly BiliApiClient _apiClient;
    private string? _imgKey;
    private string? _subKey;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

    public WbiSigner(BiliApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<Dictionary<string, string>> SignAsync(AccountSession account, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        await EnsureKeysAsync(account, cancellationToken);

        var signedParameters = parameters.ToDictionary(pair => pair.Key, pair => FilterValue(pair.Value), StringComparer.Ordinal);
        signedParameters["wts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var query = string.Join("&", signedParameters.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));
        var mixinKey = BuildMixinKey(_imgKey!, _subKey!);
        signedParameters["w_rid"] = ComputeMd5(query + mixinKey);

        return signedParameters;
    }

    private async Task EnsureKeysAsync(AccountSession account, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _expiresAtUtc && !string.IsNullOrWhiteSpace(_imgKey) && !string.IsNullOrWhiteSpace(_subKey))
        {
            return;
        }

        using var json = await _apiClient.GetJsonAsync(account, "https://api.bilibili.com/x/web-interface/nav", null, cancellationToken);
        var data = json.RootElement.GetProperty("data");
        var imgUrl = data.TryGetStringByPath("wbi_img", "img_url") ?? throw new InvalidOperationException("nav 响应缺少 wbi_img.img_url。");
        var subUrl = data.TryGetStringByPath("wbi_img", "sub_url") ?? throw new InvalidOperationException("nav 响应缺少 wbi_img.sub_url。");

        _imgKey = ExtractFileName(imgUrl);
        _subKey = ExtractFileName(subUrl);
        _expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30);
    }

    private static string ExtractFileName(string url)
    {
        var lastSegment = url.Split('/').LastOrDefault() ?? string.Empty;
        var dotIndex = lastSegment.IndexOf('.');
        return dotIndex > 0 ? lastSegment[..dotIndex] : lastSegment;
    }

    private static string BuildMixinKey(string imgKey, string subKey)
    {
        var source = imgKey + subKey;
        var builder = new StringBuilder(32);
        foreach (var index in MixinKeyEncTab)
        {
            if (index < source.Length)
            {
                builder.Append(source[index]);
            }

            if (builder.Length >= 32)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static string FilterValue(string value)
    {
        return value.Replace("!", string.Empty, StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal);
    }

    private static string ComputeMd5(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
