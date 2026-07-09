using BiliCommunityGuard.App.Models;

namespace BiliCommunityGuard.App.Services;

public sealed class GuardBotRunner
{
    private readonly AppConfig _config;
    private readonly IReadOnlyList<CookieAccount> _cookieAccounts;
    private readonly HashSet<long> _blacklist;
    private readonly string _statePath;
    private readonly string _reportHistoryPath;
    private readonly Action<string> _log;
    private readonly Action<string> _status;
    private readonly Action<CookieAccount, string, string> _accountStatusUpdater;
    private readonly Action<BotRunMetrics> _metricsUpdater;
    private readonly StateStore _stateStore;
    private readonly AccountManager _accountManager;
    private readonly ContentFetcher _contentFetcher;
    private readonly CommentScanner _commentScanner;
    private readonly Reporter _reporter;
    private readonly TimeSpan _scanInterval;
    private readonly TimeSpan _minReReportInterval;
    private readonly TimeSpan _accountCooldown;

    public GuardBotRunner(
        AppConfig config,
        IReadOnlyList<CookieAccount> cookieAccounts,
        IReadOnlyCollection<long> blacklist,
        string statePath,
        string reportHistoryPath,
        Action<string> log,
        Action<string> status,
        Action<CookieAccount, string, string> accountStatusUpdater,
        Action<BotRunMetrics> metricsUpdater)
    {
        _config = config;
        _cookieAccounts = cookieAccounts;
        _blacklist = new HashSet<long>(blacklist);
        _statePath = statePath;
        _reportHistoryPath = reportHistoryPath;
        _log = log;
        _status = status;
        _accountStatusUpdater = accountStatusUpdater;
        _metricsUpdater = metricsUpdater;

        _scanInterval = TimeSpan.FromSeconds(Math.Max(5, config.ScanIntervalSeconds));
        _minReReportInterval = TimeSpan.FromSeconds(Math.Max(0, config.MinReReportIntervalSeconds));
        _accountCooldown = TimeSpan.FromSeconds(Math.Max(5, config.AccountCooldownSeconds));

        _stateStore = new StateStore();
        var requestDelayer = new RequestDelayer(config.RequestIntervalMs.Min, config.RequestIntervalMs.Max);
        var apiClient = new BiliApiClient(requestDelayer);
        var wbiSigner = new WbiSigner(apiClient);
        _accountManager = new AccountManager(cookieAccounts, apiClient, log, accountStatusUpdater);
        _contentFetcher = new ContentFetcher(apiClient, wbiSigner, log);
        _commentScanner = new CommentScanner(apiClient);
        _reporter = new Reporter(apiClient);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var metrics = new BotRunMetrics();
        var state = _stateStore.LoadState(_statePath);
        var reportHistory = _stateStore.LoadReportHistory(_reportHistoryPath);

        _status("正在校验账号...");
        await _accountManager.InitializeAsync(cancellationToken);
        EnsureUsableAccounts();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                metrics.CycleCount++;
                _status($"第 {metrics.CycleCount} 轮扫描中...");
                _log($"开始第 {metrics.CycleCount} 轮扫描。");

                var fetchAccount = _accountManager.GetFetchAccount() ?? throw new InvalidOperationException("没有可用于抓取内容的账号。");
                var protectedContents = await _contentFetcher.FetchProtectedContentAsync(fetchAccount, _config, cancellationToken);
                if (protectedContents.Count == 0)
                {
                    _log("当前没有可扫描的保护内容。等待下一轮。");
                    metrics.LastActivityUtc = DateTimeOffset.UtcNow;
                    _metricsUpdater(metrics);
                    await Task.Delay(_scanInterval, cancellationToken);
                    continue;
                }

                var batchCount = Math.Clamp(_config.ScanBatchSizePerCycle, 1, protectedContents.Count);
                for (var offset = 0; offset < batchCount; offset++)
                {
                    var index = (state.ContentCursor + offset) % protectedContents.Count;
                    var content = protectedContents[index];
                    await ProcessContentAsync(content, state, reportHistory, metrics, cancellationToken);
                }

                state.ContentCursor = (state.ContentCursor + batchCount) % protectedContents.Count;
                await _stateStore.SaveAsync(_statePath, state, _reportHistoryPath, reportHistory, cancellationToken);
                metrics.LastActivityUtc = DateTimeOffset.UtcNow;
                _metricsUpdater(metrics);
                _status($"第 {metrics.CycleCount} 轮完成。已扫描 {metrics.ScannedContentCount} 个内容，举报尝试 {metrics.ReportAttemptCount} 次。");
                await Task.Delay(_scanInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _log($"[ERROR] 扫描轮次失败：{exception.Message}");
                _status($"扫描异常：{exception.Message}");
                metrics.LastActivityUtc = DateTimeOffset.UtcNow;
                _metricsUpdater(metrics);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        _status("机器人已停止。");
    }

    private async Task ProcessContentAsync(GuardContentItem content, GuardState state, ReportHistoryStore reportHistory, BotRunMetrics metrics, CancellationToken cancellationToken)
    {
        var fetchAccount = _accountManager.GetFetchAccount() ?? throw new InvalidOperationException("没有可用于读取评论的账号。");
        metrics.ScannedContentCount++;
        metrics.LastContentLabel = content.DisplayLabel;
        metrics.LastActivityUtc = DateTimeOffset.UtcNow;
        _metricsUpdater(metrics);

        _log($"扫描内容：{content.DisplayLabel}");
        var comments = await _commentScanner.FetchLatestCommentsAsync(fetchAccount, content, _config.CommentPageSize, cancellationToken);
        _stateStore.TouchContent(state, content.ContentKey);
        if (comments.Count == 0)
        {
            _log($"未读取到评论：{content.DisplayLabel}");
            return;
        }

        var processedInCycle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in comments)
        {
            var commentKey = comment.CommentKey(content.CommentType, content.Oid);
            if (!processedInCycle.Add(commentKey))
            {
                continue;
            }

            if (!_blacklist.Contains(comment.AuthorMid))
            {
                continue;
            }

            metrics.BlacklistHitCount++;
            metrics.LastActivityUtc = DateTimeOffset.UtcNow;
            _metricsUpdater(metrics);
            _log($"命中黑名单：uid={comment.AuthorMid}, uname={comment.AuthorName}, rpid={comment.Rpid}, content={content.ContentId}");

            var reportAccounts = _accountManager.GetReportAccounts();
            if (reportAccounts.Count == 0)
            {
                _log("当前没有可用举报账号，跳过本条评论。");
                continue;
            }

            var attempted = false;
            foreach (var account in reportAccounts)
            {
                if (!_stateStore.CanAttempt(reportHistory, commentKey, account.AccountId, _minReReportInterval, out var waitRemaining))
                {
                    _log($"跳过账号 {account.AccountId}，同条评论再次举报冷却剩余 {waitRemaining.TotalSeconds:F0}s。");
                    continue;
                }

                attempted = true;
                metrics.ReportAttemptCount++;
                _metricsUpdater(metrics);

                if (_config.DryRun)
                {
                    _stateStore.RecordAttempt(reportHistory, commentKey, account.AccountId, 0, "dry-run", true);
                    _log($"[DRY-RUN] 账号 {account.AccountId} 将举报评论 rpid={comment.Rpid}");
                    continue;
                }

                var result = await _reporter.ReportCommentAsync(account, content, comment, _config.Report, cancellationToken);
                var success = result.Success || result.AlreadyReported;
                _stateStore.RecordAttempt(reportHistory, commentKey, account.AccountId, result.Code, result.Message, success);

                if (result.Success)
                {
                    metrics.SuccessfulReportCount++;
                    _log($"举报成功：账号 {account.AccountId} -> rpid={comment.Rpid}");
                }
                else if (result.AlreadyReported)
                {
                    _log($"账号 {account.AccountId} 已举报过该评论：rpid={comment.Rpid}");
                }
                else
                {
                    _log($"举报返回 code={result.Code}, message={result.Message}, account={account.AccountId}, rpid={comment.Rpid}");
                }

                if (result.ShouldCooldown)
                {
                    _accountManager.MarkCooldown(account, _accountCooldown, $"code={result.Code}, message={result.Message}");
                }

                if (result.ShouldInvalidate)
                {
                    _accountManager.MarkInvalid(account, $"code={result.Code}, message={result.Message}");
                }

                metrics.LastActivityUtc = DateTimeOffset.UtcNow;
                _metricsUpdater(metrics);
            }

            if (!attempted)
            {
                _log($"命中黑名单评论但所有账号都仍在再次举报间隔内：rpid={comment.Rpid}");
            }
        }
    }

    private void EnsureUsableAccounts()
    {
        if (_accountManager.GetFetchAccount() is null)
        {
            throw new InvalidOperationException("没有校验通过的账号，无法启动机器人。");
        }
    }
}



