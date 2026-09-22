using UnityEditor;
using UnityEngine;

public static class BattleMusicImportSettings
{
    private static readonly string[] ClipPaths =
    {
        "Assets/Resources/Audio/Music/Singularity_calm.ogg",
        "Assets/Resources/Audio/Music/Singularity_action.ogg"
    };

    [MenuItem("FPS/Audio/Configure Battle Music Import")]
    public static void Configure()
    {
        foreach (string path in ClipPaths)
        {
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
            {
                Debug.LogError($"[BattleMusic] Audio importer not found: {path}");
                continue;
            }

            AudioImporterSampleSettings settings =
                importer.defaultSampleSettings;
            if (settings.loadType == AudioClipLoadType.Streaming &&
                settings.compressionFormat == AudioCompressionFormat.Vorbis &&
                Mathf.Approximately(settings.quality, 0.7f) &&
                importer.loadInBackground && settings.preloadAudioData)
            {
                continue;
            }

            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = true;
            importer.SaveAndReimport();
        }

        AssetDatabase.SaveAssets();
    }
}
