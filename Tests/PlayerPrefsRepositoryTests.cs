using System.Collections;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hlight.DataPersistence.Tests
{
    /// <summary>
    /// Covers the PlayerPrefs backend: Base64 round trip, the corrupt-vs-absent split every
    /// repository owes its caller, and the key index that keeps <c>ClearAsync</c> from
    /// behaving like <c>PlayerPrefs.DeleteAll()</c>.
    /// </summary>
    public class PlayerPrefsRepositoryTests
    {
        private const string Prefix   = "DataPersistenceTests/";
        private const string Outsider = "SomeOtherSystemsKey";

        private PlayerPrefsRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _repo = ScriptableObject.CreateInstance<PlayerPrefsRepository>();
            var so = new SerializedObject(_repo);
            so.FindProperty("_keyPrefix").stringValue = Prefix;
            so.ApplyModifiedPropertiesWithoutUndo();

            PlayerPrefs.DeleteKey(Outsider);
        }

        [TearDown]
        public void TearDown()
        {
            _repo.ClearAsync().Forget(); // synchronous under the hood
            PlayerPrefs.DeleteKey(Outsider);
            PlayerPrefs.Save();
            Object.DestroyImmediate(_repo);
        }

        private static byte[] Payload(string s) => Encoding.UTF8.GetBytes(s);

        [UnityTest]
        public IEnumerator SaveThenLoad_RoundTripsPayload() => UniTask.ToCoroutine(async () =>
        {
            await _repo.SaveAsync("k", Payload("hello"));

            Assert.AreEqual("hello", Encoding.UTF8.GetString(await _repo.LoadAsync("k")));
        });

        [UnityTest]
        public IEnumerator Load_MissingKey_ReturnsNull() => UniTask.ToCoroutine(async () =>
        {
            Assert.IsNull(await _repo.LoadAsync("nope"));
        });

        [UnityTest]
        public IEnumerator Load_CorruptValue_Throws() => UniTask.ToCoroutine(async () =>
        {
            PlayerPrefs.SetString(Prefix + "k", "not!valid!base64!");

            // Must throw, not return null: null reads as "first run" upstream and the
            // damaged value would be overwritten with defaults on the next save.
            try
            {
                await _repo.LoadAsync("k");
                Assert.Fail("expected InvalidDataException");
            }
            catch (InvalidDataException) { /* expected */ }
        });

        [UnityTest]
        public IEnumerator DeleteAsync_RemovesKey() => UniTask.ToCoroutine(async () =>
        {
            await _repo.SaveAsync("k", Payload("x"));

            await _repo.DeleteAsync("k");

            Assert.IsNull(await _repo.LoadAsync("k"));
            Assert.IsFalse(PlayerPrefs.HasKey(Prefix + "k"));
        });

        [UnityTest]
        public IEnumerator ClearAsync_RemovesOwnKeysOnly() => UniTask.ToCoroutine(async () =>
        {
            PlayerPrefs.SetString(Outsider, "belongs to something else");
            await _repo.SaveAsync("a", Payload("1"));
            await _repo.SaveAsync("b", Payload("2"));

            await _repo.ClearAsync();

            Assert.IsNull(await _repo.LoadAsync("a"));
            Assert.IsNull(await _repo.LoadAsync("b"));
            Assert.AreEqual("belongs to something else", PlayerPrefs.GetString(Outsider),
                "ClearAsync must scope to this repository's index, never DeleteAll");
        });

        [UnityTest]
        public IEnumerator ClearAsync_AfterDelete_LeavesNoStaleIndexEntry() => UniTask.ToCoroutine(async () =>
        {
            await _repo.SaveAsync("a", Payload("1"));
            await _repo.SaveAsync("b", Payload("2"));
            await _repo.DeleteAsync("a");

            await _repo.ClearAsync();

            Assert.IsFalse(PlayerPrefs.HasKey(Prefix + "__index"), "index key itself must be removed");
            Assert.IsFalse(PlayerPrefs.HasKey(Prefix + "b"));
        });
    }
}
