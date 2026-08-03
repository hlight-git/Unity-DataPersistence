using System;

namespace Hlight.DataPersistence
{
    /// <summary>
    /// Mark an <see cref="ARepository"/> subclass with this attribute to
    /// indicate that its <c>LoadAsync</c> / <c>SaveAsync</c> / <c>ClearAsync</c>
    /// work correctly inside the Unity Editor (both Edit Mode and Play Mode).
    ///
    /// <para>
    /// <see cref="DataEntry{T}"/> uses this at runtime to decide whether to show
    /// per-entry Load / Save buttons in the Inspector during Play Mode.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SupportsEditorIOAttribute : Attribute { }
}
