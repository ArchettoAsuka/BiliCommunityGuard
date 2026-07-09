namespace BiliCommunityGuard.App.Models;

public sealed class BiliComment
{
    public long Rpid { get; init; }

    public long AuthorMid { get; init; }

    public string AuthorName { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public long CreatedAtUnixSeconds { get; init; }

    public string CommentKey(int commentType, long oid) => $"{commentType}:{oid}:{Rpid}";

    public string Summary => $"rpid={Rpid}, uid={AuthorMid}, uname={AuthorName}";
}
