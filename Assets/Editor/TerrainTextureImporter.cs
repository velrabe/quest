using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;

public static class TerrainTextureImporter
{
    const string PaintMapPath = "Assets/Terrain/terrain-texture.png";
    const string LayerPath = "Assets/Terrain/Layers/PaintMap.terrainlayer";
    const string TerrainDataPath = "Assets/Terrain/TerrainData.asset";
    const string LogPath = "Assets/Terrain/texture-import-result.txt";

    const int AlphamapResolution = 512;

    [MenuItem("Tools/Terrain/Import Terrain Textures")]
    public static void ImportTerrainTexturesMenu() => ImportTerrainTextures(silent: false);

    public static void ImportTerrainTexturesSilent() => ImportTerrainTextures(silent: true);

    public static void ImportTerrainTextures(bool silent)
    {
        var paintTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PaintMapPath);
        var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);

        if (paintTex == null) { Report("paint map not found", silent); return; }
        if (terrainData == null) { Report("TerrainData not found", silent); return; }

        FixPaintTextureImport(PaintMapPath);
        paintTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PaintMapPath);

        var layer = EnsurePaintLayer(paintTex);
        terrainData.alphamapResolution = AlphamapResolution;
        terrainData.terrainLayers = new[] { layer };

        var alpha = new float[AlphamapResolution, AlphamapResolution, 1];
        for (int z = 0; z < AlphamapResolution; z++)
            for (int x = 0; x < AlphamapResolution; x++)
                alpha[z, x, 0] = 1f;

        terrainData.SetAlphamaps(0, 0, alpha);

        EditorUtility.SetDirty(terrainData);
        AssetDatabase.SaveAssets();

        var log = new StringBuilder();
        log.AppendLine("OK");
        log.AppendLine($"Paint map: {paintTex.width}×{paintTex.height}");
        log.AppendLine($"Layer tileSize: {layer.tileSize.x:F1}×{layer.tileSize.y:F1} m");
        log.AppendLine($"Layer tileOffset: {layer.tileOffset.x:F4}, {layer.tileOffset.y:F4}");
        log.AppendLine("Mode: single 1× paint overlay (no splat tiling)");
        File.WriteAllText(LogPath, log.ToString());
        if (!silent) EditorUtility.DisplayDialog("Terrain Textures", log.ToString(), "OK");
    }

    static void Report(string msg, bool silent)
    {
        var full = "ERROR: " + msg;
        File.WriteAllText(LogPath, full);
        if (!silent) EditorUtility.DisplayDialog("Terrain Textures", full, "OK");
        Debug.LogError("[TerrainTextureImporter] " + full);
    }

    /// <summary>
    /// tileSize/tileOffset подобраны так, чтобы UV слоя совпадали с TerrainMapCoordinates
    /// (world → map u/v), как и при семплинге heightmap.
    /// </summary>
    static TerrainLayer EnsurePaintLayer(Texture2D paintTex)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Terrain/Layers"))
            AssetDatabase.CreateFolder("Assets/Terrain", "Layers");

        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, LayerPath);
        }

        layer.name = "PaintMap";
        layer.diffuseTexture = paintTex;
        layer.normalMapTexture = null;
        layer.maskMapTexture = null;
        layer.tileSize = new Vector2(TerrainMapCoordinates.MapUScale, TerrainMapCoordinates.MapVScale);
        layer.tileOffset = new Vector2(
            (TerrainMapCoordinates.MapUOffset - TerrainMapCoordinates.TerrainHalf) / TerrainMapCoordinates.MapUScale,
            (TerrainMapCoordinates.MapVOffset - TerrainMapCoordinates.TerrainHalf) / TerrainMapCoordinates.MapVScale);
        layer.smoothness = 0f;
        layer.metallic = 0f;

        EditorUtility.SetDirty(layer);
        return layer;
    }

    static void FixPaintTextureImport(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        bool changed = false;
        if (importer.maxTextureSize < 2048) { importer.maxTextureSize = 2048; changed = true; }
        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
        if (importer.wrapModeU != TextureWrapMode.Clamp)
        { importer.wrapModeU = TextureWrapMode.Clamp; changed = true; }
        if (importer.wrapModeV != TextureWrapMode.Clamp)
        { importer.wrapModeV = TextureWrapMode.Clamp; changed = true; }
        if (!importer.sRGBTexture) { importer.sRGBTexture = true; changed = true; }

        if (changed) importer.SaveAndReimport();
    }
}
