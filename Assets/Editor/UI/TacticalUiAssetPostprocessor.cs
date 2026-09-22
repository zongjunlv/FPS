using UnityEditor;

internal sealed class TacticalUiAssetPostprocessor : AssetPostprocessor
{
    private const string IconRoot =
        "Assets/Resources/UI/Kenney/GameIcons/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(IconRoot,
                System.StringComparison.Ordinal))
            return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = UnityEngine.FilterMode.Bilinear;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.maxTextureSize = 256;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }
}

