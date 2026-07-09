using System.IO;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using BiliCommunityGuard.App.Infrastructure;
using BiliCommunityGuard.App.Models;
using BiliCommunityGuard.App.Services;

namespace BiliCommunityGuard.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string FixedStateRelativePath = "data/state.json";
    private const string FixedReportHistoryRelativePath = "data/report-history.json";

    private readonly ConfigLoader _configLoader = new();
    private readonly CookieParser _cookieParser = new();
    private readonly BlacklistLoader _blacklistLoader = new();

    private AppConfig _config = new();
    private string _configPath;
    private string _cookiePath;
    private string _blacklistPath;
    private string _statusMessage = "请选择或生成输入文件，然后点击“重新加载”。";
    private string _logText = string.Empty;
    private IReadOnlyList<long> _blacklist = Array.Empty<long>();
    private CancellationTokenSource? _runnerCts;
    private bool _isRunning;
    private BotRunMetrics _metrics = new();
    private string _runtimeStateSummary = "未启动";

    public MainViewModel()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        _configPath = Path.Combine(baseDirectory, "config.yaml");
        _cookiePath = Path.Combine(baseDirectory, "cookie.json");
        _blacklistPath = Path.Combine(baseDirectory, "blacklist.txt");

        BrowseConfigCommand = new RelayCommand(_ => BrowseFile(FileKind.Config), _ => !IsRunning);
        BrowseCookieCommand = new RelayCommand(_ => BrowseFile(FileKind.Cookie), _ => !IsRunning);
        BrowseBlacklistCommand = new RelayCommand(_ => BrowseFile(FileKind.Blacklist), _ => !IsRunning);
        ReloadAllCommand = new RelayCommand(_ => ReloadAll(), _ => !IsRunning);
        CreateSampleFilesCommand = new RelayCommand(_ => CreateSampleFiles(), _ => !IsRunning);
        StartBotCommand = new RelayCommand(_ => StartBot(), _ => !IsRunning);
        StopBotCommand = new RelayCommand(_ => StopBot(), _ => IsRunning);

        ReloadAll();
    }

    public ObservableCollection<long> ProtectUps { get; } = [];

    public ObservableCollection<CookieAccount> CookieAccounts { get; } = [];

    public RelayCommand BrowseConfigCommand { get; }

    public RelayCommand BrowseCookieCommand { get; }

    public RelayCommand BrowseBlacklistCommand { get; }

    public RelayCommand ReloadAllCommand { get; }

    public RelayCommand CreateSampleFilesCommand { get; }

    public RelayCommand StartBotCommand { get; }

    public RelayCommand StopBotCommand { get; }

    public string ConfigPath
    {
        get => _configPath;
        set => SetProperty(ref _configPath, value);
    }

    public string CookiePath
    {
        get => _cookiePath;
        set => SetProperty(ref _cookiePath, value);
    }

    public string BlacklistPath
    {
        get => _blacklistPath;
        set => SetProperty(ref _blacklistPath, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string LogText
    {
        get => _logText;
        set => SetProperty(ref _logText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(RunningSummary));
            }
        }
    }

    public string RunningSummary => IsRunning ? "运行中" : "已停止";

    public string RuntimeStateSummary
    {
        get => _runtimeStateSummary;
        private set => SetProperty(ref _runtimeStateSummary, value);
    }

    public int ProtectUpCount => ProtectUps.Count;

    public int CookieAccountCount => CookieAccounts.Count;

    public int ValidCookieAccountCount => CookieAccounts.Count(account => account.HasRequiredFields);

    public int BlacklistCount => _blacklist.Count;

    public string ScanIntervalSummary => $"{_config.ScanIntervalSeconds} 秒";

    public string VideoWindowSummary => $"{_config.VideoWindowSize} 个";

    public string DynamicWindowSummary => $"{_config.DynamicWindowSize} 个";

    public string CommentModeSummary => _config.IncludeSubReplies ? "已配置二级评论，当前版本仍按一级处理" : "仅一级评论";

    public string ReportReasonSummary => _config.Report.Reason.ToString();

    public string ReportContentSummary => string.IsNullOrWhiteSpace(_config.Report.Content) ? "（空）" : _config.Report.Content;

    public string RequestIntervalSummary => $"{_config.RequestIntervalMs.Min} ~ {_config.RequestIntervalMs.Max} 毫秒";

    public string ScanBatchSummary => $"{_config.ScanBatchSizePerCycle} 个";

    public string CommentPageSizeSummary => $"{_config.CommentPageSize} 条";

    public string MinReReportSummary => $"{_config.MinReReportIntervalSeconds} 秒";

    public string AccountCooldownSummary => $"{_config.AccountCooldownSeconds} 秒";

    public string StateFileSummary => ResolveAppRelativePath(FixedStateRelativePath);

    public string ReportHistorySummary => ResolveAppRelativePath(FixedReportHistoryRelativePath);

    public string CycleCountSummary => $"{_metrics.CycleCount} 轮";

    public string ScannedContentSummary => $"{_metrics.ScannedContentCount} 个";

    public string BlacklistHitSummary => $"{_metrics.BlacklistHitCount} 条";

    public string ReportAttemptSummary => $"{_metrics.ReportAttemptCount} 次";

    public string SuccessfulReportSummary => $"{_metrics.SuccessfulReportCount} 次";

    public string LastContentSummary => string.IsNullOrWhiteSpace(_metrics.LastContentLabel) ? "-" : _metrics.LastContentLabel;

    public string LastActivitySummary => _metrics.LastActivityUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

    private void ReloadAll()
    {
        try
        {
            LoadConfig();
            LoadCookies();
            LoadBlacklist();

            StatusMessage = $"加载完成：{ProtectUpCount} 个保护 UP，{CookieAccountCount} 个 Cookie 账号，{BlacklistCount} 个黑名单 UID。";
            AppendLog("重新加载完成。");
        }
        catch (Exception exception)
        {
            StatusMessage = $"加载失败：{exception.Message}";
            AppendLog($"[ERROR] {exception.Message}");
        }

        NotifySummaryChanged();
    }

    private void LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            AppendLog($"[WARN] 未找到 config.yaml: {ConfigPath}");
            _config = new AppConfig();
            ProtectUps.Clear();
            return;
        }

        _config = _configLoader.LoadFromFile(ConfigPath);

        ProtectUps.Clear();
        foreach (var userId in _config.ProtectUps)
        {
            ProtectUps.Add(userId);
        }

        AppendLog($"已加载 config.yaml，保护 UP 数：{_config.ProtectUps.Count}。");
    }

    private void LoadCookies()
    {
        if (!File.Exists(CookiePath))
        {
            AppendLog($"[WARN] 未找到 cookie.json: {CookiePath}");
            CookieAccounts.Clear();
            return;
        }

        var accounts = _cookieParser.LoadFromFile(CookiePath);
        CookieAccounts.Clear();
        foreach (var account in accounts)
        {
            CookieAccounts.Add(account);
        }

        AppendLog($"已加载 cookie.json，账号数：{CookieAccounts.Count}，字段完整账号数：{ValidCookieAccountCount}。");
    }

    private void LoadBlacklist()
    {
        if (!File.Exists(BlacklistPath))
        {
            AppendLog($"[WARN] 未找到 blacklist.txt: {BlacklistPath}");
            _blacklist = Array.Empty<long>();
            return;
        }

        _blacklist = _blacklistLoader.LoadFromFile(BlacklistPath);
        AppendLog($"已加载 blacklist.txt，UID 数：{_blacklist.Count}。");
    }

    private void StartBot()
    {
        if (IsRunning)
        {
            return;
        }

        try
        {
            ReloadAll();
            ValidateBeforeStart();

            _metrics = new BotRunMetrics();
            NotifyRuntimeMetricsChanged();
            RuntimeStateSummary = "启动中";
            IsRunning = true;
            StatusMessage = "机器人正在启动...";
            AppendLog("开始启动机器人。");

            _runnerCts = new CancellationTokenSource();
            var runner = new GuardBotRunner(
                _config,
                CookieAccounts.ToList(),
                _blacklist,
                ResolveAppRelativePath(FixedStateRelativePath),
                ResolveAppRelativePath(FixedReportHistoryRelativePath),
                AppendLog,
                UpdateStatusMessage,
                UpdateCookieRuntimeStatus,
                UpdateMetrics);

            _ = Task.Run(async () =>
            {
                try
                {
                    await runner.RunAsync(_runnerCts.Token);
                }
                catch (OperationCanceledException)
                {
                    AppendLog("机器人已收到停止信号。");
                }
                catch (Exception exception)
                {
                    AppendLog($"[FATAL] 机器人已停止：{exception.Message}");
                    UpdateStatusMessage($"机器人异常退出：{exception.Message}");
                }
                finally
                {
                    RunOnUi(() =>
                    {
                        IsRunning = false;
                        RuntimeStateSummary = "已停止";
                        StatusMessage = string.IsNullOrWhiteSpace(StatusMessage) ? "机器人已停止。" : StatusMessage;
                    });
                }
            });
        }
        catch (Exception exception)
        {
            IsRunning = false;
            RuntimeStateSummary = "启动失败";
            StatusMessage = $"启动失败：{exception.Message}";
            AppendLog($"[ERROR] 启动失败：{exception.Message}");
        }
    }

    private void StopBot()
    {
        if (!IsRunning)
        {
            return;
        }

        RuntimeStateSummary = "停止中";
        StatusMessage = "正在停止机器人...";
        AppendLog("正在停止机器人。");
        _runnerCts?.Cancel();
    }

    private void CreateSampleFiles()
    {
        try
        {
            WriteFileIfMissing(ConfigPath, _configLoader.CreateSampleConfig());
            WriteFileIfMissing(CookiePath, CreateSampleCookieJson());
            WriteFileIfMissing(BlacklistPath, "# 评论作者 UID，每行一个\n123456\n987654\n");

            AppendLog("示例文件已生成。");
            ReloadAll();
        }
        catch (Exception exception)
        {
            StatusMessage = $"生成示例文件失败：{exception.Message}";
            AppendLog($"[ERROR] {exception.Message}");
        }
    }

    private void BrowseFile(FileKind fileKind)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = false,
            Multiselect = false
        };

        switch (fileKind)
        {
            case FileKind.Config:
                dialog.Filter = "YAML files (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*";
                dialog.FileName = Path.GetFileName(ConfigPath);
                break;
            case FileKind.Cookie:
                dialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                dialog.FileName = Path.GetFileName(CookiePath);
                break;
            case FileKind.Blacklist:
                dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.FileName = Path.GetFileName(BlacklistPath);
                break;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        switch (fileKind)
        {
            case FileKind.Config:
                ConfigPath = dialog.FileName;
                break;
            case FileKind.Cookie:
                CookiePath = dialog.FileName;
                break;
            case FileKind.Blacklist:
                BlacklistPath = dialog.FileName;
                break;
        }
    }

    private void UpdateStatusMessage(string message)
    {
        RunOnUi(() =>
        {
            StatusMessage = message;
            RuntimeStateSummary = IsRunning ? "运行中" : RuntimeStateSummary;
        });
    }

    private void UpdateCookieRuntimeStatus(CookieAccount account, string status, string detail)
    {
        RunOnUi(() =>
        {
            account.RuntimeStatus = status;
            account.RuntimeDetail = detail;
        });
    }

    private void UpdateMetrics(BotRunMetrics metrics)
    {
        RunOnUi(() =>
        {
            _metrics = metrics;
            RuntimeStateSummary = IsRunning ? "运行中" : RuntimeStateSummary;
            NotifyRuntimeMetricsChanged();
        });
    }

    private void ValidateBeforeStart()
    {
        if (_config.ProtectUps.Count == 0)
        {
            throw new InvalidOperationException("config.yaml 中没有“保护UP主UID列表”。");
        }

        if (CookieAccounts.Count == 0)
        {
            throw new InvalidOperationException("cookie.json 中没有可解析的 Cookie 账号。");
        }

        if (CookieAccounts.All(account => !account.HasRequiredFields))
        {
            throw new InvalidOperationException("所有 Cookie 账号都缺少必要字段，至少需要 SESSDATA、bili_jct、DedeUserID。");
        }
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(ProtectUpCount));
        OnPropertyChanged(nameof(CookieAccountCount));
        OnPropertyChanged(nameof(ValidCookieAccountCount));
        OnPropertyChanged(nameof(BlacklistCount));
        OnPropertyChanged(nameof(ScanIntervalSummary));
        OnPropertyChanged(nameof(VideoWindowSummary));
        OnPropertyChanged(nameof(DynamicWindowSummary));
        OnPropertyChanged(nameof(CommentModeSummary));
        OnPropertyChanged(nameof(ReportReasonSummary));
        OnPropertyChanged(nameof(ReportContentSummary));
        OnPropertyChanged(nameof(RequestIntervalSummary));
        OnPropertyChanged(nameof(ScanBatchSummary));
        OnPropertyChanged(nameof(CommentPageSizeSummary));
        OnPropertyChanged(nameof(MinReReportSummary));
        OnPropertyChanged(nameof(AccountCooldownSummary));
        OnPropertyChanged(nameof(StateFileSummary));
        OnPropertyChanged(nameof(ReportHistorySummary));
    }

    private void NotifyRuntimeMetricsChanged()
    {
        OnPropertyChanged(nameof(RunningSummary));
        OnPropertyChanged(nameof(CycleCountSummary));
        OnPropertyChanged(nameof(ScannedContentSummary));
        OnPropertyChanged(nameof(BlacklistHitSummary));
        OnPropertyChanged(nameof(ReportAttemptSummary));
        OnPropertyChanged(nameof(SuccessfulReportSummary));
        OnPropertyChanged(nameof(LastContentSummary));
        OnPropertyChanged(nameof(LastActivitySummary));
    }

    private void AppendLog(string message)
    {
        RunOnUi(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogText = string.IsNullOrWhiteSpace(LogText)
                ? line
                : $"{LogText}{Environment.NewLine}{line}";
        });
    }

    private static string ResolveAppRelativePath(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath));
    }

    private void RunOnUi(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private void RaiseCommandStates()
    {
        BrowseConfigCommand.RaiseCanExecuteChanged();
        BrowseCookieCommand.RaiseCanExecuteChanged();
        BrowseBlacklistCommand.RaiseCanExecuteChanged();
        ReloadAllCommand.RaiseCanExecuteChanged();
        CreateSampleFilesCommand.RaiseCanExecuteChanged();
        StartBotCommand.RaiseCanExecuteChanged();
        StopBotCommand.RaiseCanExecuteChanged();
    }

    private static void WriteFileIfMissing(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(path))
        {
            File.WriteAllText(path, content);
        }
    }

    private static string CreateSampleCookieJson()
    {
        var sample = new[]
        {
            "SESSDATA=your_sessdata; bili_jct=your_csrf; DedeUserID=111111; DedeUserID__ckMd5=your_md5; sid=your_sid",
            "SESSDATA=your_sessdata_2; bili_jct=your_csrf_2; DedeUserID=222222; DedeUserID__ckMd5=your_md5_2; sid=your_sid_2"
        };

        return JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
    }

    private enum FileKind
    {
        Config,
        Cookie,
        Blacklist
    }
}


