using System;

namespace Hlight.DataPersistence
{
    /// <summary>
    /// Marker contract discovered by <see cref="DataBundle"/> via reflection on
    /// public properties of a bundle subclass. Implementors own deserialize/serialize;
    /// <see cref="DataBundle"/> owns repository IO and delegate provisioning.
    ///
    /// <para>
    /// Two hooks split responsibility cleanly:
    /// <list type="bullet">
    ///   <item><see cref="OnLoad"/> — bundle passes raw bytes + deserialize delegate; entry turns them into typed data.</item>
    ///   <item><see cref="OnSave"/> — bundle passes serialize delegate; entry returns bytes (or <c>null</c> to skip).</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface IDataEntry
    {
        /// <summary>Current load-phase state — updated by the framework, never by callers.</summary>
        DataEntryStatus Status { get; }

        /// <summary>
        /// Turn repository bytes into entry's typed data. <see cref="DataBundle"/>
        /// passes through null/empty bytes (no file on disk); the implementation
        /// decides how to respond. Default <see cref="DataEntry{T}"/> seeds via
        /// <c>CreateDefault()</c> on null/empty, otherwise calls the delegate.
        /// </summary>
        void OnLoad(string key, byte[] bytes, Func<byte[], Type, object> deserializeFunc);

        /// <summary>
        /// The repository could not produce bytes for this key — IO error, or a
        /// payload that failed its integrity check (<see cref="System.IO.InvalidDataException"/>).
        /// Distinct from "no bytes yet", which arrives through <see cref="OnLoad"/>
        /// with a null payload.
        ///
        /// <para>
        /// Implementations should leave the persisted bytes recoverable: seed
        /// something usable so the game can run, but enter a state where
        /// <see cref="OnSave"/> returns <c>null</c> so the unreadable payload is
        /// not overwritten with fresh defaults.
        /// </para>
        /// </summary>
        void OnLoadFailed(string key, Exception error);

        /// <summary>
        /// Produce bytes for the repository to write. Return <c>null</c> to
        /// skip save (e.g. <see cref="DataEntryStatus.LoadFailed"/>, nothing to write).
        /// </summary>
        byte[] OnSave(Func<object, byte[]> serializeFunc);
    }
}
