using System.Collections;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hlight.DataPersistence.Tests
{
    /// <summary>
    /// Covers the guarantees DataBundle documents: per-key isolation on save, and never
    /// overwriting bytes the entry could not read.
    /// </summary>
    public class DataBundleTests
    {
        private TestBundle     _bundle;
        private FakeRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _repo   = ScriptableObject.CreateInstance<FakeRepository>();
            _bundle = new TestBundle { Repository = _repo };
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false; // static — must not leak into the next test
            Object.DestroyImmediate(_repo);
        }

        private static byte[] Json(int value) => Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Score { Value = value }));

        [UnityTest]
        public IEnumerator LoadAll_MissingKey_SeedsDefault() => UniTask.ToCoroutine(async () =>
        {
            await _bundle.LoadAllAsync();

            Assert.AreEqual(DataEntryStatus.Loaded, _bundle.Good.Status);
            Assert.IsNotNull(_bundle.Good.CurrentValue);
            Assert.AreEqual(0, _bundle.Good.CurrentValue.Value);
        });

        [UnityTest]
        public IEnumerator LoadAll_ExistingKey_ReadsStoredValue() => UniTask.ToCoroutine(async () =>
        {
            _repo.Store["Good"] = Json(42);

            await _bundle.LoadAllAsync();

            Assert.AreEqual(42, _bundle.Good.CurrentValue.Value);
        });

        [UnityTest]
        public IEnumerator SaveAll_ThrowingEntry_DoesNotAbortRemainingEntries() => UniTask.ToCoroutine(async () =>
        {
            await _bundle.LoadAllAsync();
            _bundle.Good.CurrentValue.Value = 7;
            LogAssert.ignoreFailingMessages = true; // the throwing entry logs an error by design

            await _bundle.SaveAllAsync();

            // "Broken" is declared first and throws; "Good" must still reach the repository.
            CollectionAssert.Contains(_repo.SaveCalls, "Good");
            Assert.AreEqual(7, JsonUtility.FromJson<Score>(Encoding.UTF8.GetString(_repo.Store["Good"])).Value);
        });

        [UnityTest]
        public IEnumerator SaveAll_BeforeLoad_WritesNothing() => UniTask.ToCoroutine(async () =>
        {
            LogAssert.ignoreFailingMessages = true;

            await _bundle.SaveAllAsync(); // CurrentValue is null on every entry

            CollectionAssert.IsEmpty(_repo.SaveCalls);
        });

        [UnityTest]
        public IEnumerator LoadAll_CorruptKey_EntersLoadFailedAndNeverOverwrites() => UniTask.ToCoroutine(async () =>
        {
            _repo.Store["Good"] = Json(99);
            _repo.CorruptKeys.Add("Good");
            LogAssert.ignoreFailingMessages = true;

            await _bundle.LoadAllAsync();

            Assert.AreEqual(DataEntryStatus.LoadFailed, _bundle.Good.Status);
            Assert.IsNotNull(_bundle.Good.CurrentValue, "game should still run on defaults");

            await _bundle.SaveAllAsync();

            CollectionAssert.DoesNotContain(_repo.SaveCalls, "Good");
            Assert.AreEqual(99, JsonUtility.FromJson<Score>(Encoding.UTF8.GetString(_repo.Store["Good"])).Value,
                "the unreadable payload must survive for recovery");
        });

        [UnityTest]
        public IEnumerator ReloadFailure_KeepsDataAlreadyInMemory() => UniTask.ToCoroutine(async () =>
        {
            _repo.Store["Good"] = Json(77);
            await _bundle.LoadAllAsync();
            LogAssert.ignoreFailingMessages = true;

            _repo.CorruptKeys.Add("Good");
            await _bundle.LoadAsync(_bundle.Good);

            Assert.AreEqual(DataEntryStatus.LoadFailed, _bundle.Good.Status);
            Assert.AreEqual(77, _bundle.Good.CurrentValue.Value,
                "a failed re-load must not discard data the session already holds");
        });

        [UnityTest]
        public IEnumerator SaveAsync_SingleEntry_WritesOnlyThatKey() => UniTask.ToCoroutine(async () =>
        {
            await _bundle.LoadAllAsync();
            _bundle.Good.CurrentValue.Value = 3;

            await _bundle.SaveAsync(_bundle.Good);

            CollectionAssert.AreEqual(new[] { "Good" }, _repo.SaveCalls);
        });

        [UnityTest]
        public IEnumerator LoadAsync_SingleEntry_RereadsOnlyThatKey() => UniTask.ToCoroutine(async () =>
        {
            _repo.Store["Good"] = Json(5);
            await _bundle.LoadAllAsync();
            Assert.AreEqual(5, _bundle.Good.CurrentValue.Value);

            // Someone else rewrote the key behind our back.
            _repo.Store["Good"] = Json(11);
            var brokenBefore = _bundle.Broken.CurrentValue;

            await _bundle.LoadAsync(_bundle.Good);

            Assert.AreEqual(11, _bundle.Good.CurrentValue.Value);
            Assert.AreSame(brokenBefore, _bundle.Broken.CurrentValue, "other entries must not be re-read");
        });

        // The flags are an Inspector-only debug switch on a private serialized field, and the
        // entry property has a private setter — deliberate in both cases. Reflection is the
        // seam. The flags must be in place BEFORE the first load, which is how they are
        // actually used: ticked in the Inspector, then enter Play Mode.
        private void PlantEntryWithFlags(string propertyName, DataEntryIgnoreFlags flags)
        {
            var entry = new DataEntry<Score>();
            typeof(DataEntry<Score>)
                .GetField("_ignoreFlags", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(entry, flags);
            _bundle.GetType().GetProperty(propertyName).SetValue(_bundle, entry);
        }

        [UnityTest]
        public IEnumerator IgnoreLoad_LeavesTheInspectorMockUntouched() => UniTask.ToCoroutine(async () =>
        {
            _repo.Store["Good"] = Json(500);              // the stored payload
            PlantEntryWithFlags("Good", DataEntryIgnoreFlags.Load);
            _bundle.Good.CurrentValue = new Score { Value = 42 }; // stands in for the Odin-mocked value

            await _bundle.LoadAllAsync();

            Assert.AreEqual(42, _bundle.Good.CurrentValue.Value,
                "the mock must survive — Ignore Load exists so the stored payload does not replace it");
            Assert.AreEqual(DataEntryStatus.Idle, _bundle.Good.Status, "nothing was loaded");
        });

        [UnityTest]
        public IEnumerator IgnoreLoad_DoesNotBlockSave() => UniTask.ToCoroutine(async () =>
        {
            PlantEntryWithFlags("Good", DataEntryIgnoreFlags.Load);
            _bundle.Good.CurrentValue = new Score { Value = 42 };
            LogAssert.ignoreFailingMessages = true; // the ThrowingEntry logs during SaveAll

            await _bundle.LoadAllAsync();
            await _bundle.SaveAllAsync();

            // The two flags are independent by design: mock a value, then exercise the save
            // path with it. Tick Save as well when the write must be held back too.
            CollectionAssert.Contains(_repo.SaveCalls, "Good");
            Assert.AreEqual(42, JsonUtility.FromJson<Score>(Encoding.UTF8.GetString(_repo.Store["Good"])).Value);
        });

        [UnityTest]
        public IEnumerator IgnoreSave_LoadsRealDataButNeverWrites() => UniTask.ToCoroutine(async () =>
        {
            _repo.Store["Good"] = Json(500);
            PlantEntryWithFlags("Good", DataEntryIgnoreFlags.Save);
            LogAssert.ignoreFailingMessages = true;

            await _bundle.LoadAllAsync();
            Assert.AreEqual(500, _bundle.Good.CurrentValue.Value, "Save-only ignore must still load real data");
            Assert.AreEqual(DataEntryStatus.Loaded, _bundle.Good.Status);

            _bundle.Good.CurrentValue.Value = 1;
            await _bundle.SaveAllAsync();

            CollectionAssert.DoesNotContain(_repo.SaveCalls, "Good");
        });

        [UnityTest]
        public IEnumerator GetKeyFor_ReturnsPropertyName_AndNullForForeignEntry() => UniTask.ToCoroutine(async () =>
        {
            await _bundle.LoadAllAsync(); // instantiates the auto-properties

            Assert.AreEqual("Good", _bundle.GetKeyFor(_bundle.Good));
            Assert.IsNull(_bundle.GetKeyFor(new DataEntry<Score>()));
        });

        [UnityTest]
        public IEnumerator DataKey_OverridesPropertyName_ForBothLoadAndSave() => UniTask.ToCoroutine(async () =>
        {
            var keyed = new KeyedBundle { Repository = _repo };
            _repo.Store["legacy_key"] = Json(4);

            await keyed.LoadAllAsync();
            Assert.AreEqual(4, keyed.RenamedSinceRelease.CurrentValue.Value,
                "must read the pinned key, not the property name");

            keyed.RenamedSinceRelease.CurrentValue.Value = 6;
            await keyed.SaveAllAsync();

            CollectionAssert.AreEqual(new[] { "legacy_key" }, _repo.SaveCalls);
            CollectionAssert.DoesNotContain(_repo.Store.Keys, "RenamedSinceRelease");
            Assert.AreEqual("legacy_key", keyed.GetKeyFor(keyed.RenamedSinceRelease));
        });
    }
}
