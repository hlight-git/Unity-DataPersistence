using System;
using UnityEngine;

namespace Hlight.DataPersistence
{
    /// <summary>
    /// <see cref="DataEntry{T}"/> variant that carries its own <c>[SerializeField]</c>
    /// default value edited in the Inspector. On first run (no bytes), <c>CreateDefault()</c>
    /// returns a clone of that template instead of <c>new T()</c> — useful for seeding
    /// tuned starter state (starter inventory, difficulty defaults, feature-flag
    /// baselines) without writing code.
    ///
    /// <para>
    /// Cloning uses <see cref="JsonUtility"/> round-trip, which supports the same
    /// types Unity's built-in serializer supports. Override <see cref="DataEntry{T}.CreateDefault"/>
    /// if your model uses Odin-serialized fields (dictionaries, polymorphic lists,
    /// etc.) that <see cref="JsonUtility"/> cannot round-trip.
    /// </para>
    /// </summary>
    [Serializable]
    public class DefaultDataEntry<T> : DataEntry<T> where T : class, new()
    {
        [SerializeField] private T _defaultValue;

        /// <summary>
        /// Returns a deep clone of <see cref="_defaultValue"/>, or <c>new T()</c>
        /// when the template is null (user forgot to populate it in Inspector).
        /// </summary>
        protected override T CreateDefault()
            => _defaultValue == null
                ? new T()
                : JsonUtility.FromJson<T>(JsonUtility.ToJson(_defaultValue));
    }
}
