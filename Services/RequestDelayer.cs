namespace BiliCommunityGuard.App.Services;

public sealed class RequestDelayer
{
    private readonly int _minMilliseconds;
    private readonly int _maxMilliseconds;

    public RequestDelayer(int minMilliseconds, int maxMilliseconds)
    {
        _minMilliseconds = Math.Min(minMilliseconds, maxMilliseconds);
        _maxMilliseconds = Math.Max(minMilliseconds, maxMilliseconds);
    }

    public async Task DelayAsync(CancellationToken cancellationToken)
    {
        if (_maxMilliseconds <= 0)
        {
            return;
        }

        var delay = Random.Shared.Next(_minMilliseconds, _maxMilliseconds + 1);
        await Task.Delay(delay, cancellationToken);
    }
}
