using System;
using System.Text;
using UnityEngine;

namespace Hlight.DataPersistence.Example
{
    /// <summary>
    /// Concrete save bundle for the game. Serializes all entries with
    /// <see cref="JsonUtility"/> (UTF-8 JSON bytes).
    ///
    /// <para>
    /// Host this as a <c>[SerializeField]</c> on <see cref="GameSaveAsset"/>
    /// and assign a repository in the Inspector. Typical runtime usage:
    /// <code>
    /// await _bundle.LoadAllAsync();
    /// _bundle.PlayerSave.CurrentValue.SoftCurrency += 50;
    /// await _bundle.SaveAllAsync();
    /// </code>
    /// </para>
    /// </summary>
    [Serializable]
    public class GameSaveBundle : DataBundle
    {
        /// <summary>
        /// Player progress. The [DataKey] pins the name on disk, so this property can be
        /// renamed later without orphaning saves already written — declare it on every
        /// entry from day one, even when it matches the property name.
        /// </summary>
        [DataKey("PlayerSave")] [field: SerializeField]
        public PlayerSaveEntry PlayerSave { get; private set; }

        /// <summary>
        /// Game settings. Seeded from the Inspector-tuned <c>Default Value</c> on first launch.
        /// </summary>
        [DataKey("Settings")] [field: SerializeField]
        public DefaultDataEntry<GameSettings> Settings { get; private set; }

        protected override byte[] Serialize(object obj)
            => Encoding.UTF8.GetBytes(JsonUtility.ToJson(obj));

        protected override object Deserialize(byte[] bytes, Type type)
            => JsonUtility.FromJson(Encoding.UTF8.GetString(bytes), type);
    }
}
