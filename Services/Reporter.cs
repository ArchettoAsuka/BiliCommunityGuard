using BiliCommunityGuard.App.Models;

namespace BiliCommunityGuard.App.Services;

public sealed class Reporter
{
    private readonly BiliApiClient _apiClient;

    public Reporter(BiliApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<ReportResult> ReportCommentAsync(AccountSession account, GuardContentItem content, BiliComment comment, ReportConfig reportConfig, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["type"] = content.CommentType.ToString(),
            ["oid"] = content.Oid.ToString(),
            ["rpid"] = comment.Rpid.ToString(),
            ["reason"] = reportConfig.Reason.ToString(),
            ["content"] = reportConfig.Content,
            ["csrf"] = account.Csrf
        };

        using var json = await _apiClient.PostFormAsync(account, "https://api.bilibili.com/x/v2/reply/report", form, cancellationToken);
        var root = json.RootElement;
        var code = root.TryGetInt32ByPath("code") ?? -1;
        var message = root.TryGetStringByPath("message") ?? root.TryGetStringByPath("msg") ?? string.Empty;

        return new ReportResult
        {
            Code = code,
            Message = string.IsNullOrWhiteSpace(message) ? "(empty)" : message,
            Success = code == 0,
            AlreadyReported = code == 12008,
            ShouldCooldown = code is 12019 or -509,
            ShouldInvalidate = code is -101 or -111
        };
    }
}
