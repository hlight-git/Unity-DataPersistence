using System;

namespace Hlight.DataPersistence
{
    /// <summary>
    /// Pins the storage key of a <see cref="DataBundle"/> entry property, decoupling the
    /// name on disk from the name in code.
    ///
    /// <para>
    /// Without it the key defaults to the property name — so renaming the property
    /// silently orphans every save already written under the old name. That failure is
    /// invisible: the code compiles, nothing warns, and the entry simply reads as a first
    /// run and seeds defaults. Schema migration cannot rescue it either, because the
    /// migration hook only runs when bytes are *found*.
    /// </para>
    ///
    /// <para>
    /// <b>Declare it on every entry property from the start</b>, even when the key equals
    /// the property name. The attribute only protects saves written *after* it exists —
    /// adding it once a rename has already shipped is too late.
    /// <code>
    /// [DataKey("Player")] [field: SerializeField]
    /// public DataEntry&lt;PlayerProgress&gt; PlayerProgress { get; private set; } // renaming this is now safe
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Keys reach <see cref="FileRepository"/> as filenames, so keep them short and
    /// filesystem-safe — no path separators, no reserved characters. Blank keys and keys
    /// that collide within one bundle are reported in the bundle's Inspector.
    /// </para>
    ///
    /// <para>
    /// Orthogonal to <c>[FormerlySerializedAs]</c>, which protects the entry's
    /// Inspector-authored contents inside your <c>.asset</c> file. This one protects the
    /// player's save on their device. A rename needs both.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true)]
    public sealed class DataKeyAttribute : Attribute
    {
        public string Key { get; }

        public DataKeyAttribute(string key) => Key = key;
    }
}
