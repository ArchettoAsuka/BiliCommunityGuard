using System.Text.Json;
using System.Net.Http;
using BiliCommunityGuard.App.Models;

namespace BiliCommunityGuard.App.Services;

public sealed class AccountManager
{
    private readonly IReadOnlyList<CookieAccount> _cookieAccounts;
    private readonly BiliApiClient _apiClient;
    private readonly Action<string> _log;
    private readonly Action<CookieAccount, string, string>? _statusUpdater;
    private readonly List<AccountSession> _sessions = [];
    private int _rotationIndex;

    public AccountManager(IReadOnlyList<CookieAccount> cookieAccounts, BiliApiClient apiClient, Action<string> log, Action<CookieAccount, string, string>? statusUpdater = null)
    {
        _cookieAccounts = cookieAccounts;
        _apiClient = apiClient;
        _log = log;
        _statusUpdater = statusUpdater;
    }

    public IReadOnlyList<AccountSession> Sessions => _sessions;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _sessions.Clear();

        foreach (var cookieAccount in _cookieAccounts)
        {
            if (!cookieAccount.HasRequiredFields)
            {
                UpdateStatus(cookieAccount, "字段缺失", cookieAccount.MissingFieldsSummary);
                continue;
            }

            var session = new AccountSession(cookieAccount, _apiClient.CreateHttpClient());
            _sessions.Add(session);
            await ValidateAsync(session, cancellationToken);
        }
    }

    public AccountSession? GetFetchAccount()
    {
        return _sessions.FirstOrDefault(session => session.IsAvailableForFetch);
    }

    public IReadOnlyList<AccountSession> GetReportAccounts()
    {
        var available = _sessions.Where(session => session.IsAvailableForReport).ToList();
        if (available.Count == 0)
        {
            return available;
        }

        var ordered = new List<AccountSession>(available.Count);
        for (var index = 0; index < available.Count; index++)
        {
            ordered.Add(available[(_rotationIndex + index) % available.Count]);
        }

        _rotationIndex = (_rotationIndex + 1) % available.Count;
        return ordered;
    }

    public void MarkCooldown(AccountSession session, TimeSpan duration, string reason)
    {
        session.MarkCooldown(duration);
        UpdateStatus(session.CookieAccount, "冷却中", reason);
        _log($"账号 {session.AccountId} 进入冷却，原因：{reason}");
    }

    public void MarkInvalid(AccountSession session, string reason)
    {
        session.MarkInvalid();
        UpdateStatus(session.CookieAccount, "已失效", reason);
        _log($"账号 {session.AccountId} 已标记失效，原因：{reason}");
    }

    private async Task ValidateAsync(AccountSession session, CancellationToken cancellationToken)
    {
        try
        {
            using var json = await _apiClient.GetJsonAsync(session, "https://api.bilibili.com/x/web-interface/nav", null, cancellationToken);
            var root = json.RootElement;
            var code = root.TryGetInt32ByPath("code") ?? -1;
            var data = root.TryGetPropertyValue("data", out var dataElement) ? dataElement : default;
            var isLogin = data.ValueKind != default && (data.TryGetPropertyValue("isLogin", out var loginElement) && loginElement.ValueKind == JsonValueKind.True);

            if (code == 0 && isLogin)
            {
                var mid = data.TryGetInt64ByPath("mid");
                var userName = data.TryGetStringByPath("uname") ?? string.Empty;
                session.MarkValidated(mid, userName);
                UpdateStatus(session.CookieAccount, "可用", $"mid={mid}, uname={userName}");
                _log($"账号验证成功：{session.AccountId} ({userName})");
                return;
            }

            session.MarkInvalid();
            UpdateStatus(session.CookieAccount, "登录失效", $"code={code}");
            _log($"账号验证失败：{session.CookieAccount.DisplayName}，code={code}");
        }
        catch (Exception exception)
        {
            session.MarkInvalid();
            UpdateStatus(session.CookieAccount, "校验失败", exception.Message);
            _log($"账号校验异常：{session.CookieAccount.DisplayName}，{exception.Message}");
        }
    }

    private void UpdateStatus(CookieAccount account, string status, string detail)
    {
        if (_statusUpdater is not null)
        {
            _statusUpdater(account, status, detail);
            return;
        }

        account.RuntimeStatus = status;
        account.RuntimeDetail = detail;
    }
}

