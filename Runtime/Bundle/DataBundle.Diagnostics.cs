#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using UnityEditor;

namespace Hlight.DataPersistence
{
    /// <summary>
    /// Inspector-time authoring diagnostics for <see cref="DataBundle"/>. Every problem
    /// reported here is silent at runtime — a wrong declaration form, or a key that resolves
    /// to something unusable — so the Inspector is where they get surfaced.
    ///
    /// <para>
    /// Lives in a partial rather than the Editor assembly because Odin's
    /// <see cref="OnInspectorGUIAttribute"/> has to sit on the inspected type itself. Split
    /// into its own file so the orchestrator is not interleaved with GUI code.
    /// </para>
    /// </summary>
    public abstract partial class DataBundle
    {
        [OnInspectorGUI, PropertyOrder(-1000)]
        private void DrawAuthoringWarnings()
        {
            DrawEntryFieldWarnings();
            DrawKeyWarnings();
        }

        /// <summary>
        /// Entries must be declared as properties — fields are silently skipped by
        /// <see cref="EntryProperties"/>. Warns for every <see cref="IDataEntry"/>-typed
        /// field found, with the exact replacement syntax. Skips compiler-generated
        /// property backing fields.
        /// </summary>
        private void DrawEntryFieldWarnings()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var field in GetType().GetFields(flags))
            {
                if (!typeof(IDataEntry).IsAssignableFrom(field.FieldType)) continue;
                if (field.Name.StartsWith('<')) continue; // compiler-generated backing field for a property

                EditorGUILayout.HelpBox(
                    $"'{field.Name}' is a field — entry discovery is property-based, so it's IGNORED. " +
                    $"Declare as:\n[DataKey(\"{field.Name}\")] [field: SerializeField] " +
                    $"public {field.FieldType.Name} {field.Name} {{ get; private set; }}",
                    MessageType.Warning);
            }
        }

        // Hard-coded rather than Path.GetInvalidFileNameChars(): that returns only '\0' and
        // '/' on Unix, so a key authored on macOS would validate there and break the Windows
        // build. This is the Windows set — a superset — and control characters are rejected
        // too because PlayerPrefsRepository delimits its key index with U+001F.
        private const string ReservedKeyChars = "<>:\"/\\|?*";

        private static bool IsKeyUnsafe(string key)
        {
            foreach (var c in key)
                if (char.IsControl(c) || ReservedKeyChars.IndexOf(c) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Keys become filenames in <see cref="FileRepository"/> and PlayerPrefs entries
        /// elsewhere, and two entries sharing one key overwrite each other in silence.
        /// Property names could never collide or be malformed; <see cref="DataKeyAttribute"/>
        /// values can, so they are validated here.
        /// </summary>
        private void DrawKeyWarnings()
        {
            var seen = new Dictionary<string, string>(); // key -> first property using it

            foreach (var prop in EntryProperties())
            {
                var declared = prop.GetCustomAttribute<DataKeyAttribute>()?.Key;
                var key      = KeyOf(prop);

                if (declared == null)
                    EditorGUILayout.HelpBox(
                        $"'{prop.Name}' has no [DataKey] — its storage key is the property name, so renaming " +
                        $"the property orphans every save already written.\nDeclare:\n[DataKey(\"{prop.Name}\")]",
                        MessageType.Info);
                else if (string.IsNullOrWhiteSpace(declared))
                    EditorGUILayout.HelpBox(
                        $"'{prop.Name}' has a blank [DataKey]. The key is used verbatim — this entry " +
                        $"reads and writes an unnamed payload. Give it a real key.",
                        MessageType.Error);
                else if (IsKeyUnsafe(declared))
                    EditorGUILayout.HelpBox(
                        $"[DataKey(\"{declared}\")] on '{prop.Name}' contains a reserved or control character. " +
                        $"Keys become filenames — avoid {ReservedKeyChars} and control characters.",
                        MessageType.Error);

                if (seen.TryGetValue(key, out var owner))
                    EditorGUILayout.HelpBox(
                        $"'{prop.Name}' and '{owner}' both resolve to the storage key '{key}' — they will " +
                        $"overwrite each other.",
                        MessageType.Error);
                else
                    seen[key] = prop.Name;
            }
        }
    }
}
#endif
