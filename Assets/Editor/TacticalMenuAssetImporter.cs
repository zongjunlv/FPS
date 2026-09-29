using UnityEditor;
using UnityEngine;

// Keep third-party menu art import settings reproducible on fresh clones.
internal sealed class TacticalMenuAssetImporter : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/UI/TacticalMenu/"))
            return;

        if (assetImporter is not TextureImporter importer)
            return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = assetPath.EndsWith("lobby-backdrop.png")
            ? Vector4.zero
            : new Vector4(14f, 14f, 14f, 14f);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048;
    }
}
