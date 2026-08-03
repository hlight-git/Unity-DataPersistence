using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Hlight.DataPersistence
{
    /// <summary>
    /// Raw byte storage. Knows nothing about models or serialization format.
    /// Subclass with <see cref="SupportsEditorIOAttribute"/> if the implementation
    /// functions correctly inside the Unity Editor (Edit Mode and Play Mode).
    /// </summary>
    public abstract class ARepository : ScriptableObject
    {
        /// <summary>
        /// Read the payload for <paramref name="key"/>. Return <c>null</c> (or empty)
        /// only when nothing has been saved yet — the caller reads that as "first run"
        /// and seeds defaults. If a payload exists but cannot be read or fails an
        /// integrity check, <b>throw</b> (<see cref="System.IO.InvalidDataException"/>
        /// for corruption) so the caller can protect the damaged bytes instead of
        /// overwriting them.
        /// </summary>
        public abstract UniTask<byte[]> LoadAsync(string key);

        /// <summary>
        /// Write <paramref name="bytes"/> under <paramref name="key"/>, replacing any
        /// existing payload. <paramref name="bytes"/> is never null — <see cref="DataBundle"/>
        /// skips entries that produce no payload rather than writing an empty one.
        /// </summary>
        public abstract UniTask SaveAsync(string key, byte[] bytes);

        /// <summary>Remove the payload for <paramref name="key"/>. No-op if absent.</summary>
        public abstract UniTask DeleteAsync(string key);

        /// <summary>Remove all persisted data managed by this repository.</summary>
        public abstract UniTask ClearAsync();

#if UNITY_EDITOR
        private bool IsEditorIOSupported => GetType().IsDefined(typeof(SupportsEditorIOAttribute), false);

        // Forget() over async void: an exception in an async void escapes to the
        // editor's SynchronizationContext; Forget() routes it to UniTaskScheduler.
        [Button("Clear Repository"), GUIColor(1f, 0.4f, 0.4f), ShowIf(nameof(IsEditorIOSupported))]
        private void ClearRepositoryInEditor() => ClearAsync().Forget();
#endif
    }
}
