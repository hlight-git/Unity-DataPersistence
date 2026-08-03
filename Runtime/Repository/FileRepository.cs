using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.DataPersistence
{
    /// <summary>
    /// File-based repository rooted at <see cref="Application.persistentDataPath"/>.
    /// Stores raw bytes — file extension is arbitrary; the serializer decides format.
    /// Marked <see cref="SupportsEditorIOAttribute"/> because
    /// <see cref="Application.persistentDataPath"/> is available in the Unity Editor.
    ///
    /// <para>
    /// <b>On-disk layout:</b> every saved file is <c>[payload bytes][CRC32 trailer 4B]</c>.
    /// The trailer lets <see cref="LoadAsync"/> detect partially-written files and reject
    /// corrupt ones without surfacing junk to the deserializer.
    /// </para>
    ///
    /// <para>
    /// <b>Crash-safe writes:</b> <see cref="SaveAsync"/> uses the write-temp + flush +
    /// atomic-rename pattern. Bytes (with CRC trailer) are written to <c>key.ext.tmp</c>,
    /// flushed to disk via <see cref="FileStream.Flush(bool)"/>, then committed to the
    /// final path with <see cref="File.Replace(string, string, string)"/> (atomic swap)
    /// or <see cref="File.Move(string, string)"/> (atomic rename). At any crash point
    /// the final file is either the previous version or the new version — never partial.
    /// </para>
    ///
    /// <para>
    /// <b>Tmp recovery on load:</b> if a save flushes the tmp file but is killed before
    /// the rename (the tiny window between <c>Flush(true)</c> returning and the rename
    /// syscall), the new data sits orphaned in <c>key.ext.tmp</c>. <see cref="LoadAsync"/>
    /// detects this — if the tmp's CRC is valid it is promoted to the final path before
    /// reading; if invalid it is treated as a partial write and deleted. Combined with
    /// atomic rename, this closes the last gap: a save that successfully reaches disk
    /// will always be visible on the next load.
    /// </para>
    /// </summary>
    [SupportsEditorIO]
    public class FileRepository : ARepository
    {
        private const string TempSuffix  = ".tmp";
        private const int    TrailerSize = sizeof(uint); // CRC32

        [SerializeField] private string _subDirectory  = "SaveData";
        [SerializeField] private string _fileExtension = ".dat";

        private string RootPath               => Path.Combine(Application.persistentDataPath, _subDirectory);
        private string PathFor(string key)     => Path.Combine(RootPath, key + _fileExtension);
        private string TempPathFor(string key) => PathFor(key) + TempSuffix;

        public override async UniTask<byte[]> LoadAsync(string key)
        {
            var finalPath = PathFor(key);
            var tempPath  = TempPathFor(key);

            await UniTask.SwitchToThreadPool();
            try
            {
                // Recovery pass — a leftover tmp with a valid CRC is a save that flushed
                // but never made it through the rename. Promote it to finalPath so the
                // app sees the freshest committed-to-disk data.
                if (File.Exists(tempPath))
                {
                    if (TryReadAndVerify(tempPath, out var orphanPayload))
                    {
                        if (File.Exists(finalPath))
                            File.Replace(tempPath, finalPath, destinationBackupFileName: null);
                        else
                            File.Move(tempPath, finalPath);
                        return orphanPayload;
                    }
                    // Partial / corrupt tmp — discard so it doesn't poison future loads.
                    try { File.Delete(tempPath); } catch { /* ignore */ }
                }

                if (!File.Exists(finalPath)) return null;

                // Throw rather than return null: null means "nothing saved yet" and would
                // make the entry seed defaults and overwrite the damaged file on next save.
                // Covers both a CRC mismatch and an unreadable file (locked, permissions) —
                // in either case the bytes exist and must not be clobbered.
                if (!TryReadAndVerify(finalPath, out var payload))
                    throw new InvalidDataException($"[{nameof(FileRepository)}] '{key}' could not be read or failed its CRC check: {finalPath}");

                return payload;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
            }
        }

        public override async UniTask SaveAsync(string key, byte[] bytes)
        {
            // Resolve every path on the main thread — Application.persistentDataPath
            // is main-thread only.
            var root      = RootPath;
            var finalPath = PathFor(key);
            var tempPath  = TempPathFor(key);

            await UniTask.SwitchToThreadPool();
            try
            {
                Directory.CreateDirectory(root);
                var trailer = BitConverter.GetBytes(Crc32(bytes, 0, bytes.Length));

                // Write payload + CRC trailer to tmp and force the bytes down to disk
                // (fsync on POSIX, FlushFileBuffers on Windows). FileShare.None makes a
                // concurrent save to the same key fail fast instead of interleaving.
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await fs.WriteAsync(bytes,   0, bytes.Length);
                    await fs.WriteAsync(trailer, 0, trailer.Length);
                    fs.Flush(flushToDisk: true);
                }

                // Atomic commit. Replace performs an atomic inode swap when the target
                // exists; Move does an atomic rename for the first save. Either way, a
                // crash during this step leaves the final file in a complete state.
                if (File.Exists(finalPath))
                    File.Replace(tempPath, finalPath, destinationBackupFileName: null);
                else
                    File.Move(tempPath, finalPath);
            }
            catch
            {
                // Best-effort tmp cleanup; the outer save failure is what matters and is
                // already reported by the caller.
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* ignore */ }
                throw;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
            }
        }

        public override async UniTask DeleteAsync(string key)
        {
            var path = PathFor(key);
            var tmp  = TempPathFor(key);
            if (!File.Exists(path) && !File.Exists(tmp)) return;
            await UniTask.SwitchToThreadPool();
            try
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(tmp))  File.Delete(tmp);  // clean up any stale temp
            }
            finally { await UniTask.SwitchToMainThread(); }
        }

        /// <inheritdoc/>
        public override async UniTask ClearAsync()
        {
            var root = RootPath; // main-thread only — resolve before switching
            if (!Directory.Exists(root)) return;
            await UniTask.SwitchToThreadPool();
            try   { Directory.Delete(root, recursive: true); }
            finally { await UniTask.SwitchToMainThread(); }
        }

        /// <summary>
        /// Reads the file at <paramref name="path"/>, verifies its CRC32 trailer, and
        /// returns the payload portion (bytes minus trailer). Returns <c>false</c> for
        /// any read error, files shorter than the trailer, or CRC mismatch.
        /// </summary>
        private static bool TryReadAndVerify(string path, out byte[] payload)
        {
            payload = null;
            byte[] all;
            try   { all = File.ReadAllBytes(path); }
            catch { return false; }

            if (all.Length < TrailerSize) return false;

            var payloadLen = all.Length - TrailerSize;
            var storedCrc  = BitConverter.ToUInt32(all, payloadLen);
            if (Crc32(all, 0, payloadLen) != storedCrc) return false;

            payload = new byte[payloadLen];
            Buffer.BlockCopy(all, 0, payload, 0, payloadLen);
            return true;
        }

        // Standard CRC32 (IEEE 802.3 polynomial, reflected). Cheap — covers the
        // single accident we care about: tmp file with truncated/garbled tail.
        private static readonly uint[] _crcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var c = i;
                for (var j = 0; j < 8; j++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data, int offset, int count)
        {
            var crc = 0xFFFFFFFFu;
            for (var i = offset; i < offset + count; i++)
                crc = _crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }
    }
}
