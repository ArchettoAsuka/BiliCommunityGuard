using BiliCommunityGuard.App.Infrastructure;

namespace BiliCommunityGuard.App.Models;

public sealed class AppConfig
{
    public int ScanIntervalSeconds { get; set; } = 60;

    public List<long> ProtectUps { get; } = [];

    public int VideoWindowSize { get; set; } = 10;

    public int DynamicWindowSize { get; set; } = 10;

    public int ScanBatchSizePerCycle { get; set; } = 1;

    public int CommentPageSize { get; set; } = 20;

    public bool IncludeSubReplies { get; set; }

    public ReportConfig Report { get; set; } = new();

    public string AccountStrategy { get; set; } = "round_robin";

    public bool DryRun { get; set; }

    public RequestIntervalConfig RequestIntervalMs { get; set; } = new();

    public int MinReReportIntervalSeconds { get; set; } = 600;

    public int AccountCooldownSeconds { get; set; } = 300;
}

public sealed class ReportConfig
{
    public int Reason { get; set; } = 1;

    public string Content { get; set; } = string.Empty;
}

public sealed class RequestIntervalConfig
{
    public int Min { get; set; } = 1500;

    public int Max { get; set; } = 4000;
}
