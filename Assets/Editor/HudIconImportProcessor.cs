using UnityEditor;

public sealed class HudIconImportProcessor : AssetPostprocessor
{
    private const string HudIconRoot = "Assets/Resources/UI/Icons/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(HudIconRoot))
        {
            return;
        }

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
    }
}
