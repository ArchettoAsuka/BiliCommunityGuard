namespace BiliCommunityGuard.App.Models;

public sealed class GuardState
{
    public int ContentCursor { get; set; }

    public Dictionary<string, ContentScanState> Contents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ContentScanState
{
    public DateTimeOffset? LastScanAtUtc { get; set; }

    public int ScanCount { get; set; }
}

public sealed class ReportHistoryStore
{
    public Dictionary<string, CommentReportHistory> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CommentReportHistory
{
    public DateTimeOffset? FirstReportAtUtc { get; set; }

    public DateTimeOffset? LastReportAtUtc { get; set; }

    public List<ReportAttemptRecord> Attempts { get; set; } = [];
}

public sealed class ReportAttemptRecord
{
    public string AccountId { get; set; } = string.Empty;

    public int Code { get; set; }

    public string Message { get; set; } = string.Empty;

    public bool Success { get; set; }

    public DateTimeOffset ReportedAtUtc { get; set; }
}
