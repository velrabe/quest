using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class HeightmapTerrainImporter
{
    const string NatureHeightmapPath = "Assets/Terrain/NatureHeightmap.png";
    const string TerrainDataPath = "Assets/Terrain/TerrainData.asset";
    const string TriggerPath = "Assets/Terrain/.run-import";
    const string LogPath = "Assets/Terrain/import-result.txt";

    const int HeightmapResolution = 1025;
    const float TerrainWidth = 300f;
    const float TerrainLength = 300f;
    const float TerrainVerticalSize = 80f;
    const float TerrainWorldBaseY = -2f;

    const float AnchorLowMeters = -2f;
    const float AnchorHighMeters = 30f;

    // Плоский террейн: карта видна под ногами, ±1 м микрорельеф
    const bool UseFlatTerrain = true;
    const float FlatBaseMeters = 0f;
    const float FlatAmplitudeMeters = 1f;

    // Сильное сглаживание heightmap — убирает пиксельные «иголки»
    const int MacroRes = 257;
    const int MacroBlurRadius = 3;
    const int MacroBlurPasses = 3;
    const float SpikeRejectGray = 0.015f;
    const float MaxSlopeMeters = 0.55f;
    const int SlopeClampPasses = 12;

    const float PadEdgeBlendM = 1.5f;
    const float RoadEdgeBlendM = 1.2f;

    struct PadRect
    {
        public string Id;
        public float X, Z, W, H;
        public PadRect(string id, float x, float z, float w, float h) { Id = id; X = x; Z = z; W = w; H = h; }
    }

    struct RoadSeg
    {
        public float Ax, Az, Bx, Bz, WidthM;
        public RoadSeg(float ax, float az, float bx, float bz, float w) { Ax = ax; Az = az; Bx = bx; Bz = bz; WidthM = w; }
    }

    struct Site
    {
        public float X, Z, W, H;
        public Site(float x, float z, float w, float h) { X = x; Z = z; W = w; H = h; }
    }

    static readonly PadRect[] Pads = {
        new PadRect("lab",        0f,     0f,   44f, 44f),
        new PadRect("solar",     90f,   -30f,   44f, 34f),
        new PadRect("offices",  100f,  -120f,   38f, 28f),
        new PadRect("abandoned",-90f,   -60f,   32f, 26f),
        new PadRect("entrance",  40f,   -90f,   16f, 16f),
        new PadRect("bridge",   -80f,  -100f,   28f, 10f),
        new PadRect("railway",  -20f,   120f,   10f, 90f),
    };

    static readonly RoadSeg[] Roads = {
        new RoadSeg(   0f,    0f,   40f,  -90f, 3.0f),
        new RoadSeg(   0f,    0f,  -90f,  -60f, 3.0f),
        new RoadSeg(   0f,    0f,   90f,  -30f, 3.0f),
        new RoadSeg(   0f,    0f,    0f,   22f, 3.0f),
        new RoadSeg(   0f,   22f,  -20f,   80f, 3.0f),
        new RoadSeg( -80f, -100f, -110f,-120f, 2.5f),
        new RoadSeg(  90f,  -30f,  100f, -120f, 2.0f),
        new RoadSeg(-110f,   70f,  -20f,  100f, 2.5f),
        new RoadSeg( 110f,  100f,    0f,   30f, 2.0f),
        new RoadSeg(   0f,   30f,    0f,    0f, 2.0f),
        new RoadSeg( -20f,   80f,  -20f,  120f, 3.0f),
    };

    static readonly Site LighthouseAnchor = new Site(110f, 100f, 36f, 36f);
    static readonly Site ReservoirAnchor  = new Site(-110f, -120f, 50f, 38f);

    [InitializeOnLoadMethod]
    static void AutoImportOnTrigger()
    {
        if (!File.Exists(TriggerPath)) return;
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(TriggerPath)) return;
            File.Delete(TriggerPath);
            ImportTerrain(silent: true);
        };
    }

    [MenuItem("Tools/Terrain/Import Terrain")]
    public static void ImportTerrainMenu() => ImportTerrain(silent: false);

    public static void ImportHeightmapToTerrainDataSilent() => ImportTerrain(silent: true);

    [MenuItem("Tools/Terrain/Apply Terrain → Scene")]
    public static void ApplyTerrainToScene()
    {
        ImportTerrain(silent: true);
        TerrainTextureImporter.ImportTerrainTexturesSilent();
        var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (terrainData == null)
        {
            EditorUtility.DisplayDialog("Terrain", "TerrainData не найден.", "OK");
            return;
        }

        var pos = new Vector3(-TerrainWidth * 0.5f, TerrainWorldBaseY, -TerrainLength * 0.5f);
        RemoveTerrainIfExists("Terrain_Buildings");
        RemoveTerrainIfExists("Terrain_Nature");
        var terrain = EnsureSceneTerrain("Terrain_Main", terrainData, pos);
        WorldSiteBuilder.BuildAllSitesSilent();
        Selection.activeGameObject = terrain.gameObject;
        EditorUtility.DisplayDialog("Terrain", "Terrain_Main + WorldSites обновлены.", "OK");
    }

    public static void ImportTerrain(bool silent)
    {
        float[,] heights;
        string heightInfo;

        if (UseFlatTerrain)
        {
            heights = BuildFlatTerrainHeights(HeightmapResolution);
            heightInfo = $"flat ±{FlatAmplitudeMeters}m";
        }
        else
        {
            var heightTex = AssetDatabase.LoadAssetAtPath<Texture2D>(NatureHeightmapPath);
            if (heightTex == null)
            {
                ReportError("heightmap not found", silent);
                return;
            }

            if (heightTex.width != heightTex.height)
            {
                ReportError($"heightmap must be square, got {heightTex.width}×{heightTex.height}", silent);
                return;
            }

            EnsureReadable(heightTex);
            float[,] raw = SampleHeights(heightTex, HeightmapResolution);
            heights = BuildTerrainHeights(raw, HeightmapResolution);
            heightInfo = $"{heightTex.width}×{heightTex.height}";
        }

        var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (terrainData == null)
        {
            terrainData = new TerrainData();
            AssetDatabase.CreateAsset(terrainData, TerrainDataPath);
        }

        terrainData.heightmapResolution = HeightmapResolution;
        terrainData.size = new Vector3(TerrainWidth, TerrainVerticalSize, TerrainLength);
        terrainData.SetHeights(0, 0, heights);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        LogHeightStatsMeters(heights, HeightmapResolution);

        var log = new StringBuilder();
        log.AppendLine("OK");
        log.AppendLine($"Height: {heightInfo}");
        File.WriteAllText(LogPath, log.ToString());
        if (!silent) EditorUtility.DisplayDialog("Terrain", log.ToString(), "OK");
    }

    static float[,] BuildFlatTerrainHeights(int res)
    {
        var meters = new float[res, res];
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                TerrainMapCoordinates.PixelToWorld(x, z, res, out float wx, out float wz);
                float n1 = Mathf.PerlinNoise(wx * 0.06f + 41.2f, wz * 0.06f + 17.8f);
                float n2 = Mathf.PerlinNoise(wx * 0.11f + 90f, wz * 0.11f + 33f);
                float blend = n1 * 0.7f + n2 * 0.3f;
                meters[z, x] = FlatBaseMeters + (blend * 2f - 1f) * FlatAmplitudeMeters;
            }
        }
        return MetersToNorm(meters, res);
    }

    static void ReportError(string msg, bool silent)
    {
        var full = "ERROR: " + msg;
        File.WriteAllText(LogPath, full);
        if (!silent) EditorUtility.DisplayDialog("Terrain", full, "OK");
        Debug.LogError("[HeightmapTerrainImporter] " + full);
    }

    static float[,] BuildTerrainHeights(float[,] raw, int res)
    {
        float[,] natural = BuildNaturalTerrain(raw, res);
        float[,] final = CloneHeights(natural, res);

        foreach (var pad in Pads)
            ApplyPadFoundation(final, natural, pad, res);

        foreach (var road in Roads)
            ApplyCutFillRoad(final, natural, road, res);

        final = ClampSlopesMeters(final, res, MaxSlopeMeters, SlopeClampPasses);
        ReinforcePadsAndRoads(final, natural, res);

        return MetersToNorm(final, res);
    }

    static float[,] BuildNaturalTerrain(float[,] raw, int res)
    {
        float[,] g = RejectSpikes(raw, res, SpikeRejectGray);
        g = RejectSpikes(g, res, SpikeRejectGray * 0.7f);

        float[,] macro = Downsample(g, res, MacroRes);
        for (int i = 0; i < MacroBlurPasses; i++)
            macro = BoxBlur(macro, MacroRes, MacroBlurRadius);
        g = Upsample(macro, MacroRes, res);

        float hLow = ZoneMin(g, res, ReservoirAnchor);
        float hHigh = ZoneMax(g, res, LighthouseAnchor);
        if (hHigh - hLow < 0.04f) { hLow = 0.04f; hHigh = 0.88f; }

        var m = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                m[z, x] = GrayToMeters(g[z, x], hLow, hHigh);

        return ClampSlopesMeters(m, res, MaxSlopeMeters, SlopeClampPasses);
    }

    static void ApplyPadFoundation(float[,] final, float[,] natural, PadRect pad, int res)
    {
        float hx = pad.W * 0.5f;
        float hz = pad.H * 0.5f;
        float target = MedianInBox(natural, res, pad.X, pad.Z, hx * 0.9f, hz * 0.9f);

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                PixelToWorld(x, z, res, out float wx, out float wz);
                if (IsProtectedNatural(wx, wz)) continue;

                float outside = BoxOutsideDistance(wx, wz, pad.X, pad.Z, hx, hz);
                if (outside > PadEdgeBlendM) continue;

                if (outside <= 0f)
                    final[z, x] = target;
                else
                {
                    float w = 1f - outside / PadEdgeBlendM;
                    final[z, x] = Mathf.Lerp(natural[z, x], target, w);
                }
            }
        }
    }

    /// <summary>
    /// Дорога = выравнивание поперечного профиля (cut-fill), не канавa по одной точке.
    /// </summary>
    static void ApplyCutFillRoad(float[,] final, float[,] natural, RoadSeg road, int res)
    {
        float halfW = road.WidthM * 0.5f;
        float abx = road.Bx - road.Ax;
        float abz = road.Bz - road.Az;
        float len = Mathf.Sqrt(abx * abx + abz * abz);
        if (len < 0.01f) return;
        float px = -abz / len;
        float pz = abx / len;

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                PixelToWorld(x, z, res, out float wx, out float wz);
                if (IsProtectedNatural(wx, wz)) continue;

                float dist = DistToSegment(wx, wz, road.Ax, road.Az, road.Bx, road.Bz, out float t);
                if (dist > halfW + RoadEdgeBlendM) continue;

                float bed = CrossSectionHeight(natural, res, road, t, px, pz, halfW);

                if (dist <= halfW)
                    final[z, x] = bed;
                else
                {
                    float w = 1f - (dist - halfW) / RoadEdgeBlendM;
                    final[z, x] = Mathf.Lerp(natural[z, x], bed, w);
                }
            }
        }
    }

    static float CrossSectionHeight(float[,] natural, int res, RoadSeg road, float t, float px, float pz, float halfW)
    {
        float cx = Mathf.Lerp(road.Ax, road.Bx, t);
        float cz = Mathf.Lerp(road.Az, road.Bz, t);
        float sum = 0f;
        int count = 0;
        const int steps = 8;
        for (int i = 0; i <= steps; i++)
        {
            float s = Mathf.Lerp(-halfW, halfW, i / (float)steps);
            sum += SampleMetersBilinear(natural, res, cx + px * s, cz + pz * s);
            count++;
        }
        return sum / count;
    }

    static void ReinforcePadsAndRoads(float[,] final, float[,] natural, int res)
    {
        foreach (var pad in Pads)
        {
            float hx = pad.W * 0.5f * 0.88f;
            float hz = pad.H * 0.5f * 0.88f;
            float target = MedianInBox(natural, res, pad.X, pad.Z, hx, hz);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    PixelToWorld(x, z, res, out float wx, out float wz);
                    if (IsProtectedNatural(wx, wz)) continue;
                    if (Mathf.Abs(wx - pad.X) <= hx && Mathf.Abs(wz - pad.Z) <= hz)
                        final[z, x] = target;
                }
        }
    }

    static float[,] ClampSlopesMeters(float[,] src, int res, float maxStep, int passes)
    {
        var h = CloneHeights(src, res);
        float maxNorm = maxStep / TerrainVerticalSize;
        for (int p = 0; p < passes; p++)
        {
            var dst = CloneHeights(h, res);
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    PixelToWorld(x, z, res, out float wx, out float wz);
                    if (IsProtectedNatural(wx, wz)) { dst[z, x] = h[z, x]; continue; }

                    float minA = float.MinValue, maxA = float.MaxValue;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            int nx = x + dx, nz = z + dz;
                            if (nx < 0 || nx >= res || nz < 0 || nz >= res) continue;
                            float n = h[nz, nx];
                            minA = Mathf.Max(minA, n - maxNorm * TerrainVerticalSize);
                            maxA = Mathf.Min(maxA, n + maxNorm * TerrainVerticalSize);
                        }
                    }
                    dst[z, x] = Mathf.Clamp(h[z, x], minA, maxA);
                }
            }
            h = dst;
        }
        return h;
    }

    static bool IsProtectedNatural(float wx, float wz)
    {
        float edx = (wx + 110f) / 20f;
        float edz = (wz + 120f) / 16f;
        if (edx * edx + edz * edz < 1f) return true;
        if (Vector2.Distance(new Vector2(wx, wz), new Vector2(110f, 100f)) < 14f) return true;
        return false;
    }

    static float BoxOutsideDistance(float wx, float wz, float cx, float cz, float hx, float hz)
    {
        float ox = Mathf.Max(0f, Mathf.Abs(wx - cx) - hx);
        float oz = Mathf.Max(0f, Mathf.Abs(wz - cz) - hz);
        return Mathf.Max(ox, oz);
    }

    static float MedianInBox(float[,] m, int res, float cx, float cz, float hx, float hz)
    {
        var vals = new List<float>(512);
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                PixelToWorld(x, z, res, out float wx, out float wz);
                if (Mathf.Abs(wx - cx) > hx || Mathf.Abs(wz - cz) > hz) continue;
                vals.Add(m[z, x]);
            }
        if (vals.Count == 0) return 0f;
        vals.Sort();
        return vals[vals.Count / 2];
    }

    static float SampleMetersBilinear(float[,] m, int res, float wx, float wz)
    {
        float u = (wx + TerrainWidth * 0.5f) / TerrainWidth;
        float v = (wz + TerrainLength * 0.5f) / TerrainLength;
        float fx = Mathf.Clamp01(u) * (res - 1);
        float fz = Mathf.Clamp01(v) * (res - 1);
        int x0 = Mathf.FloorToInt(fx);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(res - 1, x0 + 1);
        int z1 = Mathf.Min(res - 1, z0 + 1);
        float tx = fx - x0, tz = fz - z0;
        float a = Mathf.Lerp(m[z0, x0], m[z0, x1], tx);
        float b = Mathf.Lerp(m[z1, x0], m[z1, x1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    static float DistToSegment(float px, float pz, float ax, float az, float bx, float bz, out float t)
    {
        float abx = bx - ax, abz = bz - az;
        float len2 = abx * abx + abz * abz;
        if (len2 < 0.0001f) { t = 0f; return Vector2.Distance(new Vector2(px, pz), new Vector2(ax, az)); }
        t = Mathf.Clamp01(((px - ax) * abx + (pz - az) * abz) / len2);
        float cx = ax + t * abx, cz = az + t * abz;
        return Vector2.Distance(new Vector2(px, pz), new Vector2(cx, cz));
    }

    static float GrayToMeters(float gray, float hLow, float hHigh)
    {
        float t = Mathf.Clamp01((gray - hLow) / (hHigh - hLow));
        return Mathf.Lerp(AnchorLowMeters, AnchorHighMeters, t);
    }

    static float ZoneMin(float[,] gray, int res, Site site)
    {
        float min = 1f;
        float hx = site.W * 0.5f, hz = site.H * 0.5f;
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                PixelToWorld(x, z, res, out float wx, out float wz);
                if (Mathf.Abs(wx - site.X) > hx || Mathf.Abs(wz - site.Z) > hz) continue;
                if (gray[z, x] < min) min = gray[z, x];
            }
        return min;
    }

    static float ZoneMax(float[,] gray, int res, Site site)
    {
        float max = 0f;
        float hx = site.W * 0.5f, hz = site.H * 0.5f;
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                PixelToWorld(x, z, res, out float wx, out float wz);
                if (Mathf.Abs(wx - site.X) > hx || Mathf.Abs(wz - site.Z) > hz) continue;
                if (gray[z, x] > max) max = gray[z, x];
            }
        return max;
    }

    static void PixelToWorld(int x, int z, int res, out float wx, out float wz)
        => TerrainMapCoordinates.PixelToWorld(x, z, res, out wx, out wz);

    static float WorldToNorm(float meters) => (meters - TerrainWorldBaseY) / TerrainVerticalSize;

    static float[,] MetersToNorm(float[,] meters, int res)
    {
        var norm = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                norm[z, x] = WorldToNorm(meters[z, x]);
        return norm;
    }

    static void RemoveTerrainIfExists(string name)
    {
        var go = GameObject.Find(name);
        if (go != null) Undo.DestroyObjectImmediate(go);
    }

    static Terrain EnsureSceneTerrain(string name, TerrainData data, Vector3 position)
    {
        var go = GameObject.Find(name);
        Terrain terrain;
        if (go == null)
        {
            terrain = Terrain.CreateTerrainGameObject(data).GetComponent<Terrain>();
            terrain.name = name;
            Undo.RegisterCreatedObjectUndo(terrain.gameObject, "Create " + name);
        }
        else
        {
            terrain = go.GetComponent<Terrain>() ?? go.AddComponent<Terrain>();
            Undo.RecordObject(terrain, "Apply " + name);
            terrain.terrainData = data;
        }
        terrain.transform.position = position;
        var collider = terrain.GetComponent<TerrainCollider>() ?? terrain.gameObject.AddComponent<TerrainCollider>();
        Undo.RecordObject(collider, "Apply " + name);
        collider.terrainData = data;
        EditorUtility.SetDirty(terrain);
        EditorUtility.SetDirty(collider);
        return terrain;
    }

    static float[,] SampleHeights(Texture2D heightmap, int resolution)
    {
        float[,] heights = new float[resolution, resolution];
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                TerrainMapCoordinates.PixelToWorld(x, z, resolution, out float wx, out float wz);
                float mapU = TerrainMapCoordinates.WorldToMapU(wx);
                float mapV = TerrainMapCoordinates.WorldToMapV(wz);
                heights[z, x] = TerrainMapCoordinates.SampleBilinear01(heightmap, mapU, mapV);
            }
        }
        return heights;
    }

    static float[,] Downsample(float[,] src, int srcRes, int dstRes)
    {
        var dst = new float[dstRes, dstRes];
        for (int z = 0; z < dstRes; z++)
        {
            for (int x = 0; x < dstRes; x++)
            {
                float u = x / (float)(dstRes - 1);
                float v = z / (float)(dstRes - 1);
                float fx = u * (srcRes - 1);
                float fz = v * (srcRes - 1);
                int x0 = Mathf.FloorToInt(fx);
                int z0 = Mathf.FloorToInt(fz);
                int x1 = Mathf.Min(srcRes - 1, x0 + 1);
                int z1 = Mathf.Min(srcRes - 1, z0 + 1);
                float tx = fx - x0, tz = fz - z0;
                float a = Mathf.Lerp(src[z0, x0], src[z0, x1], tx);
                float b = Mathf.Lerp(src[z1, x0], src[z1, x1], tx);
                dst[z, x] = Mathf.Lerp(a, b, tz);
            }
        }
        return dst;
    }

    static float[,] Upsample(float[,] src, int srcRes, int dstRes)
    {
        var dst = new float[dstRes, dstRes];
        for (int z = 0; z < dstRes; z++)
        {
            for (int x = 0; x < dstRes; x++)
            {
                float u = x / (float)(dstRes - 1);
                float v = z / (float)(dstRes - 1);
                float fx = u * (srcRes - 1);
                float fz = v * (srcRes - 1);
                int x0 = Mathf.FloorToInt(fx);
                int z0 = Mathf.FloorToInt(fz);
                int x1 = Mathf.Min(srcRes - 1, x0 + 1);
                int z1 = Mathf.Min(srcRes - 1, z0 + 1);
                float tx = fx - x0, tz = fz - z0;
                float a = Mathf.Lerp(src[z0, x0], src[z0, x1], tx);
                float b = Mathf.Lerp(src[z1, x0], src[z1, x1], tx);
                dst[z, x] = Mathf.Lerp(a, b, tz);
            }
        }
        return dst;
    }

    static float[,] CloneHeights(float[,] src, int res)
    {
        var dst = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                dst[z, x] = src[z, x];
        return dst;
    }

    static float[,] BoxBlur(float[,] src, int res, int radius)
    {
        var dst = new float[res, res];
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float sum = 0f;
                int count = 0;
                for (int dz = -radius; dz <= radius; dz++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = Mathf.Clamp(x + dx, 0, res - 1);
                        int nz = Mathf.Clamp(z + dz, 0, res - 1);
                        sum += src[nz, nx];
                        count++;
                    }
                }
                dst[z, x] = sum / count;
            }
        }
        return dst;
    }

    static float[,] RejectSpikes(float[,] src, int res, float threshold)
    {
        var dst = CloneHeights(src, res);
        for (int z = 1; z < res - 1; z++)
        {
            for (int x = 1; x < res - 1; x++)
            {
                float c = src[z, x];
                float avg = (src[z - 1, x] + src[z + 1, x] + src[z, x - 1] + src[z, x + 1]) * 0.25f;
                float diff = c - avg;
                if (Mathf.Abs(diff) > threshold)
                    dst[z, x] = avg + Mathf.Sign(diff) * threshold;
            }
        }
        return dst;
    }

    static void LogHeightStatsMeters(float[,] norm, int res)
    {
        float minM = float.MaxValue, maxM = float.MinValue, maxStepM = 0f;
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float m = TerrainWorldBaseY + norm[z, x] * TerrainVerticalSize;
                if (m < minM) minM = m;
                if (m > maxM) maxM = m;
                if (x + 1 < res)
                {
                    float m2 = TerrainWorldBaseY + norm[z, x + 1] * TerrainVerticalSize;
                    maxStepM = Mathf.Max(maxStepM, Mathf.Abs(m - m2));
                }
            }
        }
        Debug.Log($"Terrain heights (m): [{minM:F2} … {maxM:F2}], max step {maxStepM:F2} m");
    }

    static void EnsureReadable(Texture2D texture)
    {
        var path = AssetDatabase.GetAssetPath(texture);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        bool changed = false;
        if (!importer.isReadable) { importer.isReadable = true; changed = true; }
        if (importer.sRGBTexture) { importer.sRGBTexture = false; changed = true; }
        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
        if (changed) importer.SaveAndReimport();
    }
}
