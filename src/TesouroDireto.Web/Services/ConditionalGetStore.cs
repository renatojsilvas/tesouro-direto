using System.Collections.Concurrent;

namespace TesouroDireto.Web.Services;

public sealed record ConditionalCacheEntry(string ETag, byte[] Body, IReadOnlyDictionary<string, string> Headers);

public interface IConditionalGetStore
{
    bool TryGet(string uri, out ConditionalCacheEntry? entry);
    void Set(string uri, ConditionalCacheEntry entry);
}

public sealed class BoundedConditionalGetStore : IConditionalGetStore
{
    private const int Cap = 128;

    private readonly ConcurrentDictionary<string, (ConditionalCacheEntry Entry, long Seq)> _entries = new();
    private readonly object _evictionLock = new();
    private long _sequence;

    public bool TryGet(string uri, out ConditionalCacheEntry? entry)
    {
        if (_entries.TryGetValue(uri, out var stored))
        {
            entry = stored.Entry;
            return true;
        }

        entry = null;
        return false;
    }

    public void Set(string uri, ConditionalCacheEntry entry)
    {
        var seq = Interlocked.Increment(ref _sequence);
        _entries[uri] = (entry, seq);

        if (_entries.Count <= Cap)
        {
            return;
        }

        lock (_evictionLock)
        {
            while (_entries.Count > Cap)
            {
                var oldestKey = _entries
                    .OrderBy(kvp => kvp.Value.Seq)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault();

                if (oldestKey is null)
                {
                    break;
                }

                _entries.TryRemove(oldestKey, out _);
            }
        }
    }
}
