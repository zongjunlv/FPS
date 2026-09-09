using System;
using System.IO;
using System.Security;
using System.Text;

namespace FPS.SaveGame
{
    public enum SnapshotStatus { Success, MissingFile, InvalidData, IOError }

    public sealed class SnapshotResult
    {
        public bool Success => Status == SnapshotStatus.Success;
        public SnapshotStatus Status { get; }
        public string Message { get; }
        public RunSnapshot Snapshot { get; }

        public SnapshotResult(SnapshotStatus status, string message, RunSnapshot snapshot = null)
        {
            Status = status;
            Message = message;
            Snapshot = snapshot;
        }
    }

    /// <summary>The caller chooses the local path; this layer has no Unity dependency.</summary>
    public sealed class RunSnapshotStore
    {
        public string FilePath { get; }

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
                File.WriteAllText(FilePath, json, new UTF8Encoding(false));
                return new SnapshotResult(SnapshotStatus.Success, "局内快照已保存。", snapshot);
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                return new SnapshotResult(SnapshotStatus.IOError, "保存失败：" + exception.Message);
            }
        }

        public SnapshotResult Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new SnapshotResult(SnapshotStatus.MissingFile, "没有可加载的局内快照。");
                if (new FileInfo(FilePath).Length > RunSnapshotCodec.MaximumFileBytes)
                    return new SnapshotResult(SnapshotStatus.InvalidData, "存档超出大小限制。");
                var json = File.ReadAllText(FilePath, Encoding.UTF8);
                return RunSnapshotCodec.TryDeserialize(json, out var snapshot, out var error)
                    ? new SnapshotResult(SnapshotStatus.Success, "局内快照已加载。", snapshot)
                    : new SnapshotResult(SnapshotStatus.InvalidData, error);
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                return new SnapshotResult(SnapshotStatus.IOError, "读取失败：" + exception.Message);
            }
        }

        private static bool IsFileError(Exception exception) => exception is IOException
            || exception is UnauthorizedAccessException || exception is SecurityException;
    }
}
