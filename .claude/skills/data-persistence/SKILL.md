---
name: data-persistence
description: Use when adding save data, user settings, or any persisted state to a Unity project that uses the Hlight.DataPersistence package. Covers bundle/entry declaration, storage key naming, schema migration, custom repositories, and editor tooling conventions.
---

# Hlight.DataPersistence — Skill

Runtime save system built on three primitives: `DataBundle` (orchestrator), `DataEntry<T>` (typed slot), `ARepository` (byte storage). Bundles declare public properties of `IDataEntry` type, discovered by reflection; each one's storage key comes from its `[DataKey]`.

## When to activate

Triggers:
- User asks to persist gameplay data, settings, or anything between sessions.
- User asks to add a new save slot, new field to a save, or a new migration.
- User asks to swap storage backends (File ↔ PlayerPrefs ↔ remote).
- Files under `Packages/com.hlight.data-persistence/**` or the `Hlight.DataPersistence` namespace are in context.

Do NOT activate for:
- Scene/asset serialization (Unity handles that itself).
- Remote config that is never written back — this package is load+save oriented.
- Addressables/Resources loading — unrelated concern.

## Mental model

```
ScriptableObject ──[SerializeField]──▶ DataBundle
                                          ├── ARepository       (raw bytes)
                                          ├── DataEntry<A> A    ([DataKey("A")])
                                          └── DataEntry<B> B    ([DataKey("B")])
```

- `DataBundle` is a `[Serializable]` pure class — **not** a ScriptableObject. Host it as a field on a ScriptableObject or MonoBehaviour.
- `ARepository` **is** a ScriptableObject — create one asset per backend configuration.
- Storage keys come from `[DataKey("...")]` on the entry property, falling back to the property name when the attribute is absent. Always declare it — see "Renaming things".
- Entries use `[DataKey("Foo")] [field: SerializeField] public X Foo { get; private set; }` — the `[field:]` prefix targets the compiler-generated backing field so Unity serializes it, while reflection discovers the public property.

## Core task: add a new persisted data type

```csharp
// 1. Model — plain [Serializable] class, include SchemaVersion from day 1
[Serializable]
public class InventoryData
{
    public int SchemaVersion;
    public List<ItemStack> Items = new();
}

// 2. Add property to the existing bundle — DO NOT create a new bundle unless domains differ
public class GameSaveBundle : DataBundle
{
    [DataKey("Player")] [field: SerializeField] public DataEntry<PlayerProgress> Player    { get; private set; }
    [DataKey("Inventory")] [field: SerializeField] public DataEntry<InventoryData>  Inventory { get; private set; } // ← new
    // Serialize/Deserialize already implemented
}
```

That's it — no registration, no enum, no switch. The bundle discovers the new property on next `LoadAllAsync()`.

## Core task: schema migration

```csharp
public class InventoryEntry : DataEntry<InventoryData>
{
    const int Latest = 2;

    protected override InventoryData Deserialize(byte[] b, Func<byte[], Type, object> fn)
    {
        var d = (InventoryData)fn(b, typeof(InventoryData));
        if (d.SchemaVersion < 1) { /* v0→v1 transforms */ d.SchemaVersion = 1; }
        if (d.SchemaVersion < 2) { /* v1→v2 transforms */ d.SchemaVersion = 2; }
        return d;
    }

    protected override byte[] Serialize(Func<object, byte[]> fn)
    {
        CurrentValue.SchemaVersion = Latest;
        return fn(CurrentValue);
    }
}
```

Then use `[DataKey("Inventory")] [field: SerializeField] public InventoryEntry Inventory { get; private set; }` on the bundle instead of the raw `DataEntry<InventoryData>` property.

Throwing in `Deserialize` → `Status = LoadFailed` → `OnSave` returns null until a future load succeeds. This protects unreadable bytes on disk. Do not catch-and-swallow exceptions in migration — let them propagate so the protection kicks in.

## Core task: Inspector-tuned defaults

For data with sensible defaults editable by designers (settings, starter inventory, feature flags), use `DefaultDataEntry<T>`:

```csharp
public class GameSaveBundle : DataBundle
{
    [DataKey("Settings")] [field: SerializeField]
    public DefaultDataEntry<GameSettings> Settings { get; private set; } // _defaultValue editable in Inspector
}
```

`CreateDefault()` returns a `JsonUtility` deep clone of the template. If the model has Odin-serialized fields (dictionaries, polymorphic lists) that `JsonUtility` can't round-trip, override `CreateDefault()` manually instead.

## Core task: custom repository

```csharp
[SupportsEditorIO]  // only if it actually works in Edit Mode (e.g. local file, not remote HTTP)
public class RemoteRepository : ARepository
{
    public override UniTask<byte[]> LoadAsync(string key) { ... }
    public override UniTask SaveAsync(string key, byte[] bytes) { ... }
    public override UniTask DeleteAsync(string key) { ... }
    public override UniTask ClearAsync() { ... }
}
```

`[SupportsEditorIO]` controls editor buttons (per-entry Load/Save, Clear). Don't add it unless the repo actually works without entering Play Mode.

**`LoadAsync` contract — the one thing custom repositories get wrong:**

| Situation | Return |
|---|---|
| Nothing ever saved for this key | `null` (or empty) — caller seeds `CreateDefault()` |
| Payload exists, integrity check fails | **throw** `InvalidDataException` |
| Transport/IO error | **throw** |

Returning `null` for a corrupt payload looks harmless but is data loss: the entry reads it as a
first run, seeds defaults, and the next `SaveAllAsync` overwrites the damaged bytes. Throwing puts
the entry in `LoadFailed`, which suppresses its saves and leaves the payload recoverable.

## Renaming things

Renaming an entry property touches three independent things. Each needs its own mechanism;
none of them covers another.

| What is at risk | Mechanism | Consequence if missing |
|---|---|---|
| Payload *shape* (fields inside the model) | Override `Deserialize`, bump `SchemaVersion` | Old saves decode wrong or throw |
| Entry contents inside **your `.asset`** (`_defaultValue`, `_ignoreFlags`) | `[FormerlySerializedAs("<OldName>k__BackingField")]` | Designer-tuned values reset — re-enter them in the Inspector |
| **The player's save file** on their device | `[DataKey("StableName")]` | Total data loss, silent, unrecoverable |

**Why schema migration cannot substitute for `[DataKey]`.** The migration hook lives
*downstream* of the key lookup. `DataEntry<T>.OnLoad` short-circuits before it:

```csharp
if (bytes == null || bytes.Length == 0)
{
    CurrentValue = CreateDefault();
    Status       = DataEntryStatus.Loaded;
    return;                              // Deserialize is never invoked
}
```

Rename the property without `[DataKey]` → the repository is asked for a key that was never
written → `null` → the entry reads it as a first run and seeds defaults. Your migration code
does not run wrong; it does not run at all.

**Why `[FormerlySerializedAs]` cannot substitute for it either.** It teaches Unity's
serializer how to map an old YAML field name — it has no bearing on the storage key, which
comes from `PropertyInfo` via C# reflection. The two systems never meet.

**`[DataKey]` is prevention, not repair.** It only protects saves written *after* it exists.
Adding it once a rename has shipped is too late — those saves are already orphaned. Hence:
declare it on every entry from the start, even when the value matches the property name.

If a rename has *already* shipped without `[DataKey]`, there is no hook that recovers it
automatically. Do it at app level, once, after `LoadAllAsync`: keep the old property alive
alongside the new one, copy `old.CurrentValue` into `new.CurrentValue`, save the new key,
then `await bundle.Repository.DeleteAsync("OldKey")`. Retire the old property only after the
version that does the copy is no longer in the wild — users skip versions.

## Gotchas

1. **Public properties only** — reflection in `DataBundle.EntryProperties()` scans `BindingFlags.Public | Instance` on properties. Private fields, private properties, and raw public fields are silently skipped. `[field: SerializeField]` is required on auto-properties for Unity to persist the backing field.

2. **Always declare `[DataKey]`** — on every entry property, from the first commit, even when the key equals the property name. Without it the key defaults to the property name, and renaming the property orphans every save already on disk. See "Renaming things" below for why nothing else catches this.

3. **`Status` is framework-owned** — `OnLoad` / `OnLoadFailed` are the only things that write it. The setter is `protected` so a custom entry subclass can maintain it; don't reach for it from game code.

4. **Save order matches property declaration order** — `GetProperties` order is not guaranteed by the CLR spec, but in practice matches declaration order in a single class. Don't rely on it for correctness; each entry must be independently saveable.

5. **`LoadAllAsync` / `SaveAllAsync` are resilient, not atomic** — a failed key only affects that key; other entries still load and save. Don't wrap the whole call in try/catch expecting atomicity; check individual `Entry.Status` instead. Corollary: a crash mid-`SaveAllAsync` can leave some keys new and some old. If two entries must agree with each other, keep them in one entry.

6. **Serializer lives on the bundle, not the entry** — all entries in a bundle share one format. If you need heterogeneous formats (JSON for settings, binary for replays), use two bundles.

7. **`[Serializable]` on the bundle class is required** — Unity won't serialize the `_repository` / entry properties otherwise.

8. **Read-only (get-only) properties** — skipped during auto-instantiation: if the current value is null and no setter exists, an error is logged and the entry is skipped. Give the property at least a `private set`.

9. **Save one key, not all of them** — `bundle.SaveAsync(bundle.Player)` writes a single key. Reach for `SaveAllAsync` at checkpoints / app-pause, not on every currency tick.

10. **Never assign `CurrentValue = null`** — `OnSave` treats null as "nothing to write", so a nulled entry silently stops persisting. Assign a real instance, or let `LoadAllAsync` seed one.

## Naming conventions

| Thing | Convention | Example |
|---|---|---|
| Bundle class | `{Feature}Bundle` extending `DataBundle` | `GameSaveBundle`, `AnalyticsBundle` |
| Entry class (custom) | `{Feature}Entry` extending `DataEntry<T>` | `PlayerSaveEntry` |
| Model class | `{Feature}Data` plain `[Serializable]` | `PlayerSaveData`, `InventoryData` |
| Host SO | `{Feature}Asset` | `GameSaveAsset` |
| Storage key | Explicit `[DataKey]`, initially matching the property name | `[DataKey("Player")]` |

## Anti-patterns to reject

- ❌ Creating a separate bundle per entry — bundles batch reflection and IO; one bundle per save domain is right.
- ❌ Using `DataBundle` directly without subclassing — it's abstract; `Serialize`/`Deserialize` must be implemented.
- ❌ Mutating `CurrentValue` inside `Deserialize` of a framework hook *after* the return — the framework assigns the return value; side effects on incoming data are discarded.
- ❌ Using public fields or `[SerializeField] private` entries expecting auto-discovery — the scan is property-based. Convert to `[DataKey("Foo")] [field: SerializeField] public X Foo { get; private set; }`.
- ❌ Adding an entry property without `[DataKey]` — "I'll add it if I ever rename this" is exactly backwards; by then the saves are already orphaned. The bundle's Inspector flags every property that is missing one.
- ❌ Changing a `[DataKey]` value to "clean it up" — that IS the rename you were protecting against. The value is a wire format; leave it ugly if it shipped ugly.
- ❌ Catching exceptions inside `Deserialize` to "fix" a bad save — this defeats the `LoadFailed` protection and risks overwriting recoverable bytes on the next save.
- ❌ Calling `PlayerPrefs.DeleteAll()` for "clear save" — use `ARepository.ClearAsync()` which scopes to the repo's key prefix.

## Verification after changes

```csharp
// In Edit Mode tests — DataBundle is a plain class and Repository is settable, so a
// fake ARepository is all the seam you need. See Tests/TestFixtures/TestBundle.cs.
var bundle = new GameSaveBundle { Repository = ScriptableObject.CreateInstance<FakeRepository>() };
await bundle.LoadAllAsync();
Assert.AreEqual(DataEntryStatus.Loaded, bundle.PlayerSave.Status);
Assert.IsNotNull(bundle.PlayerSave.CurrentValue);
```

Run the package's own suite (`Hlight.DataPersistence.Tests`) after touching `DataBundle`,
`DataEntry`, or any repository — it pins per-key save isolation and corrupt-payload protection.

For manual verification: assign a `FileRepository` asset to the bundle's `_repository`, hit the Inspector **Load All** / **Save All** buttons in Edit Mode, inspect `persistentDataPath`.

## Further reading

- Package README: `Packages/com.hlight.data-persistence/README.md` — user-facing feature tour
- Source: `Packages/com.hlight.data-persistence/Runtime/` — every class has xmldoc explaining responsibility
- Example: `Packages/com.hlight.data-persistence/Example/` — runnable end-to-end with migration
