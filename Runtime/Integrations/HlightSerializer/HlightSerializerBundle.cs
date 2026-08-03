using System;
using Hlight.Serialization.Serializer;
using UnityEngine;

namespace Hlight.DataPersistence.Integrations
{
    /// <summary>
    /// Integration bridge between <see cref="DataBundle"/> and the
    /// <c>com.hlight.serialization</c> package: a bundle that delegates
    /// serialization to an <see cref="ASerializationAsset"/> assigned in the
    /// Inspector. The asset chooses the format (JSON, binary, Odin, etc.) so
    /// the bundle stays format-agnostic.
    ///
    /// <para>
    /// This type only compiles when the <c>com.hlight.serialization</c> package
    /// is installed (gated by the <c>HLIGHT_SERIALIZATION</c> define constraint
    /// on the surrounding asmdef). The core <c>Hlight.DataPersistence</c>
    /// package never depends on it.
    /// </para>
    ///
    /// <para>
    /// Subclass and declare <see cref="IDataEntry"/> properties — no need to
    /// override <see cref="DataBundle.Serialize"/> / <see cref="DataBundle.Deserialize"/>:
    /// <code>
    /// [Serializable]
    /// public class GameSaveBundle : HlightSerializerBundle
    /// {
    ///     [field: SerializeField] public DataEntry&lt;PlayerProgress&gt; Player { get; private set; }
    /// }
    /// </code>
    /// </para>
    /// </summary>
    [Serializable]
    public class HlightSerializerBundle : DataBundle
    {
        [SerializeField] private ASerializationAsset _serializer;

        protected override byte[] Serialize(object obj)
        {
            EnsureSerializerAssigned();
            return _serializer.SerializeToBytes(obj);
        }

        protected override object Deserialize(byte[] bytes, Type type)
        {
            EnsureSerializerAssigned();
            return _serializer.Deserialize(bytes, type);
        }

        private void EnsureSerializerAssigned()
        {
            if (_serializer == null)
                throw new InvalidOperationException(
                    $"[{GetType().Name}] {nameof(_serializer)} ({nameof(ASerializationAsset)}) is not assigned in the Inspector.");
        }
    }
}
