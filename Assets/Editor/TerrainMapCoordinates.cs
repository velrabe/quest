using UnityEngine;

/// <summary>
/// Единая геопривязка Design-карт (1254²) к мировым координатам Unity.
/// Калибровка по двум якорям: водохранилище и маяк на NatureHeightmap / terrain-texture.
/// </summary>
public static class TerrainMapCoordinates
{
    public const float TerrainWidth = 300f;
    public const float TerrainLength = 300f;
    public const float TerrainHalf = 150f;

    // map u = (wx + MapUOffset) / MapUScale
    // map v = (wz + MapVOffset) / MapVScale
    public const float MapUScale = 369.128f;
    public const float MapUOffset = 193.423f;
    public const float MapVScale = 340.031f;
    public const float MapVOffset = 201.947f;

    public static float WorldToMapU(float wx) => (wx + MapUOffset) / MapUScale;
    public static float WorldToMapV(float wz) => (wz + MapVOffset) / MapVScale;

    public static void PixelToWorld(int x, int z, int res, out float wx, out float wz)
    {
        float u = x / (float)(res - 1);
        float v = z / (float)(res - 1);
        wx = -TerrainHalf + u * TerrainWidth;
        wz = -TerrainHalf + v * TerrainLength;
    }

    public static float SampleBilinear01(Texture2D tex, float u, float v)
    {
        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);
        return tex.GetPixelBilinear(u, v).grayscale;
    }

    public static Color SampleColorBilinear01(Texture2D tex, float u, float v)
    {
        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);
        return tex.GetPixelBilinear(u, v);
    }
}
