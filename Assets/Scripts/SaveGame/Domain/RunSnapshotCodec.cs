using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace FPS.SaveGame
{
    public static class RunSnapshotCodec
    {
        public const int MaximumFileBytes = 1024 * 1024;

        public static string Serialize(RunSnapshot snapshot)
        {
            if (!SnapshotValidation.TryValidate(snapshot, out var error)) throw new InvalidDataException(error);
            snapshot.Checksum = SnapshotChecksum.Compute(snapshot);
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(RunSnapshot)).WriteObject(stream, snapshot);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static bool TryDeserialize(string json, out RunSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
            {
                error = "存档为空或超出大小限制。";
                return false;
            }
            SnapshotDecodeResult result = SnapshotMigration.Decode(json);
            snapshot = result.Snapshot;
            error = result.Error;
            return result.Success;
        }

        public static SnapshotDecodeResult Decode(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
                return SnapshotDecodeResult.Failed("存档为空或超出大小限制。");
            return SnapshotMigration.Decode(json);
        }
    }
}
