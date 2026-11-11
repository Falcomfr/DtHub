namespace DtHub.Core.IPhone;

/// <summary>
/// An iOS account's playtime, in spans: one starts when its window counts
/// (<see cref="IosWindowLife.CountsPlaytime"/>), and ends when it no longer
/// does, unplugged, failed or closed.
/// </summary>
public sealed class IosPlaytime
{
    private readonly Dictionary<string, DateTimeOffset> _since = new(StringComparer.Ordinal);

    /// <param name="counting">The accounts whose window counts now.</param>
    /// <returns>The spans that ended, in whole seconds.</returns>
    public IReadOnlyList<(string Key, int Seconds)> Update(IReadOnlyCollection<string> counting, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(counting);

        List<(string Key, int Seconds)> ended = [];

        foreach (var (key, since) in _since.Where(s => !counting.Contains(s.Key)).ToList())
        {
            _since.Remove(key);
            ended.Add((key, (int)(now - since).TotalSeconds));
        }

        foreach (var key in counting)
        {
            _since.TryAdd(key, now);
        }

        return ended;
    }
}
