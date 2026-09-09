using System;
using System.IO;
using System.Security;
using System.Text;

namespace FPS.SaveGame
{
    public enum SnapshotStatus { Success, MissingFile, InvalidData, IOError }
    public enum SnapshotLoadSource { None, Primary, Backup }
    public enum SnapshotLoadDisposition { None, Current, Migrated, RecoveredFromBackup, RecoveredFromBackupAndMigrated }

    public sealed class SnapshotResult
    {
        public bool Success => Status == SnapshotStatus.Success;
        public SnapshotStatus Status { get; }
        public string Message { get; }
        public RunSnapshot Snapshot { get; }
        public SnapshotLoadSource Source { get; }
        public SnapshotLoadDisposition LoadDisposition { get; }
        public int OriginalSchemaVersion { get; }
        public bool WasMigrated => LoadDisposition == SnapshotLoadDisposition.Migrated ||
            LoadDisposition == SnapshotLoadDisposition.RecoveredFromBackupAndMigrated;
        public bool RecoveredFromBackup => Source == SnapshotLoadSource.Backup;
        public string CorruptFilePath { get; }

        public SnapshotResult(SnapshotStatus status, string message, RunSnapshot snapshot = null,
            SnapshotLoadSource source = SnapshotLoadSource.None,
            SnapshotLoadDisposition loadDisposition = SnapshotLoadDisposition.None,
            int originalSchemaVersion = 0, string corruptFilePath = null)
        {
            Status = status;
            Message = message;
            Snapshot = snapshot;
            Source = source;
            LoadDisposition = loadDisposition;
            OriginalSchemaVersion = originalSchemaVersion;
            CorruptFilePath = corruptFilePath;
        }
    }

    /// <summary>The caller chooses the local path; this layer has no Unity dependency.</summary>
    public sealed class RunSnapshotStore
    {
        public string FilePath { get; }
        public string BackupPath => FilePath + ".bak";
        public string TemporaryPath => FilePath + ".tmp";

        public RunSnapshotStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("存档路径不能为空。", nameof(filePath));
            FilePath = Path.GetFullPath(filePath);
        }

        public SnapshotResult Save(RunSnapshot snapshot)
        {
            // Validate and serialize before touching an existing save file.
            if (!SnapshotValidation.TryValidate(snapshot, out var error))
                return new SnapshotResult(SnapshotStatus.InvalidData, error);
            var json = RunSnapshotCodec.Serialize(snapshot);
            if (Encoding.UTF8.GetByteCount(json) > RunSnapshotCodec.MaximumFileBytes)
                return new SnapshotResult(SnapshotStatus.InvalidData, "存档超出大小限制。");
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                WriteDurableTemporary(json);
                SnapshotDecodeResult staged = Read(TemporaryPath);
                if (!staged.Success)
                    return new SnapshotResult(SnapshotStatus.InvalidData, "临时存档复验失败：" + staged.Error);

                string corruptPath = null;
                if (!File.Exists(FilePath))
                {
                    File.Move(TemporaryPath, FilePath);
                }
                else if (Read(FilePath).Success)
                {
                    // Only a verified primary is allowed to become the new backup.
                    File.Replace(TemporaryPath, FilePath, BackupPath);
                }
                else
                {
                    // Preserve invalid bytes separately; never overwrite the last known-good backup.
                    corruptPath = NextCorruptPath();
                    File.Copy(FilePath, corruptPath);
                    File.Replace(TemporaryPath, FilePath, null);
                }
                return new SnapshotResult(SnapshotStatus.Success, "局内快照已保存。", snapshot,
                    corruptFilePath: corruptPath);
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                return new SnapshotResult(SnapshotStatus.IOError, "保存失败：" + exception.Message);
            }
            finally
            {
                TryDeleteTemporary();
            }
        }

        public SnapshotResult Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return File.Exists(BackupPath) ? LoadBackup(null) :
                        new SnapshotResult(SnapshotStatus.MissingFile, "没有可加载的局内快照。");

                SnapshotDecodeResult primary = Read(FilePath);
                if (primary.Success) return Loaded(primary, SnapshotLoadSource.Primary, null);
                return File.Exists(BackupPath)
                    ? LoadBackup(primary.Error)
                    : PreserveUnrecoverablePrimary(primary.Error);
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                return new SnapshotResult(SnapshotStatus.IOError, "读取失败：" + exception.Message);
            }
        }

        private SnapshotResult LoadBackup(string primaryError)
        {
            SnapshotDecodeResult backup = Read(BackupPath);
            if (!backup.Success)
                return PreserveUnrecoverablePrimary(primaryError ?? backup.Error);

            string corruptPath = null;
            if (File.Exists(FilePath))
            {
                corruptPath = NextCorruptPath();
                File.Move(FilePath, corruptPath);
            }
            RestorePrimaryFromBackup();
            return Loaded(backup, SnapshotLoadSource.Backup, corruptPath);
        }

        private SnapshotResult PreserveUnrecoverablePrimary(string error)
        {
            string corruptPath = null;
            if (File.Exists(FilePath))
            {
                corruptPath = NextCorruptPath();
                File.Move(FilePath, corruptPath);
            }
            return new SnapshotResult(SnapshotStatus.InvalidData, error, corruptFilePath: corruptPath);
        }

        private SnapshotResult Loaded(SnapshotDecodeResult decoded, SnapshotLoadSource source, string corruptPath)
        {
            SnapshotLoadDisposition disposition;
            if (source == SnapshotLoadSource.Backup)
                disposition = decoded.WasMigrated ? SnapshotLoadDisposition.RecoveredFromBackupAndMigrated : SnapshotLoadDisposition.RecoveredFromBackup;
            else
                disposition = decoded.WasMigrated ? SnapshotLoadDisposition.Migrated : SnapshotLoadDisposition.Current;
            string message = source == SnapshotLoadSource.Backup ? "主存档损坏，已从最近有效备份恢复。" :
                decoded.WasMigrated ? "旧版局内快照已迁移并加载。" : "局内快照已加载。";
            return new SnapshotResult(SnapshotStatus.Success, message, decoded.Snapshot, source,
                disposition, decoded.OriginalSchemaVersion, corruptPath);
        }

        private SnapshotDecodeResult Read(string path)
        {
            if (new FileInfo(path).Length > RunSnapshotCodec.MaximumFileBytes)
                return SnapshotDecodeResult.Failed("存档超出大小限制。");
            return RunSnapshotCodec.Decode(File.ReadAllText(path, Encoding.UTF8));
        }

        private void WriteDurableTemporary(string json)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            using (var stream = new FileStream(TemporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private void RestorePrimaryFromBackup()
        {
            using (FileStream source = new FileStream(BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream target = new FileStream(TemporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                source.CopyTo(target);
                target.Flush(true);
            }
            File.Move(TemporaryPath, FilePath);
        }

        private string NextCorruptPath()
        {
            string prefix = FilePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            string candidate = prefix;
            int suffix = 1;
            while (File.Exists(candidate)) candidate = prefix + "-" + suffix++;
            return candidate;
        }

        private void TryDeleteTemporary()
        {
            try { if (File.Exists(TemporaryPath)) File.Delete(TemporaryPath); }
            catch (Exception exception) when (IsFileError(exception)) { }
        }

        private static bool IsFileError(Exception exception) => exception is IOException
            || exception is UnauthorizedAccessException || exception is SecurityException;
    }
}
