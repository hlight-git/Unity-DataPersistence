# Hlight.DataPersistence

Typed, Inspector-driven save system for Unity. Declare fields on a bundle, call `LoadAllAsync` / `SaveAllAsync` — storage format and backend are pluggable.

## Install

Add assembly references to your project:
- `Hlight.DataPersistence` (runtime)
- `Hlight.DataPersistence.Editor` (editor drawers — automatic)

Dependencies: `UniTask`, `Sirenix.OdinInspector.Attributes`.

## Quick Start

```csharp
// 1. Model — plain [Serializable] class
[Serializable]
public class PlayerProgress
{
    public int SchemaVersion;
    public int HighestLevel;
    public int Coins;
}

// 2. Bundle — one property per storage key.
//    [DataKey] pins the name on disk; [field: SerializeField] makes Unity persist the entry.
[Serializable]
public class GameSaveBundle : DataBundle
{
    [DataKey("Player")] [field: SerializeField] public DataEntry<PlayerProgress>      Player   { get; private set; }
    [DataKey("Settings")] [field: SerializeField] public DefaultDataEntry<GameSettings> Settings { get; private set; }

    protected override byte[] Serialize(object obj)
        => Encoding.UTF8.GetBytes(JsonUtility.ToJson(obj));

    protected override object Deserialize(byte[] bytes, Type type)
        => JsonUtility.FromJson(Encoding.UTF8.GetString(bytes), type);
}

// 3. Host on a ScriptableObject and assign a repository in the Inspector
public class GameSaveAsset : ScriptableObject
{
    [SerializeField] private GameSaveBundle _bundle;
    public GameSaveBundle Bundle => _bundle;
}

// 4. Use it
await asset.Bundle.LoadAllAsync();
asset.Bundle.Player.CurrentValue.Coins += 50;
await asset.Bundle.SaveAsync(asset.Bundle.Player);  // one key…
await asset.Bundle.SaveAllAsync();                  // …or everything
```

## Architecture

```
DataBundle              — orchestrator. Owns ARepository + Serialize/Deserialize.
├─ LoadAllAsync()       Reflects public IDataEntry properties → key → bytes → entry.OnLoad.
├─ SaveAllAsync()       entry.OnSave → bytes → repository.
├─ LoadAsync(entry)     Single key. For incremental reads.
└─ SaveAsync(entry)     Single key. A currency change shouldn't rewrite the whole bundle.

DataEntry<T>            — typed slot. Holds CurrentValue + Status.
├─ CreateDefault()      First-run seed. Override to populate.
├─ Deserialize(...)     bytes → T. Override to migrate old schemas.
└─ Serialize(...)       T → bytes. Override for pre-save transforms.

DefaultDataEntry<T>     — DataEntry<T> with an Inspector-tuned template.
                          CreateDefault() returns a deep clone (JsonUtility).

ARepository             — raw byte storage. ScriptableObject.
├─ FileRepository        persistentDataPath/SubDir/key.ext
└─ PlayerPrefsRepository Base64 in PlayerPrefs, keyed with prefix + __index
```

**Responsibility split:**
- `DataBundle` owns serialization format + repository IO.
- `DataEntry<T>` owns the typed data lifecycle and schema migration.
- `ARepository` knows nothing about models or formats — just bytes.

## Built-in Repositories

| Repository | Backend | Editor IO | Notes |
|---|---|---|---|
| `FileRepository` | `Application.persistentDataPath` | ✅ | Subdirectory + extension configurable. Threaded IO. |
| `PlayerPrefsRepository` | `PlayerPrefs` (Base64) | ✅ | Main-thread only. Not for large payloads. |

Both are `ScriptableObject` — create asset via **Create → ScriptableObject**, assign in bundle's Inspector.

### Custom Repository

```csharp
[SupportsEditorIO] // add only if LoadAsync/SaveAsync work in Editor
public class RemoteRepository : ARepository
{
    // null ⇒ nothing saved yet. Corrupt/unreadable ⇒ throw (InvalidDataException).
    public override UniTask<byte[]> LoadAsync(string key)        => /* ... */;
    public override UniTask         SaveAsync(string key, byte[] bytes) => /* ... */;
    public override UniTask         DeleteAsync(string key)      => /* ... */;
    public override UniTask         ClearAsync()                 => /* ... */;
}
```

## Storage Keys

The key is `[DataKey("...")]` on the entry property, falling back to the property name when
absent. **Declare it on every entry from the start**, even when the value matches the
property name — otherwise renaming the property silently orphans every save already written,
and no migration hook can recover it (the hook only runs when bytes are *found*).

Renaming an entry property affects three separate things, each with its own mechanism:

| At risk | Mechanism |
|---|---|
| Payload shape (fields inside the model) | Override `Deserialize` + `SchemaVersion` |
| Entry contents in your `.asset` (`_defaultValue`, `_ignoreFlags`) | `[FormerlySerializedAs("<OldName>k__BackingField")]` |
| The player's save file | `[DataKey("StableName")]` |

Keys become filenames in `FileRepository`, so keep them short and filesystem-safe. The
bundle's Inspector flags missing, blank, illegal, and colliding keys.

## Migration Pattern

Stamp a `SchemaVersion` field on your model and apply forward migrations in a custom entry:

```csharp
public class PlayerSaveEntry : DataEntry<PlayerSaveData>
{
    private const int LatestSchemaVersion = 2;

    protected override PlayerSaveData Deserialize(byte[] bytes, Func<byte[], Type, object> fn)
    {
        var data = (PlayerSaveData)fn(bytes, typeof(PlayerSaveData));
        if (data.SchemaVersion < 1) { /* v0 → v1 */ data.SchemaVersion = 1; }
        if (data.SchemaVersion < 2) { /* v1 → v2 */ data.SchemaVersion = 2; }
        return data;
    }

    protected override byte[] Serialize(Func<object, byte[]> fn)
    {
        CurrentValue.SchemaVersion = LatestSchemaVersion;
        return fn(CurrentValue);
    }
}
```

If `Deserialize` throws, `Status` becomes `LoadFailed` and `OnSave` returns `null` — protects potentially recoverable bytes on disk. Sticky: only a successful load resets it.

## Status Lifecycle

```
Idle ──LoadAllAsync──▶ Loaded          bytes decoded, or CreateDefault() on a first run
        │
        ├──deserialize throws────▶ LoadFailed
        └──repository throws─────▶ LoadFailed   (IO error, or InvalidDataException = corrupt)

LoadFailed: CurrentValue is seeded with CreateDefault() so the game still runs,
            but OnSave returns null — the unreadable payload is never overwritten.
            Sticky until a load succeeds.
```

Shown in the Inspector with a colour indicator, in Edit Mode and Play Mode alike — the
per-entry Load/Save buttons work in both, so the resulting status has to be visible in both.

**Corrupt vs. absent.** A repository returns `null` *only* when nothing was ever saved — that is what makes an entry seed defaults. If a payload exists but fails its integrity check, the repository **throws** (`InvalidDataException`); returning `null` there would silently reset the save and overwrite it on the next write. Custom repositories must honour this split.

## Known Limits

- **`SaveAllAsync` is not atomic across keys.** Each key is written crash-safely on its own (see `FileRepository`), but a crash mid-bundle can leave key 1–3 new and key 4–8 old. Only matters if entries hold cross-references to each other; keep such data in a single entry.
- **One serializer per bundle.** All entries in a bundle share the bundle's `Serialize`/`Deserialize`. Need JSON for settings and binary for replays? Use two bundles.
- **`PlayerPrefsRepository` flushes per key.** `SaveAllAsync` over N entries calls `PlayerPrefs.Save()` N times. Fine for a handful of small keys, which is all PlayerPrefs is good for anyway.

## Editor Tooling

- **Per-entry Load / Save buttons** — shown in the Inspector when the bundle's repository is marked `[SupportsEditorIO]`. Works in Edit Mode and Play Mode.
- **Clear Repository** button on `ARepository` subclasses with `[SupportsEditorIO]`.
- **Ignore flags** on every `DataEntry<T>` (`Load` / `Save`) to suppress lifecycle hooks during debugging. Serialized — survives domain reload.

## File Layout

```
Runtime/
  Bundle/DataBundle.cs             Abstract orchestrator
  Bundle/DataBundle.Diagnostics.cs Editor-only authoring warnings (partial, #if UNITY_EDITOR)
  Bundle/DataKeyAttribute.cs       Pins an entry's storage key
  Entry/IDataEntry.cs            Marker contract
  Entry/DataEntry.cs             Default typed entry (sealed framework + override hooks)
  Entry/DefaultDataEntry.cs      DataEntry + Inspector-tuned template
  Entry/DataEntryStatus.cs       Idle / Loaded / LoadFailed
  Entry/DataEntryIgnoreFlags.cs  Editor-only Load/Save suppression flags
  Repository/ARepository.cs           Raw byte storage base
  Repository/FileRepository.cs
  Repository/PlayerPrefsRepository.cs
  Repository/SupportsEditorIOAttribute.cs

Editor/
  DataEntryOdinDrawer.cs         Per-entry Load/Save button drawer

Tests/                           19 EditMode tests
  DataBundleTests.cs             Per-key isolation, corrupt-payload protection, [DataKey]
  FileRepositoryTests.cs         CRC, orphan-tmp recovery, thread affinity
  TestFixtures/TestBundle.cs     In-memory repository + a deliberately throwing entry
  AgentTestRunner.cs             Menu item that writes results to a file, for CI/agents

Example/
  GameSaveData.cs                PlayerSaveData + GameSettings models
  PlayerSaveEntry.cs             Entry with migration example
  GameSaveBundle.cs              Bundle with JsonUtility serialization
  GameSaveAsset.cs               ScriptableObject host
```

## Claude Code Skill

A skill at `.claude/skills/data-persistence/SKILL.md` teaches agents how to use this package. Activated automatically when working in projects that include it.
