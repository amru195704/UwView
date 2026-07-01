namespace UwView.Core;

/// <summary>直近アクセス項目を保持する単純な LRU キャッシュ（UI スレッド前提・非スレッドセーフ）。</summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();

    public LruCache(int capacity)
    {
        _capacity = Math.Max(1, capacity);
        _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(_capacity);
    }

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            existing.Value = (key, value);
            _order.Remove(existing);
            _order.AddFirst(existing);
            return;
        }

        if (_map.Count >= _capacity)
        {
            var last = _order.Last;
            if (last is not null)
            {
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }

        var node = new LinkedListNode<(TKey, TValue)>((key, value));
        _order.AddFirst(node);
        _map[key] = node;
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
    }
}
