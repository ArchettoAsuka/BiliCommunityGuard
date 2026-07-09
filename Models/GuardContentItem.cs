namespace BiliCommunityGuard.App.Models;

public enum BiliContentSourceType
{
    Video,
    Dynamic
}

public sealed class GuardContentItem
{
    public BiliContentSourceType SourceType { get; init; }

    public long UpMid { get; init; }

    public string ContentId { get; init; } = string.Empty;

    public long Oid { get; init; }

    public int CommentType { get; init; }

    public string Title { get; init; } = string.Empty;

    public long PublishedAtUnixSeconds { get; init; }

    public string ContentKey => $"{SourceType}:{UpMid}:{ContentId}:{CommentType}:{Oid}";

    public string DisplayLabel => $"{SourceType} | UP {UpMid} | {Title} | oid={Oid} | type={CommentType}";
}
