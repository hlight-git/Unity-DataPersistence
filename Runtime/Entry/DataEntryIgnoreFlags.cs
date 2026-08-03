using System;

namespace Hlight.DataPersistence
{
    /// <summary>
    /// Editor-only flags that suppress lifecycle hooks on a <see cref="DataEntry{T}"/>.
    /// Serialized so values survive domain reload — set before entering Play Mode, active for
    /// that session. Independent: pick whichever half of the cycle you want stopped.
    /// </summary>
    [Flags]
    public enum DataEntryIgnoreFlags
    {
        None = 0,

        /// <summary>
        /// Skip the repository read. <see cref="DataEntry{T}.CurrentValue"/> is left exactly
        /// as it is, so a value mocked in the Inspector survives entering Play Mode instead of
        /// being replaced by the stored payload.
        /// </summary>
        Load = 1 << 0,

        /// <summary>Skip the repository write, so a test session cannot alter the stored payload.</summary>
        Save = 1 << 1,
    }
}
