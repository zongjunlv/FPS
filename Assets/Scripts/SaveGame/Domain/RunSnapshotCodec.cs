using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

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
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var loaded = new DataContractJsonSerializer(typeof(RunSnapshot)).ReadObject(stream) as RunSnapshot;
                    if (!SnapshotValidation.TryValidate(loaded, out error)) return false;
                    if (!string.Equals(loaded.Checksum, SnapshotChecksum.Compute(loaded), StringComparison.Ordinal))
                    {
                        error = "存档校验失败，内容可能已经损坏。";
                        return false;
                    }
                    snapshot = loaded;
                    return true;
                }
            }
            catch (Exception exception) when (exception is SerializationException || exception is XmlException
                || exception is ArgumentException || exception is FormatException || exception is OverflowException)
            {
                error = "无法解析存档：" + exception.Message;
                return false;
            }
        }
    }
}
