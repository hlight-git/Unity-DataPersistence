namespace Hlight.DataPersistence
{
    /// <summary>
    /// Load-phase state of a <see cref="DataEntry{T}"/>. Saving is a repeated in-game
    /// action with no clean lifecycle, so it is not tracked here — the only reason
    /// save inspects status is to skip entries that failed to load.
    /// </summary>
    public enum DataEntryStatus
    {
        /// <summary>Initial state — <see cref="DataBundle.LoadAllAsync"/> has not run yet.</summary>
        Idle,

        /// <summary><see cref="DataEntry{T}.CurrentValue"/> populated — either from bytes or <c>CreateDefault()</c>.</summary>
        Loaded,

        /// <summary>
        /// Deserialize threw an exception. <see cref="IDataEntry.OnSave"/> returns
        /// <c>null</c> while in this state to protect potentially recoverable bytes on disk.
        /// Sticky — a subsequent successful <see cref="IDataEntry.OnLoad"/> is the only
        /// way back to <see cref="Loaded"/>.
        /// </summary>
        LoadFailed,
    }
}
