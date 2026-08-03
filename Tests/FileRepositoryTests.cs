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
    /// Covers the on-disk contract: CRC-guarded payloads, orphaned-tmp recovery, and the
    /// thread-affinity of every path lookup (persistentDataPath is main-thread only, so a
    /// regression here throws rather than silently misbehaving).
    /// </summary>
    public class FileRepositoryTests
    {
        private const string SubDir    = "DataPersistenceTests";
        private const string Extension = ".dat";

        private FileRepository _repo;
        private string         Root                 => Path.Combine(Application.persistentDataPath, SubDir);
        private string         PathFor(string key)  => Path.Combine(Root, key + Extension);
        private string         TempFor(string key)  => PathFor(key) + ".tmp";

        [SetUp]
        public void SetUp()
        {
            _repo = ScriptableObject.CreateInstance<FileRepository>();
            // No runtime need to reconfigure these, so they stay [SerializeField]-only;
            // SerializedObject is the seam for tests.
            var so = new SerializedObject(_repo);
            so.FindProperty("_subDirectory").stringValue  = SubDir;
            so.FindProperty("_fileExtension").stringValue = Extension;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_repo);
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static byte[] Payload(string s) => Encoding.UTF8.GetBytes(s);

        /// <summary>Write a file the way SaveAsync does: payload + little-endian CRC32 trailer.</summary>
        private void WriteWithValidTrailer(string path, byte[] payload)
        {
            Directory.CreateDirectory(Root);
            uint crc = 0xFFFFFFFFu;
            foreach (var b in payload)
            {
                crc ^= b;
                for (var i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            fs.Write(payload, 0, payload.Length);
            fs.Write(System.BitConverter.GetBytes(~crc), 0, 4);
        }

        [UnityTest]
        public IEnumerator SaveThenLoad_RoundTripsPayload() => UniTask.ToCoroutine(async () =>
        {
            await _repo.SaveAsync("k", Payload("hello"));
            var loaded = await _repo.LoadAsync("k");

            Assert.AreEqual("hello", Encoding.UTF8.GetString(loaded));
            Assert.IsFalse(File.Exists(TempFor("k")), "tmp must not survive a successful save");
        });

        [UnityTest]
        public IEnumerator Load_MissingKey_ReturnsNull() => UniTask.ToCoroutine(async () =>
        {
            Assert.IsNull(await _repo.LoadAsync("nope"));
        });

        [UnityTest]
        public IEnumerator Load_CorruptFile_Throws() => UniTask.ToCoroutine(async () =>
        {
            await _repo.SaveAsync("k", Payload("hello"));

            var bytes = File.ReadAllBytes(PathFor("k"));
            bytes[0] ^= 0xFF; // flip a payload bit — trailer no longer matches
            File.WriteAllBytes(PathFor("k"), bytes);

            // Must throw, not return null: null would read as "first run" upstream and the
            // damaged file would be overwritten with defaults on the next save.
            try
            {
                await _repo.LoadAsync("k");
                Assert.Fail("expected InvalidDataException");
            }
            catch (InvalidDataException) { /* expected */ }
        });

        [UnityTest]
        public IEnumerator Load_TruncatedFile_Throws() => UniTask.ToCoroutine(async () =>
        {
            await _repo.SaveAsync("k", Payload("hello"));
            var bytes = File.ReadAllBytes(PathFor("k"));
            File.WriteAllBytes(PathFor("k"), bytes[..2]); // shorter than the trailer

            try
            {
                await _repo.LoadAsync("k");
                Assert.Fail("expected InvalidDataException");
            }
            catch (InvalidDataException) { /* expected */ }
        });

        [UnityTest]
        public IEnumerator Load_OrphanTempWithValidCrc_IsPromoted() => UniTask.ToCoroutine(async () =>
        {
            // Simulates a crash between fsync and rename: the newest data is only in .tmp.
            await _repo.SaveAsync("k", Payload("old"));
            WriteWithValidTrailer(TempFor("k"), Payload("new"));

            var loaded = await _repo.LoadAsync("k");

            Assert.AreEqual("new", Encoding.UTF8.GetString(loaded));
            Assert.IsFalse(File.Exists(TempFor("k")), "tmp is consumed by the promotion");
            Assert.AreEqual("new", Encoding.UTF8.GetString(await _repo.LoadAsync("k")), "promotion must persist");
        });

        [UnityTest]
        public IEnumerator Load_OrphanTempCorrupt_IsDiscardedAndFinalSurvives() => UniTask.ToCoroutine(async () =>
        {
            await _repo.SaveAsync("k", Payload("good"));
            Directory.CreateDirectory(Root);
            File.WriteAllBytes(TempFor("k"), Payload("partial write, no trailer"));

            var loaded = await _repo.LoadAsync("k");

            Assert.AreEqual("good", Encoding.UTF8.GetString(loaded));
            Assert.IsFalse(File.Exists(TempFor("k")), "a partial tmp must be deleted, not left to poison later loads");
        });

        [UnityTest]
        public IEnumerator Save_OverwritesExistingKeyAtomically() => UniTask.ToCoroutine(async () =>
        {
            await _repo.SaveAsync("k", Payload("v1"));
            await _repo.SaveAsync("k", Payload("v2"));

            Assert.AreEqual("v2", Encoding.UTF8.GetString(await _repo.LoadAsync("k")));
            Assert.IsFalse(File.Exists(TempFor("k")));
        });

        [UnityTest]
        public IEnumerator DeleteAsync_RemovesFileAndTemp() => UniTask.ToCoroutine(async () =>
        {
            await _repo.SaveAsync("k", Payload("x"));
            File.WriteAllBytes(TempFor("k"), Payload("stale"));

            await _repo.DeleteAsync("k");

            Assert.IsFalse(File.Exists(PathFor("k")));
            Assert.IsFalse(File.Exists(TempFor("k")));
        });

        [UnityTest]
        public IEnumerator ClearAsync_RemovesRootDirectory() => UniTask.ToCoroutine(async () =>
        {
            await _repo.SaveAsync("a", Payload("1"));
            await _repo.SaveAsync("b", Payload("2"));

            // Regression guard: resolving RootPath after the thread-pool switch throws
            // "can only be called from the main thread".
            await _repo.ClearAsync();

            Assert.IsFalse(Directory.Exists(Root));
        });
    }
}
