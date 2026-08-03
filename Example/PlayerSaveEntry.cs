using System;
using UnityEngine;

namespace Hlight.DataPersistence.Example
{
    /// <summary>
    /// Typed entry for <see cref="PlayerSaveData"/>. Handles forward schema migrations
    /// and stamps <see cref="PlayerSaveData.SchemaVersion"/> on every write.
    ///
    /// <para>
    /// <b>How to add a new field migration:</b>
    /// <list type="number">
    ///   <item>Add the field to <see cref="PlayerSaveData"/> with a safe default initializer.</item>
    ///   <item>Add a migration block (see below) targeting the next schema version.</item>
    ///   <item>Increment <see cref="LatestSchemaVersion"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    [Serializable]
    public class PlayerSaveEntry : DataEntry<PlayerSaveData>
    {
        private const int LatestSchemaVersion = 1;

        /// <summary>
        /// Deserializes raw bytes then applies any pending forward migrations.
        /// Each version block is independent and safe to re-run on already-migrated data.
        /// </summary>
        protected override PlayerSaveData Deserialize(
            byte[] bytes,
            Func<byte[], Type, object> deserializeFunc)
        {
            var data = (PlayerSaveData)deserializeFunc(bytes, typeof(PlayerSaveData));

            // v0 → v1: TotalPlaytimeSeconds field was introduced.
            // JsonUtility defaults missing fields to 0f — no manual seeding needed.
            if (data.SchemaVersion < 1)
            {
                data.SchemaVersion = 1;
            }

            // Add the next migration block here when LatestSchemaVersion is bumped.

            return data;
        }

        /// <summary>
        /// Writes the current schema version into the payload before serializing
        /// so the save file is always stamped with the version that wrote it.
        /// </summary>
        protected override byte[] Serialize(Func<object, byte[]> serializeFunc)
        {
            CurrentValue.SchemaVersion = LatestSchemaVersion;
            return serializeFunc(CurrentValue);
        }
    }
}
