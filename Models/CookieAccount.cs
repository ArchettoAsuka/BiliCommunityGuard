using BiliCommunityGuard.App.Infrastructure;

namespace BiliCommunityGuard.App.Models;

public sealed class CookieAccount : ObservableObject
{
    private string _runtimeStatus = "未校验";
    private string _runtimeDetail = string.Empty;

    public int Index { get; init; }

    public string RawCookie { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();

    public string DisplayName
    {
        get
        {
            if (Fields.TryGetValue("DedeUserID", out var userId) && !string.IsNullOrWhiteSpace(userId))
            {
                return userId;
            }

            return $"account-{Index}";
        }
    }

    public bool HasRequiredFields => MissingFields.Count == 0;

    public bool HasCsrf => Fields.ContainsKey("bili_jct");

    public string RuntimeStatus
    {
        get => _runtimeStatus;
        set
        {
            if (SetProperty(ref _runtimeStatus, value))
            {
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public string RuntimeDetail
    {
        get => _runtimeDetail;
        set => SetProperty(ref _runtimeDetail, value);
    }

    public string Status => HasRequiredFields ? RuntimeStatus : "字段缺失";

    public string MissingFieldsSummary => MissingFields.Count == 0 ? "-" : string.Join(", ", MissingFields);

    public string MaskedCookie
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RawCookie))
            {
                return "(empty)";
            }

            const int visibleLength = 32;
            return RawCookie.Length <= visibleLength
                ? RawCookie
                : $"{RawCookie[..visibleLength]}...";
        }
    }
}
