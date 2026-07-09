using System.IO;
using System.Text.Json;
using BiliCommunityGuard.App.Models;

namespace BiliCommunityGuard.App.Services;

public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public GuardState LoadState(string path)
    {
        if (!File.Exists(path))
        {
            return new GuardState();
        }

        var content = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GuardState>(content, JsonOptions) ?? new GuardState();
    }

    public ReportHistoryStore LoadReportHistory(string path)
    {
        if (!File.Exists(path))
        {
            return new ReportHistoryStore();
        }

        var content = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ReportHistoryStore>(content, JsonOptions) ?? new ReportHistoryStore();
    }

    public async Task SaveAsync(string statePath, GuardState state, string historyPath, ReportHistoryStore history, CancellationToken cancellationToken)
    {
        EnsureParentDirectory(statePath);
        EnsureParentDirectory(historyPath);

        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(state, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(historyPath, JsonSerializer.Serialize(history, JsonOptions), cancellationToken);
    }

    public bool CanAttempt(ReportHistoryStore history, string commentKey, string accountId, TimeSpan minInterval, out TimeSpan waitRemaining)
    {
        waitRemaining = TimeSpan.Zero;

        if (!history.Entries.TryGetValue(commentKey, out var entry))
        {
            return true;
        }

        var lastAttempt = entry.Attempts
            .Where(attempt => string.Equals(attempt.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(attempt => attempt.ReportedAtUtc)
            .FirstOrDefault();

        if (lastAttempt is null)
        {
            return true;
        }

        var elapsed = DateTimeOffset.UtcNow - lastAttempt.ReportedAtUtc;
        if (elapsed >= minInterval)
        {
            return true;
        }

        waitRemaining = minInterval - elapsed;
        return false;
    }

    public void RecordAttempt(ReportHistoryStore history, string commentKey, string accountId, int code, string message, bool success)
    {
        if (!history.Entries.TryGetValue(commentKey, out var entry))
        {
            entry = new CommentReportHistory();
            history.Entries[commentKey] = entry;
        }

        var timestamp = DateTimeOffset.UtcNow;
        if (entry.FirstReportAtUtc is null)
        {
            entry.FirstReportAtUtc = timestamp;
        }

        entry.LastReportAtUtc = timestamp;
        entry.Attempts.Add(new ReportAttemptRecord
        {
            AccountId = accountId,
            Code = code,
            Message = message,
            Success = success,
            ReportedAtUtc = timestamp
        });

        if (entry.Attempts.Count > 50)
        {
            entry.Attempts = entry.Attempts.OrderByDescending(attempt => attempt.ReportedAtUtc).Take(50).OrderBy(attempt => attempt.ReportedAtUtc).ToList();
        }
    }

    public void TouchContent(GuardState state, string contentKey)
    {
        if (!state.Contents.TryGetValue(contentKey, out var item))
        {
            item = new ContentScanState();
            state.Contents[contentKey] = item;
        }

        item.LastScanAtUtc = DateTimeOffset.UtcNow;
        item.ScanCount++;
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
