namespace ScreenTranslator.Core;

/// <summary>A thread-safe, in-memory least-recently-used cache. It never writes recognized text to disk.</summary>
public sealed class TranslationCache
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, string>>> _entries =
        new(StringComparer.Ordinal);
    private readonly LinkedList<KeyValuePair<string, string>> _recency = new();

    public TranslationCache(int capacity = 32)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Cache capacity must be positive.");
        _capacity = capacity;
    }

    public bool TryGet(string text, out string translation)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            if (!_entries.TryGetValue(text, out var node))
            {
                translation = string.Empty;
                return false;
            }
            _recency.Remove(node);
            _recency.AddFirst(node);
            translation = node.Value.Value;
            return true;
        }
    }

    public void Store(string text, string translation)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(translation);
        lock (_gate)
        {
            if (_entries.TryGetValue(text, out var existing))
            {
                existing.Value = new KeyValuePair<string, string>(text, translation);
                _recency.Remove(existing);
                _recency.AddFirst(existing);
                return;
            }

            var node = _recency.AddFirst(new KeyValuePair<string, string>(text, translation));
            _entries.Add(text, node);
            if (_entries.Count > _capacity)
            {
                var oldest = _recency.Last!;
                _entries.Remove(oldest.Value.Key);
                _recency.RemoveLast();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recency.Clear();
        }
    }
}
