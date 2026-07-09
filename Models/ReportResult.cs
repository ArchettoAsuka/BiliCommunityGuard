namespace BiliCommunityGuard.App.Models;

public sealed class ReportResult
{
    public int Code { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool Success { get; init; }

    public bool AlreadyReported { get; init; }

    public bool ShouldCooldown { get; init; }

    public bool ShouldInvalidate { get; init; }
}
