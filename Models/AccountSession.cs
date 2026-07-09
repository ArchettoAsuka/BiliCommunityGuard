using System.Net.Http;
namespace BiliCommunityGuard.App.Models;

public sealed class AccountSession
{
    public AccountSession(CookieAccount cookieAccount, HttpClient httpClient)
    {
        CookieAccount = cookieAccount;
        HttpClient = httpClient;
    }

    public CookieAccount CookieAccount { get; }

    public HttpClient HttpClient { get; }

    public long? Mid { get; private set; }

    public string? UserName { get; private set; }

    public bool IsValid { get; private set; }

    public DateTimeOffset CooldownUntilUtc { get; private set; } = DateTimeOffset.MinValue;

    public string RawCookie => CookieAccount.RawCookie;

    public IReadOnlyDictionary<string, string> Fields => CookieAccount.Fields;

    public string AccountId => Mid?.ToString() ?? CookieAccount.DisplayName;

    public bool IsAvailableForFetch => IsValid;

    public bool IsAvailableForReport => IsValid && DateTimeOffset.UtcNow >= CooldownUntilUtc;

    public string Csrf => Fields.TryGetValue("bili_jct", out var value) ? value : string.Empty;

    public void MarkValidated(long? mid, string? userName)
    {
        Mid = mid;
        UserName = userName;
        IsValid = true;
    }

    public void MarkInvalid()
    {
        IsValid = false;
    }

    public void MarkCooldown(TimeSpan duration)
    {
        CooldownUntilUtc = DateTimeOffset.UtcNow.Add(duration);
    }
}

