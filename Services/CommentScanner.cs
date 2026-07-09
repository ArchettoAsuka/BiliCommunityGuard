using System.Text.Json;
using BiliCommunityGuard.App.Models;

namespace BiliCommunityGuard.App.Services;

public sealed class CommentScanner
{
    private readonly BiliApiClient _apiClient;

    public CommentScanner(BiliApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<IReadOnlyList<BiliComment>> FetchLatestCommentsAsync(AccountSession account, GuardContentItem content, int pageSize, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["type"] = content.CommentType.ToString(),
            ["oid"] = content.Oid.ToString(),
            ["sort"] = "0",
            ["pn"] = "1",
            ["ps"] = Math.Clamp(pageSize, 1, 20).ToString(),
            ["nohot"] = "1"
        };

        using var json = await _apiClient.GetJsonAsync(account, "https://api.bilibili.com/x/v2/reply", parameters, cancellationToken);
        var root = json.RootElement;
        var code = root.TryGetInt32ByPath("code") ?? -1;
        if (code != 0)
        {
            throw new InvalidOperationException($"获取评论失败，code={code}, content={content.ContentKey}");
        }

        if (!root.TryGetPropertyValue("data", out var data) || !data.TryGetPropertyValue("replies", out var replies) || replies.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BiliComment>();
        }

        var comments = new List<BiliComment>();
        foreach (var reply in replies.EnumerateArray())
        {
            var rpid = reply.TryGetInt64ByPath("rpid") ?? reply.TryGetInt64ByPath("rpid_str");
            var authorMid = reply.TryGetInt64ByPath("member", "mid");
            if (rpid is null || authorMid is null)
            {
                continue;
            }

            comments.Add(new BiliComment
            {
                Rpid = rpid.Value,
                AuthorMid = authorMid.Value,
                AuthorName = reply.TryGetStringByPath("member", "uname") ?? string.Empty,
                Message = reply.TryGetStringByPath("content", "message") ?? string.Empty,
                CreatedAtUnixSeconds = reply.TryGetInt64ByPath("ctime") ?? 0
            });
        }

        return comments;
    }
}
