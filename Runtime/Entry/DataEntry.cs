using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Hlight.DataPersistence
{
    /// <summary>
    /// Default generic <see cref="IDataEntry"/>. Holds a typed reference to model
    /// data, runs a sealed load/save framework, and exposes narrow hooks for
    /// customization.
    ///
    /// <para>
    /// Declare as a public property on a <see cref="DataBundle"/> subclass, with a
    /// <see cref="DataKeyAttribute"/> for the storage key and <c>[field: SerializeField]</c>
    /// so Unity persists the backing field:
    /// <code>
    /// public class GameBundle : DataBundle
    /// {
    ///     [DataKey("Player")] [field: SerializeField] public DataEntry&lt;PlayerProgress&gt; Player   { get; private set; }
    ///     [DataKey("Settings")] [field: SerializeField] public DataEntry&lt;GameSettings&gt;   Settings { get; private set; }
    ///     // implement DataBundle.Serialize / DataBundle.Deserialize
    /// }
    ///
    /// await bundle.LoadAllAsync();
    /// bundle.Player.CurrentValue.Level = 5;
    /// await bundle.SaveAllAsync();
    /// </code>
    /// </para>
    ///
    /// <para>
    /// <b>Framework is sealed</b> — <see cref="IDataEntry.OnLoad"/> / <see cref="IDataEntry.OnSave"/>
    /// are explicit interface implementations; subclasses cannot override
    /// the load-failure guard or exception handling.
    /// Customize via the narrow hooks:
    /// <list type="bullet">
    ///   <item><see cref="CreateDefault"/> — build instance when no bytes exist (first run).</item>
    ///   <item><see cref="Deserialize"/> — convert raw bytes to <c>T</c>; override to migrate old schemas.</item>
    ///   <item><see cref="Serialize"/> — convert <c>T</c> to bytes; override for pre-save transforms or conditional skip.</item>
    /// </list>
    /// For wholly custom IO (encryption, bytes-level migration), implement
    /// <see cref="IDataEntry"/> directly instead of subclassing.
    /// </para>
    /// </summary>
    [Serializable]
    public class DataEntry<T> : IDataEntry where T : class, new()
    {
        /// <summary>
        /// Editor-only flags to suppress Load and/or Save hooks during testing.
        /// Serialized so the value survives domain reload — set before Play, stays active for that session.
        /// </summary>
        [EnumToggleButtons, SerializeField]
        private DataEntryIgnoreFlags _ignoreFlags;

        // --------------------------------- State ----------------------------------

#if UNITY_EDITOR
        private Color StatusColor => Status switch
        {
            DataEntryStatus.Idle       => Color.white,
            DataEntryStatus.Loaded     => Color.green,
            DataEntryStatus.LoadFailed => Color.red,
            _                          => Color.white
        };
        // Visible in Edit Mode too — the per-entry Load/Save buttons work there, so the
        // resulting status has to be visible there.
        [GUIColor(nameof(StatusColor))]
        [ShowInInspector]
        [ReadOnly]
#endif
        public DataEntryStatus Status { get; protected set; }

        /// <summary>
        /// Typed model. Populated by the framework on load; freely settable by
        /// the caller. <c>null</c> until <see cref="DataBundle.LoadAllAsync"/> runs.
        /// </summary>
        [ShowInInspector]
        public T CurrentValue { get; set; }

        // --------------- Sealed framework (explicit interface impl) ---------------

        void IDataEntry.OnLoad(string key, byte[] bytes, Func<byte[], Type, object> deserializeFunc)
        {
#if UNITY_EDITOR
            // Leaves CurrentValue exactly as it is — whatever was mocked in the Inspector
            // stays. Seeding anything here would silently overwrite that mock.
            if ((_ignoreFlags & DataEntryIgnoreFlags.Load) != 0) return;
#endif
            if (bytes == null || bytes.Length == 0)
            {
                CurrentValue = CreateDefault();
                Status       = DataEntryStatus.Loaded;
                return;
            }

            try
            {
                CurrentValue = Deserialize(bytes, deserializeFunc);
                Status       = DataEntryStatus.Loaded;
            }
            catch (Exception e)
            {
                EnterLoadFailed(key, e, "deserialize failed");
            }
        }

        void IDataEntry.OnLoadFailed(string key, Exception error)
        {
#if UNITY_EDITOR
            if ((_ignoreFlags & DataEntryIgnoreFlags.Load) != 0) return;
#endif
            EnterLoadFailed(key, error, "repository could not read this key");
        }

        /// <summary>
        /// Enter the protected state: keep the game running on something usable, but stop
        /// saving so the payload we failed to read stays recoverable. <c>??=</c> matters —
        /// a failed *re-load* must not discard data the session already holds.
        /// </summary>
        private void EnterLoadFailed(string key, Exception error, string reason)
        {
            CurrentValue ??= CreateDefault();
            Status         = DataEntryStatus.LoadFailed;
            Debug.LogError($"[{GetType().Name}] `{key}` {reason} — running on current/default value, saves suppressed for this key until a load succeeds: {error}");
        }

        byte[] IDataEntry.OnSave(Func<object, byte[]> serializeFunc)
        {
            if (Status == DataEntryStatus.LoadFailed) return null;
            // Nothing to write. Without this, saving before LoadAllAsync serializes a
            // null model — either throwing, or overwriting a good save with an empty payload.
            if (CurrentValue == null) return null;
#if UNITY_EDITOR
            if ((_ignoreFlags & DataEntryIgnoreFlags.Save) != 0) return null;
#endif
            return Serialize(serializeFunc);
        }

        // ----------------------------- Override points ----------------------------

        /// <summary>
        /// Build a fresh instance when the repository has no bytes for this
        /// entry's key (first run, deleted save). Default: <c>new T()</c>.
        /// Override to seed meaningful values.
        /// </summary>
        protected virtual T CreateDefault() => new T();

        /// <summary>
        /// Convert raw bytes to a typed <c>T</c> instance stored in <see cref="CurrentValue"/>.
        /// Default: invoke the delegate and cast. Override to migrate old schemas
        /// before or after the delegate call.
        /// </summary>
        protected virtual T Deserialize(byte[] bytes, Func<byte[], Type, object> deserializeFunc)
            => (T)deserializeFunc(bytes, typeof(T));

        /// <summary>
        /// Convert <see cref="CurrentValue"/> to raw bytes for the repository.
        /// Default: invoke the delegate. Override to mutate data first, transform
        /// output, or return <c>null</c> to conditionally skip save.
        /// </summary>
        protected virtual byte[] Serialize(Func<object, byte[]> serializeFunc)
            => serializeFunc(CurrentValue);
    }
}
