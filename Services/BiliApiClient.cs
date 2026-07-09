using System.Net.Http;
using System.Text;
using System.Text.Json;
using BiliCommunityGuard.App.Models;

namespace BiliCommunityGuard.App.Services;

public sealed class BiliApiClient
{
    private readonly RequestDelayer _requestDelayer;

    public BiliApiClient(RequestDelayer requestDelayer)
    {
        _requestDelayer = requestDelayer;
    }

    public HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.bilibili.com");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.bilibili.com");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        return client;
    }

    public async Task<JsonDocument> GetJsonAsync(AccountSession account, string url, IReadOnlyDictionary<string, string>? query, CancellationToken cancellationToken)
    {
        await _requestDelayer.DelayAsync(cancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(url, query));
        request.Headers.TryAddWithoutValidation("Cookie", account.RawCookie);
        var response = await account.HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(content);
    }

    public async Task<JsonDocument> PostFormAsync(AccountSession account, string url, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
    {
        await _requestDelayer.DelayAsync(cancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };

        request.Headers.TryAddWithoutValidation("Cookie", account.RawCookie);
        var response = await account.HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(content);
    }

    private static string BuildUrl(string baseUrl, IReadOnlyDictionary<string, string>? query)
    {
        if (query is null || query.Count == 0)
        {
            return baseUrl;
        }

        var queryString = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var separator = baseUrl.Contains('?') ? '&' : '?';
        return new StringBuilder(baseUrl).Append(separator).Append(queryString).ToString();
    }
}
