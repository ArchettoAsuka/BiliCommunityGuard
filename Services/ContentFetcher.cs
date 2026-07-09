using System.Text.Json;
using BiliCommunityGuard.App.Models;

namespace BiliCommunityGuard.App.Services;

public sealed class ContentFetcher
{
    private const string DynamicFeatures = "itemOpusStyle,listOnlyfans,opusBigCover,onlyfansVote,decorationCard,forwardListHidden,ugcDelete";

    private readonly BiliApiClient _apiClient;
    private readonly WbiSigner _wbiSigner;
    private readonly Action<string> _log;

    public ContentFetcher(BiliApiClient apiClient, WbiSigner wbiSigner, Action<string> log)
    {
        _apiClient = apiClient;
        _wbiSigner = wbiSigner;
        _log = log;
    }

    public async Task<IReadOnlyList<GuardContentItem>> FetchProtectedContentAsync(AccountSession account, AppConfig config, CancellationToken cancellationToken)
    {
        var results = new List<GuardContentItem>();

        foreach (var upMid in config.ProtectUps)
        {
            var videos = await FetchLatestVideosAsync(account, upMid, config.VideoWindowSize, cancellationToken);
            var dynamics = await FetchLatestDynamicsAsync(account, upMid, config.DynamicWindowSize, cancellationToken);
            results.AddRange(videos);
            results.AddRange(dynamics);
        }

        return results
            .OrderByDescending(item => item.PublishedAtUnixSeconds)
            .ThenBy(item => item.SourceType)
            .ThenBy(item => item.ContentId, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyList<GuardContentItem>> FetchLatestVideosAsync(AccountSession account, long upMid, int windowSize, CancellationToken cancellationToken)
    {
        var results = new List<GuardContentItem>();
        if (windowSize <= 0)
        {
            return results;
        }

        var page = 1;
        while (results.Count < windowSize)
        {
            var pageSize = Math.Min(30, windowSize - results.Count);
            var parameters = new Dictionary<string, string>
            {
                ["mid"] = upMid.ToString(),
                ["order"] = "pubdate",
                ["pn"] = page.ToString(),
                ["ps"] = pageSize.ToString()
            };

            var signed = await _wbiSigner.SignAsync(account, parameters, cancellationToken);
            using var json = await _apiClient.GetJsonAsync(account, "https://api.bilibili.com/x/space/wbi/arc/search", signed, cancellationToken);
            var root = json.RootElement;
            var code = root.TryGetInt32ByPath("code") ?? -1;
            if (code != 0)
            {
                throw new InvalidOperationException($"获取 UP {upMid} 视频失败，code={code}");
            }

            if (!root.TryGetPropertyValue("data", out var data) || !data.TryGetPropertyValue("list", out var list) || !list.TryGetPropertyValue("vlist", out var vlist) || vlist.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var added = 0;
            foreach (var video in vlist.EnumerateArray())
            {
                var aid = video.TryGetInt64ByPath("aid");
                var bvid = video.TryGetStringByPath("bvid") ?? string.Empty;
                if (aid is null || string.IsNullOrWhiteSpace(bvid))
                {
                    continue;
                }

                results.Add(new GuardContentItem
                {
                    SourceType = BiliContentSourceType.Video,
                    UpMid = upMid,
                    ContentId = bvid,
                    Oid = aid.Value,
                    CommentType = 1,
                    Title = video.TryGetStringByPath("title") ?? bvid,
                    PublishedAtUnixSeconds = video.TryGetInt64ByPath("created") ?? 0
                });
                added++;
            }

            if (added == 0)
            {
                break;
            }

            page++;
        }

        _log($"UP {upMid} 最新视频抓取完成：{results.Count} 条。");
        return results;
    }

    private async Task<IReadOnlyList<GuardContentItem>> FetchLatestDynamicsAsync(AccountSession account, long upMid, int windowSize, CancellationToken cancellationToken)
    {
        var results = new List<GuardContentItem>();
        if (windowSize <= 0)
        {
            return results;
        }

        string? offset = null;
        while (results.Count < windowSize)
        {
            var parameters = new Dictionary<string, string>
            {
                ["host_mid"] = upMid.ToString(),
                ["features"] = DynamicFeatures,
                ["timezone_offset"] = "-480",
                ["platform"] = "web",
                ["web_location"] = "333.1387"
            };

            if (!string.IsNullOrWhiteSpace(offset))
            {
                parameters["offset"] = offset;
            }

            var signed = await _wbiSigner.SignAsync(account, parameters, cancellationToken);
            using var json = await _apiClient.GetJsonAsync(account, "https://api.bilibili.com/x/polymer/web-dynamic/v1/feed/space", signed, cancellationToken);
            var root = json.RootElement;
            var code = root.TryGetInt32ByPath("code") ?? -1;
            if (code != 0)
            {
                throw new InvalidOperationException($"获取 UP {upMid} 动态失败，code={code}");
            }

            if (!root.TryGetPropertyValue("data", out var data) || !data.TryGetPropertyValue("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var added = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (TryParseDynamicItem(item, upMid, out var content))
                {
                    results.Add(content);
                    added++;
                    if (results.Count >= windowSize)
                    {
                        break;
                    }
                }
            }

            offset = data.TryGetStringByPath("offset");
            if (added == 0 || string.IsNullOrWhiteSpace(offset))
            {
                break;
            }
        }

        _log($"UP {upMid} 最新动态抓取完成：{results.Count} 条。");
        return results;
    }

    private static bool TryParseDynamicItem(JsonElement item, long upMid, out GuardContentItem content)
    {
        content = default!;
        var commentType = item.TryGetInt32ByPath("basic", "comment_type") ?? 0;
        if (commentType <= 0)
        {
            return false;
        }

        var commentId = item.TryGetStringByPath("basic", "comment_id_str")
            ?? item.TryGetStringByPath("basic", "rid_str")
            ?? item.TryGetStringByPath("id_str");

        if (!long.TryParse(commentId, out var oid) || oid <= 0)
        {
            return false;
        }

        var contentId = item.TryGetStringByPath("id_str")
            ?? item.TryGetStringByPath("basic", "rid_str")
            ?? oid.ToString();

        var title = item.TryGetStringByPath("modules", "module_dynamic", "major", "opus", "title")
            ?? item.TryGetStringByPath("modules", "module_dynamic", "desc", "text")
            ?? item.TryGetStringByPath("type")
            ?? contentId;

        var publishedAt = item.TryGetInt64ByPath("modules", "module_author", "pub_ts") ?? 0;

        content = new GuardContentItem
        {
            SourceType = BiliContentSourceType.Dynamic,
            UpMid = upMid,
            ContentId = contentId,
            Oid = oid,
            CommentType = commentType,
            Title = title,
            PublishedAtUnixSeconds = publishedAt
        };
        return true;
    }
}
