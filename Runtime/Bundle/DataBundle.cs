using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.DataPersistence
{
    /// <summary>
    /// Abstract orchestrator. Serializable pure class — host as a
    /// <c>[SerializeField]</c> field on a <see cref="ScriptableObject"/>
    /// or <see cref="MonoBehaviour"/>, or instantiate in code for tests.
    ///
    /// <para>
    /// Subclass, implement <see cref="Serialize"/> / <see cref="Deserialize"/>, and declare
    /// public <see cref="IDataEntry"/> properties, discovered via reflection on each
    /// load/save. Give each one a <see cref="DataKeyAttribute"/> so its name on disk
    /// survives a rename, and <c>[field: SerializeField]</c> so Unity persists the
    /// backing field:
    /// <code>
    /// public class GameBundle : DataBundle
    /// {
    ///     [DataKey("Player")] [field: SerializeField] public DataEntry&lt;PlayerProgress&gt; Player    { get; private set; }
    ///     [DataKey("Inventory")] [field: SerializeField] public DataEntry&lt;Inventory&gt;      Inventory { get; private set; }
    ///
    ///     protected override byte[]  Serialize(object obj)         => /* encode */;
    ///     protected override object  Deserialize(byte[] b, Type t) => /* decode */;
    /// }
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Responsibility split:
    /// <list type="bullet">
    ///   <item>DataBundle owns: repository IO, <see cref="Serialize"/> / <see cref="Deserialize"/> delegates passed to entries.</item>
    ///   <item>DataEntry owns: deserialization → typed data, serialization → bytes, migration hooks.</item>
    /// </list>
    /// </para>
    /// </summary>
    [Serializable]
    public abstract partial class DataBundle
    {
        [SerializeField] protected ARepository _repository;

        /// <summary>
        /// Byte storage backing every entry. Normally assigned in the Inspector;
        /// settable so tests can inject a fake and so a game can swap backends
        /// (local ↔ cloud) at runtime. Swapping between a load and a save mixes
        /// backends — do it while no IO is in flight.
        /// </summary>
        public ARepository Repository
        {
            get => _repository;
            set => _repository = value;
        }

        /// <summary>
        /// Read bytes for every discovered entry; hand them off to each
        /// entry's <see cref="IDataEntry.OnLoad"/>. Null/empty bytes are passed
        /// through — default <see cref="DataEntry{T}"/> responds by seeding
        /// <see cref="DataEntry{T}.CurrentValue"/> via <c>CreateDefault()</c>.
        /// A failed key only affects that key — other entries still load.
        /// </summary>
        public async UniTask LoadAllAsync()
        {
            if (!TryGetRepository(out var repo)) return;
            foreach (var prop in EntryProperties())
                await LoadEntryAsync(repo, prop);
        }

        /// <summary>
        /// Load a single entry. Use when one key changed and re-reading the whole
        /// bundle is wasteful. No-op (logs) if <paramref name="entry"/> is not held
        /// by a property on this bundle.
        /// </summary>
        public async UniTask LoadAsync(IDataEntry entry)
        {
            if (!TryGetRepository(out var repo)) return;
            if (!TryGetProperty(entry, out var prop)) return;
            await LoadEntryAsync(repo, prop);
        }

        /// <summary>
        /// Ask every discovered entry for save bytes; write non-null results
        /// to the repository. Entries decide their own skip logic inside
        /// <see cref="IDataEntry.OnSave"/>. A failed key only affects that key.
        /// </summary>
        public async UniTask SaveAllAsync()
        {
            if (!TryGetRepository(out var repo)) return;
            foreach (var prop in EntryProperties())
                await SaveEntryAsync(repo, prop);
        }

        /// <summary>
        /// Save a single entry. Use for incremental writes — a currency change should
        /// not rewrite every key in the bundle. No-op (logs) if <paramref name="entry"/>
        /// is not held by a property on this bundle.
        /// </summary>
        public async UniTask SaveAsync(IDataEntry entry)
        {
            if (!TryGetRepository(out var repo)) return;
            if (!TryGetProperty(entry, out var prop)) return;
            await SaveEntryAsync(repo, prop);
        }

        private async UniTask LoadEntryAsync(ARepository repo, PropertyInfo prop)
        {
            var entry = GetOrCreateEntry(prop);
            if (entry == null) return;
            var key = KeyOf(prop);
            try
            {
                var bytes = await repo.LoadAsync(key);
                entry.OnLoad(key, bytes, Deserialize);
            }
            catch (Exception e)
            {
                // The repository could not produce bytes it believes exist. Hand it to the
                // entry so it can guard the unreadable payload instead of overwriting it.
                entry.OnLoadFailed(key, e);
            }
        }

        private async UniTask SaveEntryAsync(ARepository repo, PropertyInfo prop)
        {
            var entry = GetOrCreateEntry(prop);
            if (entry == null) return;
            var key = KeyOf(prop);
            try
            {
                // OnSave is inside the try — a throwing serializer or migration hook
                // must not abort the remaining entries.
                var bytes = entry.OnSave(Serialize);
                if (bytes == null) return;
                await repo.SaveAsync(key, bytes);
            }
            catch (Exception e) { Debug.LogError($"[{nameof(DataBundle)}] Save '{key}' failed: {e}"); }
        }

        /// <summary>
        /// Storage key for an entry property: the <see cref="DataKeyAttribute"/> value when the
        /// attribute is present, otherwise the property name. A declared key is used verbatim —
        /// a blank or malformed one is not quietly repaired, because a key that silently differs
        /// from what was written is the failure this attribute exists to prevent. The bundle's
        /// Inspector reports blank, unsafe, and colliding keys.
        /// </summary>
        private static string KeyOf(PropertyInfo prop)
            => prop.GetCustomAttribute<DataKeyAttribute>()?.Key ?? prop.Name;

        /// <summary>
        /// Serialize <paramref name="obj"/> to raw bytes. Passed as a delegate
        /// to <see cref="IDataEntry.OnSave"/> — entry calls it inside its save hook.
        /// </summary>
        protected abstract byte[] Serialize(object obj);

        /// <summary>
        /// Deserialize raw bytes to an instance of <paramref name="type"/>.
        /// Passed as a delegate to <see cref="IDataEntry.OnLoad"/> — entry calls
        /// it inside its load hook.
        /// </summary>
        protected abstract object Deserialize(byte[] bytes, Type type);

        /// <summary>
        /// Resolve the storage key for a specific entry instance. Returns null if no
        /// property on this bundle currently returns <paramref name="entry"/> — used by
        /// the editor drawer to decide whether an entry belongs to this bundle without
        /// relying on Odin's InspectorProperty.Name, which does not match the property
        /// name when <c>[field: SerializeField]</c> is used.
        /// </summary>
        public string GetKeyFor(IDataEntry entry)
            => TryGetProperty(entry, out var prop, logWhenMissing: false) ? KeyOf(prop) : null;

        private bool TryGetProperty(IDataEntry entry, out PropertyInfo property, bool logWhenMissing = true)
        {
            property = null;
            if (entry != null)
                foreach (var prop in EntryProperties())
                    if (ReferenceEquals(prop.GetValue(this), entry)) { property = prop; return true; }

            if (logWhenMissing)
                Debug.LogError($"[{nameof(DataBundle)}] Entry is not held by any property on {GetType().Name} — no storage key to use.");
            return false;
        }

        private bool TryGetRepository(out ARepository repo)
        {
            repo = _repository;
            if (repo != null) return true;
            Debug.LogError($"[{nameof(DataBundle)}] Repository is not assigned on {GetType().Name}.");
            return false;
        }

        /// <summary>
        /// Every public instance property on this bundle whose type implements
        /// <see cref="IDataEntry"/>. Its storage key comes from <see cref="KeyOf"/>. Use
        /// <c>[field: SerializeField]</c> on the property so Unity serializes the
        /// backing field.
        ///
        /// <para>
        /// Scanned fresh on every call rather than cached: Unity re-deserializes a
        /// bundle in place in the editor, replacing entry instances while any cached
        /// list would keep pointing at the orphaned ones. A handful of properties
        /// once per load/save is not a hot path.
        /// </para>
        /// </summary>
        private IEnumerable<PropertyInfo> EntryProperties()
        {
            foreach (var prop in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(IDataEntry).IsAssignableFrom(prop.PropertyType)) continue;
                if (!prop.CanRead) continue;
                if (prop.GetIndexParameters().Length > 0) continue; // skip indexers
                yield return prop;
            }
        }

        /// <summary>
        /// Read the entry held by <paramref name="prop"/>, instantiating a fresh one
        /// when the property is null (requires a setter). Returns null — and logs —
        /// when the entry can neither be read nor created.
        /// </summary>
        private IDataEntry GetOrCreateEntry(PropertyInfo prop)
        {
            if (prop.GetValue(this) is IDataEntry existing) return existing;

            if (!prop.CanWrite)
            {
                Debug.LogError($"[{nameof(DataBundle)}] Property '{prop.Name}' is null and has no setter — cannot auto-instantiate.");
                return null;
            }
            try
            {
                var instance = (IDataEntry)Activator.CreateInstance(prop.PropertyType);
                prop.SetValue(this, instance);
                return instance;
            }
            catch (Exception e)
            {
                Debug.LogError($"[{nameof(DataBundle)}] Failed to instantiate '{prop.Name}' ({prop.PropertyType.Name}): {e}");
                return null;
            }
        }
    }
}
