namespace BiliCommunityGuard.App.Models;

public sealed class BotRunMetrics
{
    public int CycleCount { get; set; }

    public int ScannedContentCount { get; set; }

    public int BlacklistHitCount { get; set; }

    public int ReportAttemptCount { get; set; }

    public int SuccessfulReportCount { get; set; }

    public string LastContentLabel { get; set; } = "-";

    public DateTimeOffset? LastActivityUtc { get; set; }
}
