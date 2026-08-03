using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Hlight.DataPersistence.Editor
{
    /// <summary>
    /// Odin drawer for <see cref="DataEntry{T}"/> and its subclasses. Appends Load/Save
    /// buttons inside the foldout when the owning <see cref="DataBundle"/>'s repository is
    /// marked <see cref="SupportsEditorIOAttribute"/>. Works in Edit Mode and Play Mode.
    /// Save is hidden until <see cref="DataEntry{T}.CurrentValue"/> is non-null.
    /// </summary>
    public class DataEntryOdinDrawer<TEntry, TData> : OdinValueDrawer<TEntry>
        where TEntry : DataEntry<TData>
        where TData  : class, new()
    {
        // Soft, non-saturated tints so they read well on both Pro (dark) and Light skins.
        // Load: calm blue — safe read. Save: warm amber — write action that needs attention.
        private static readonly Color LoadColor = new(0.60f, 0.85f, 1.00f);
        private static readonly Color SaveColor = new(1.00f, 0.82f, 0.45f);

        protected override void DrawPropertyLayout(GUIContent label)
        {
            CallNextDrawer(label);
            if (!Property.State.Expanded) return;

            var entry = ValueEntry.SmartValue;
            if (entry == null) return;

            var bundle = FindOwnerBundle(Property);
            if (bundle?.Repository == null) return;
            if (!bundle.Repository.GetType().IsDefined(typeof(SupportsEditorIOAttribute), false)) return;
            if (bundle.GetKeyFor(entry) == null) return; // entry not owned by this bundle

            DrawIOButtons(bundle, entry, hasValue: entry.CurrentValue != null);
        }

        private static void DrawIOButtons(DataBundle bundle, IDataEntry entry, bool hasValue)
        {
            EditorGUI.indentLevel++;
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            rect = EditorGUI.IndentedRect(rect);
            EditorGUI.indentLevel--;

            var loadRect = hasValue ? new Rect(rect.x, rect.y, rect.width * 0.5f, rect.height) : rect;
            var saveRect = new Rect(rect.x + rect.width * 0.5f, rect.y, rect.width * 0.5f, rect.height);

            var previous = GUI.backgroundColor;

            GUI.backgroundColor = LoadColor;
            if (GUI.Button(loadRect, "Load"))
                bundle.LoadAsync(entry).Forget();

            if (hasValue)
            {
                GUI.backgroundColor = SaveColor;
                if (GUI.Button(saveRect, "Save"))
                    bundle.SaveAsync(entry).Forget();
            }

            GUI.backgroundColor = previous;
        }

        private static DataBundle FindOwnerBundle(InspectorProperty property)
        {
            for (var p = property.Parent; p != null; p = p.Parent)
                if (p.ValueEntry?.WeakSmartValue is DataBundle b)
                    return b;
            return null;
        }
    }
}
