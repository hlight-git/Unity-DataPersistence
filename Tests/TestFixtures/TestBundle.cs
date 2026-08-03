using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.DataPersistence.Tests
{
    /// <summary>Plain model used across the bundle tests.</summary>
    [Serializable]
    public class Score
    {
        public int Value;
    }

    /// <summary>
    /// In-memory <see cref="ARepository"/>. Records every write so tests can assert
    /// what was (and was not) persisted, and can be primed with corrupt payloads.
    /// </summary>
    public class FakeRepository : ARepository
    {
        public readonly Dictionary<string, byte[]> Store       = new();
        public readonly List<string>               SaveCalls   = new();
        public readonly HashSet<string>            CorruptKeys = new();

        public override UniTask<byte[]> LoadAsync(string key)
        {
            if (CorruptKeys.Contains(key))
                throw new System.IO.InvalidDataException($"fake corruption for '{key}'");
            return UniTask.FromResult(Store.GetValueOrDefault(key));
        }

        public override UniTask SaveAsync(string key, byte[] bytes)
        {
            SaveCalls.Add(key);
            Store[key] = bytes;
            return UniTask.CompletedTask;
        }

        public override UniTask DeleteAsync(string key)
        {
            Store.Remove(key);
            return UniTask.CompletedTask;
        }

        public override UniTask ClearAsync()
        {
            Store.Clear();
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Entry whose <see cref="Serialize"/> always throws — models a broken migration hook.</summary>
    [Serializable]
    public class ThrowingEntry : DataEntry<Score>
    {
        protected override byte[] Serialize(Func<object, byte[]> serializeFunc)
            => throw new InvalidOperationException("serialize blew up");
    }

    /// <summary>Shared JsonUtility serialization for the test bundles.</summary>
    [Serializable]
    public abstract class JsonTestBundle : DataBundle
    {
        protected override byte[] Serialize(object obj)
            => Encoding.UTF8.GetBytes(JsonUtility.ToJson(obj));

        protected override object Deserialize(byte[] bytes, Type type)
            => JsonUtility.FromJson(Encoding.UTF8.GetString(bytes), type);
    }

    /// <summary>
    /// Two entries, the throwing one declared first so a failure in it would abort the
    /// second if <c>SaveAllAsync</c> did not isolate per key. No <c>[DataKey]</c> here —
    /// exercises the property-name fallback.
    /// </summary>
    [Serializable]
    public class TestBundle : JsonTestBundle
    {
        [field: SerializeField] public ThrowingEntry    Broken { get; private set; }
        [field: SerializeField] public DataEntry<Score> Good   { get; private set; }
    }

    /// <summary>
    /// Storage key pinned by <see cref="DataKeyAttribute"/> and deliberately different from
    /// the property name — stands in for a property that has been renamed since it shipped.
    /// </summary>
    [Serializable]
    public class KeyedBundle : JsonTestBundle
    {
        [DataKey("legacy_key")] [field: SerializeField]
        public DataEntry<Score> RenamedSinceRelease { get; private set; }
    }
}
