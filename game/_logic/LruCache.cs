using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// Caché LRU genérico con capacidad máxima fija.
    /// Thread-safe mediante lock externo (el caller es responsable).
    /// </summary>
    internal sealed class LruCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey key, TValue value)>> _map;
        private readonly LinkedList<(TKey key, TValue value)> _order;

        public LruCache(int capacity)
        {
            _capacity = capacity;
            _map   = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
            _order = new LinkedList<(TKey, TValue)>();
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.value;
                return true;
            }
            value = default;
            return false;
        }

        public void Put(TKey key, TValue value)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }
            else if (_map.Count >= _capacity)
            {
                var lru = _order.Last;
                _order.RemoveLast();
                _map.Remove(lru.Value.key);
            }
            var node = _order.AddFirst((key, value));
            _map[key] = node;
        }

        public int Count => _map.Count;
    }
}