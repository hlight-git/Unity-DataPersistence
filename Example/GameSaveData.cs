using System;
using UnityEngine;

namespace Hlight.DataPersistence.Example
{
    /// <summary>
    /// Player progress persisted between sessions: levels cleared, stars, currency.
    /// Stored under key <c>"PlayerSave"</c> in <see cref="GameSaveBundle"/>.
    /// </summary>
    [Serializable]
    public class PlayerSaveData
    {
        public int   SchemaVersion;
        public int   HighestLevelCleared;
        public int   TotalStarsEarned;
        public int   SoftCurrency;
        public float TotalPlaytimeSeconds;
    }

    /// <summary>
    /// Persistent player-controlled settings. Uses <see cref="DefaultDataEntry{T}"/>
    /// so Inspector-tuned defaults are applied on first launch without any code.
    /// Stored under key <c>"Settings"</c> in <see cref="GameSaveBundle"/>.
    /// </summary>
    [Serializable]
    public class GameSettings
    {
        public float  MusicVolume    = 1f;
        public float  SfxVolume      = 1f;
        public bool   HapticsEnabled = true;
        public string Language       = "en";
    }
}
