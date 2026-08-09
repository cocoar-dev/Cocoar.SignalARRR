using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cocoar.Reflectensions;
using Microsoft.Extensions.Primitives;

namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// The attributes a client attached to its connection — <c>#</c>-prefixed request headers and
    /// <c>@</c>-prefixed query parameters, read once while the connection is being established.
    /// </summary>
    /// <remarks>
    /// This used to derive from <see cref="Dictionary{TKey,TValue}"/> and hide the inherited indexer
    /// with a <c>new</c> one returning <see cref="string"/>. The two disagreed on the result type and
    /// on what a missing key means, so the same lookup answered differently depending on whether it
    /// went through <c>ClientAttributes</c> or through the dictionary it happened to inherit from —
    /// and inheriting also exposed <c>Add</c>, <c>Remove</c> and <c>Clear</c> on what is a read model
    /// of the incoming request. Wrapping instead of inheriting leaves one indexer with one behaviour.
    /// </remarks>
    public sealed class ClientAttributes : IReadOnlyDictionary<string, StringValues> {

        private readonly Dictionary<string, StringValues> _values =
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The values stored under <paramref name="key"/>.
        /// </summary>
        /// <exception cref="KeyNotFoundException">
        /// No such attribute. Use <see cref="GetString"/>, <see cref="TryGetValue"/> or
        /// <see cref="Has(string)"/> for a lookup that tolerates a miss.
        /// </exception>
        public StringValues this[string key] => _values[key];

        public IEnumerable<string> Keys => _values.Keys;

        public IEnumerable<StringValues> Values => _values.Values;

        public int Count => _values.Count;

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public bool TryGetValue(string key, out StringValues value) => _values.TryGetValue(key, out value);

        /// <summary>
        /// The value stored under <paramref name="key"/> as a string, or <c>null</c> if there is none.
        /// This is what the old <c>attributes[key]</c> returned.
        /// </summary>
        public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value : default;

        public bool Has(string key) => _values.ContainsKey(key);

        public bool Has(string key, string value) =>
            _values.TryGetValue(key, out var stored) && stored.Any(v => v != null && v.Match(value));

        internal void Set(string key, StringValues value) => _values[key] = value;

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

}
