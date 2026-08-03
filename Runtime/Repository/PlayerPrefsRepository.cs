using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.DataPersistence
{
    /// <summary>
    /// <see cref="PlayerPrefs"/>-backed repository. Stores byte payloads as Base64
    /// strings keyed by <c>{_keyPrefix}{key}</c>. A separate index entry
    /// (<c>{_keyPrefix}__index</c>) tracks all written keys so <see cref="ClearAsync"/>
    /// removes only this repository's data instead of <c>PlayerPrefs.DeleteAll()</c>.
    ///
    /// <para>
    /// <see cref="PlayerPrefs"/> is synchronous and main-thread only — all methods
    /// complete immediately without thread-pool switches. Not suitable for large
    /// payloads (platform size limits: Windows registry on Standalone, ~1 MB on
    /// WebGL).
    /// </para>
    ///
    /// <para>Marked <see cref="SupportsEditorIOAttribute"/> — works in Edit/Play Mode.</para>
    /// </summary>
    [SupportsEditorIO]
    public class PlayerPrefsRepository : ARepository
    {
        [SerializeField] private string _keyPrefix = "Data/";

        private string IndexKey              => _keyPrefix + "__index";
        private string PrefixedKey(string k) => _keyPrefix + k;

        public override UniTask<byte[]> LoadAsync(string key)
        {
            var prefKey = PrefixedKey(key);
            if (!PlayerPrefs.HasKey(prefKey)) return UniTask.FromResult<byte[]>(null);

            var b64 = PlayerPrefs.GetString(prefKey);
            if (string.IsNullOrEmpty(b64)) return UniTask.FromResult(Array.Empty<byte>());

            try
            {
                return UniTask.FromResult(Convert.FromBase64String(b64));
            }
            catch (FormatException e)
            {
                // Throw rather than return null: null means "nothing saved yet" and would
                // make the entry seed defaults and overwrite the damaged value on next save.
                throw new InvalidDataException($"[{nameof(PlayerPrefsRepository)}] '{key}' is not valid Base64 — value is corrupt.", e);
            }
        }

        public override UniTask SaveAsync(string key, byte[] bytes)
        {
            // No null-coalesce here on purpose: DataBundle never passes null, and quietly
            // writing an empty payload over a good value would be data loss dressed as safety.
            PlayerPrefs.SetString(PrefixedKey(key), Convert.ToBase64String(bytes));
            AddToIndex(key);
            PlayerPrefs.Save();
            return UniTask.CompletedTask;
        }

        public override UniTask DeleteAsync(string key)
        {
            PlayerPrefs.DeleteKey(PrefixedKey(key));
            RemoveFromIndex(key);
            PlayerPrefs.Save();
            return UniTask.CompletedTask;
        }

        public override UniTask ClearAsync()
        {
            foreach (var key in ReadIndex()) PlayerPrefs.DeleteKey(PrefixedKey(key));
            PlayerPrefs.DeleteKey(IndexKey);
            PlayerPrefs.Save();
            return UniTask.CompletedTask;
        }

        // ------------------------------ Index helpers ------------------------------

        // Unit Separator (U+001F) — safe delimiter: it is a control character, and
        // DataBundle rejects control characters in storage keys, so it cannot appear
        // inside a key and split one in half.
        private const char IndexSeparator = '\x1F';

        private HashSet<string> ReadIndex()
        {
            var raw = PlayerPrefs.GetString(IndexKey, string.Empty);
            return raw.Length == 0
                ? new HashSet<string>()
                : new HashSet<string>(raw.Split(IndexSeparator));
        }

        private void WriteIndex(HashSet<string> keys)
        {
            if (keys.Count == 0) PlayerPrefs.DeleteKey(IndexKey);
            else                 PlayerPrefs.SetString(IndexKey, string.Join(IndexSeparator.ToString(), keys));
        }

        private void AddToIndex(string key)
        {
            var index = ReadIndex();
            if (index.Add(key)) WriteIndex(index);
        }

        private void RemoveFromIndex(string key)
        {
            var index = ReadIndex();
            if (index.Remove(key)) WriteIndex(index);
        }
    }
}
