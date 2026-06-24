using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class WorldSiteBuilder
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string RootName = "WorldSites";
    const string MaterialsFolder = "Assets/World/Materials";

    const float WallT = 0.2f;
    const float FenceT = 0.01f;
    const float SlabT = 0.25f;

    static readonly string[] LegacyRootNames =
    {
        "Laboratory", "Wasteland", "SolarField", "SolarField (1)", "SolarField (2)",
        "AbandonedBuilding", "OfficeSkeleton", "Bridge", "Railroad", "Lighthouse",
        "Entrance", "Reservoir", "SolarPanels"
    };

    static Material _concrete, _brick, _metal, _glass, _wood, _panel, _dark, _water, _sign, _roof, _rust;

    [MenuItem("Tools/World/Build All Sites")]
    public static void BuildAllSitesMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath);
        BuildAllSites();
        EditorUtility.DisplayDialog("World Sites", "WorldSites построены в SampleScene.", "OK");
    }

    public static void BuildAllSitesSilent()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath);
        BuildAllSites();
    }

    public static void SetupWorldSilent()
    {
        HeightmapTerrainImporter.ImportHeightmapToTerrainDataSilent();
        TerrainTextureImporter.ImportTerrainTexturesSilent();

        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath);

        var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/Terrain/TerrainData.asset");
        if (terrainData != null)
        {
            var pos = new Vector3(-150f, -2f, -150f);
            EnsureTerrainInScene(terrainData, pos);
        }

        BuildAllSites();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }

    static void EnsureTerrainInScene(TerrainData data, Vector3 pos)
    {
        var go = GameObject.Find("Terrain_Main");
        Terrain terrain;
        if (go == null)
        {
            terrain = Terrain.CreateTerrainGameObject(data).GetComponent<Terrain>();
            terrain.name = "Terrain_Main";
        }
        else
        {
            terrain = go.GetComponent<Terrain>() ?? go.AddComponent<Terrain>();
            terrain.terrainData = data;
        }
        terrain.transform.position = pos;
        var col = terrain.GetComponent<TerrainCollider>() ?? terrain.gameObject.AddComponent<TerrainCollider>();
        col.terrainData = data;
    }

    static void BuildAllSites()
    {
        EnsureMaterials();
        RemoveLegacyPlaceholders();
        var root = GetOrCreateRoot();
        ClearChildren(root.transform);

        BuildLaboratory(root.transform);
        BuildLighthouse(root.transform);
        BuildSolarField(root.transform);
        BuildAbandonedHouse(root.transform);
        BuildEntranceBunker(root.transform);
        BuildOfficeSkeleton(root.transform);
        BuildReservoir(root.transform);
        BuildBridge(root.transform);
        BuildRailway(root.transform);
        BuildWasteland(root.transform);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // ─── Laboratory (0, 0) — крест из 5 крыльев ───────────────────────────────

    static void BuildLaboratory(Transform root)
    {
        var site = CreateSiteRoot(root, "Laboratory", 0f, 0f);
        float gy = GroundY(0f, 0f);

        BuildRoom(site, "Hall", 0f, 0f, gy, 14f, 14f, 7f, _concrete, true);
        BuildRoom(site, "Observation", 0f, 26f, gy, 16f, 12f, 3.5f, _concrete, false);
        BuildRoom(site, "LabWing", 26f, 0f, gy, 16f, 12f, 10.5f, _concrete, true);
        BuildRoom(site, "Wards", -26f, 0f, gy, 16f, 12f, 3.5f, _brick, false);
        BuildRoom(site, "Admin", 0f, -26f, gy, 16f, 12f, 3.5f, _concrete, false);

        // Коридоры — проёмы к центру (двери 1.2×2.1 м)
        DoorOpening(site, gy, 0f, 7f, 1.2f, 2.1f, Vector3.forward);
        DoorOpening(site, gy, 7f, 0f, 1.2f, 2.1f, Vector3.right);
        DoorOpening(site, gy, -7f, 0f, 1.2f, 2.1f, Vector3.left);
        DoorOpening(site, gy, 0f, -7f, 1.2f, 2.1f, Vector3.back);

        // Главный вход с лестницей (юг)
        float stairZ = -22f;
        for (int i = 0; i < 5; i++)
            Box(site, "Step", new Vector3(0f, gy + 0.08f + i * 0.18f, stairZ - i * 0.35f),
                new Vector3(4f, 0.16f, 0.35f), _concrete);
        Box(site, "DoorFrame", new Vector3(0f, gy + 1.15f, -20.5f), new Vector3(1.4f, 2.3f, WallT), _metal);
        Box(site, "DoorL", new Vector3(-0.55f, gy + 1.05f, -20.35f), new Vector3(0.55f, 2.1f, 0.06f), _dark);
        Box(site, "DoorR", new Vector3(0.55f, gy + 1.05f, -20.35f), new Vector3(0.55f, 2.1f, 0.06f), _dark);
        Box(site, "Canopy", new Vector3(0f, gy + 2.6f, -20.2f), new Vector3(3.2f, 0.12f, 1.8f), _concrete);

        // Крыша: спутниковая тарелка + антенны
        Box(site, "DishBase", new Vector3(26f, gy + 10.8f, 0f), new Vector3(0.4f, 0.4f, 0.4f), _metal);
        var dish = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        dish.name = "SatDish";
        dish.transform.SetParent(site);
        dish.transform.position = new Vector3(26f, gy + 11.3f, 0f);
        dish.transform.localScale = new Vector3(3.6f, 0.06f, 3.6f);
        dish.transform.rotation = Quaternion.Euler(55f, 20f, 0f);
        SetMat(dish, _metal);
        for (int i = 0; i < 3; i++)
            Box(site, "Antenna", new Vector3(20f + i * 2.5f, gy + 10.8f, 4f), new Vector3(0.08f, 3.5f, 0.08f), _metal);

        FenceRect(site, 0f, 0f, 52f, 52f, gy, gateX: 0f, gateZ: -26f);
        Sign(site, new Vector3(-18f, gy + 1.4f, -24f), "ЛАБОРАТОРИЯ", _sign);
    }

    static void BuildRoom(Transform parent, string id, float cx, float cz, float gy,
        float w, float d, float height, Material wallMat, bool windows)
    {
        var room = new GameObject(id).transform;
        room.SetParent(parent);
        room.position = Vector3.zero;

        Box(room, "Floor", new Vector3(cx, gy + SlabT * 0.5f, cz), new Vector3(w, SlabT, d), _concrete);

        float hx = w * 0.5f, hz = d * 0.5f;
        float wy = gy + SlabT + height * 0.5f;
        Wall(room, wallMat, new Vector3(cx, wy, cz + hz), new Vector3(w + WallT * 2f, height, WallT));
        Wall(room, wallMat, new Vector3(cx, wy, cz - hz), new Vector3(w + WallT * 2f, height, WallT));
        Wall(room, wallMat, new Vector3(cx + hx, wy, cz), new Vector3(WallT, height, d));
        Wall(room, wallMat, new Vector3(cx - hx, wy, cz), new Vector3(WallT, height, d));

        if (windows)
        {
            int cols = Mathf.Max(1, Mathf.FloorToInt(w / 4f));
            for (int i = 0; i < cols; i++)
            {
                float wx = cx - hx + (i + 0.5f) * (w / cols);
                Window(room, new Vector3(wx, gy + SlabT + 1.6f, cz + hz + WallT * 0.5f), Vector3.forward);
                Window(room, new Vector3(wx, gy + SlabT + 1.6f, cz - hx + WallT * 0.5f), Vector3.back);
            }
        }

        Box(room, "Roof", new Vector3(cx, gy + SlabT + height + 0.06f, cz), new Vector3(w + 0.4f, 0.12f, d + 0.4f), _roof);
    }

    // ─── Lighthouse (110, 100) ────────────────────────────────────────────────

    static void BuildLighthouse(Transform root)
    {
        var site = CreateSiteRoot(root, "Lighthouse", 110f, 100f);
        float gy = GroundY(110f, 100f);

        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            Box(site, $"Rock{i}", new Vector3(Mathf.Cos(a) * 5f, gy + 0.8f, Mathf.Sin(a) * 5f),
                new Vector3(3.5f, 1.6f, 3f), _dark);
        }

        Box(site, "TowerBase", new Vector3(0f, gy + 4f, 0f), new Vector3(5.5f, 8f, 5.5f), _concrete);
        Box(site, "TowerMid", new Vector3(0f, gy + 10f, 0f), new Vector3(4.2f, 6f, 4.2f), _concrete);
        Box(site, "TowerTop", new Vector3(0f, gy + 15f, 0f), new Vector3(3.2f, 5f, 3.2f), _concrete);
        Box(site, "RedBand", new Vector3(0f, gy + 9.5f, 0f), new Vector3(4.4f, 0.5f, 4.4f), _rust);
        Box(site, "Lantern", new Vector3(0f, gy + 18.2f, 0f), new Vector3(3.6f, 1.8f, 3.6f), _glass);
        Box(site, "LanternRoof", new Vector3(0f, gy + 19.6f, 0f), new Vector3(4f, 0.8f, 4f), _rust);
        var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cone.name = "RoofCone";
        cone.transform.SetParent(site);
        cone.transform.localPosition = new Vector3(0f, gy + 20.5f, 0f);
        cone.transform.localScale = new Vector3(3.5f, 1.2f, 3.5f);
        SetMat(cone, _rust);

        BuildRoom(site, "Annex", 5f, 0f, gy, 6f, 5f, 3f, _concrete, true);
        FenceRect(site, 0f, 0f, 22f, 22f, gy, gateX: 0f, gateZ: -11f);
        Sign(site, new Vector3(-8f, gy + 1.5f, -9f), "МАЯК", _sign);
    }

    // ─── Solar (90, -30) ──────────────────────────────────────────────────────

    static void BuildSolarField(Transform root)
    {
        var site = CreateSiteRoot(root, "SolarPanels", 90f, -30f);
        float gy = GroundY(90f, -30f);

        FenceRect(site, 0f, 0f, 46f, 36f, gy, gateX: 0f, gateZ: -18f);
        Sign(site, new Vector3(-12f, gy + 1.5f, -16f), "СОЛНЕЧНАЯ СТАНЦИЯ", _sign);

        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 5; col++)
            {
                float lx = -18f + col * 8.5f;
                float lz = -12f + row * 7.5f;
                SolarUnit(site, lx, lz, gy, col % 2 == 0 ? 32f : 28f);
            }
        }
    }

    static void SolarUnit(Transform parent, float x, float z, float gy, float tilt)
    {
        Box(parent, "Post", new Vector3(x, gy + 0.6f, z), new Vector3(0.08f, 1.2f, 0.08f), _metal);
        Box(parent, "Leg", new Vector3(x - 0.5f, gy + 0.3f, z + 0.4f), new Vector3(0.06f, 0.06f, 0.9f), _metal);
        Box(parent, "Leg2", new Vector3(x + 0.5f, gy + 0.3f, z + 0.4f), new Vector3(0.06f, 0.06f, 0.9f), _metal);
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Panel";
        panel.transform.SetParent(parent);
        panel.transform.localPosition = new Vector3(x, gy + 1.35f, z);
        panel.transform.localScale = new Vector3(2f, 0.05f, 1.2f);
        panel.transform.localRotation = Quaternion.Euler(tilt, 15f, 0f);
        SetMat(panel, _panel);
        Box(parent, "Inverter", new Vector3(x, gy + 0.25f, z - 0.5f), new Vector3(0.5f, 0.4f, 0.35f), _metal);
    }

    // ─── Abandoned (-90, -60) ─────────────────────────────────────────────────

    static void BuildAbandonedHouse(Transform root)
    {
        var site = CreateSiteRoot(root, "AbandonedBuilding", -90f, -60f);
        float gy = GroundY(-90f, -60f);

        BuildRoom(site, "Main", -5f, 0f, gy, 14f, 10f, 6.5f, _brick, true);
        BuildRoom(site, "Wing", 5f, -2f, gy, 10f, 8f, 3.2f, _brick, true);

        for (int i = 0; i < 6; i++)
            Box(site, "Rubble", new Vector3(8f + i * 0.7f, gy + 0.3f, 2f + i * 0.3f),
                new Vector3(0.8f, 0.5f, 0.6f), _brick);

        Box(site, "Graffiti", new Vector3(-2f, gy + 2.5f, 5.2f), new Vector3(2f, 1.2f, 0.02f), _panel);
        Sign(site, new Vector3(-12f, gy + 1.5f, -2f), "ЗАБРОШЕННОЕ ЗДАНИЕ", _sign);
    }

    static void BuildEntranceBunker(Transform root)
    {
        var site = CreateSiteRoot(root, "Entrance", 40f, -90f);
        float gy = GroundY(40f, -90f);

        Box(site, "Mound", new Vector3(0f, gy + 0.6f, 0f), new Vector3(14f, 1.2f, 12f), _dark);
        BuildRoom(site, "Bunker", 0f, 0f, gy + 0.3f, 8f, 6f, 2.8f, _concrete, false);
        Box(site, "Hatch", new Vector3(0f, gy + 1.2f, -2f), new Vector3(2.2f, 0.15f, 2.2f), _metal);
        Box(site, "HatchWheel", new Vector3(0f, gy + 1.35f, -2f), new Vector3(0.6f, 0.08f, 0.6f), _rust);
    }

    static void BuildOfficeSkeleton(Transform root)
    {
        var site = CreateSiteRoot(root, "OfficeSkeleton", 100f, -120f);
        float gy = GroundY(100f, -120f);

        for (int col = 0; col < 5; col++)
        {
            float cx = -14f + col * 7f;
            for (int floor = 0; floor < 3; floor++)
            {
                Box(site, "Col", new Vector3(cx, gy + 1.5f + floor * 3f, 0f), new Vector3(0.25f, 3f, 0.25f), _concrete);
                Box(site, "Slab", new Vector3(0f, gy + SlabT + floor * 3f, 0f), new Vector3(36f, 0.2f, 24f), _concrete);
            }
        }
        for (int row = 0; row < 4; row++)
            Box(site, "Beam", new Vector3(0f, gy + 4.5f, -10f + row * 7f), new Vector3(36f, 0.2f, 0.25f), _concrete);
    }

    static void BuildReservoir(Transform root)
    {
        var site = CreateSiteRoot(root, "Reservoir", -110f, -120f);
        float gy = GroundY(-110f, -120f);

        Box(site, "Water", new Vector3(0f, gy - 0.35f, 0f), new Vector3(52f, 0.08f, 40f), _water);
        BuildRoom(site, "PumpHouse", 12f, 8f, gy, 10f, 8f, 6f, _concrete, true);
        Box(site, "DamWall", new Vector3(20f, gy + 1.5f, 0f), new Vector3(4f, 3f, 40f), _concrete);
        for (int i = 0; i < 5; i++)
            Box(site, "Step", new Vector3(14f, gy + 0.15f + i * 0.18f, 10f - i * 0.4f),
                new Vector3(2.5f, 0.16f, 0.35f), _concrete);
        Sign(site, new Vector3(-20f, gy + 1.5f, -15f), "ВОДОХРАНИЛИЩЕ", _sign);
    }

    static void BuildBridge(Transform root)
    {
        var site = CreateSiteRoot(root, "Bridge", -80f, -100f);
        float gy = GroundY(-80f, -100f);

        Box(site, "Deck", new Vector3(0f, gy + 0.15f, 0f), new Vector3(26f, 0.12f, 3.2f), _wood);
        for (int i = 0; i < 14; i++)
            Box(site, "Plank", new Vector3(-12f + i * 1.8f, gy + 0.22f, 0f), new Vector3(1.6f, 0.04f, 3f), _wood);
        PipeRail(site, new Vector3(-13f, gy + 1f, -1.4f), new Vector3(13f, gy + 1f, -1.4f));
        PipeRail(site, new Vector3(-13f, gy + 1f, 1.4f), new Vector3(13f, gy + 1f, 1.4f));
        Sign(site, new Vector3(-14f, gy + 1.4f, -4f), "МОСТ", _sign);
    }

    static void BuildRailway(Transform root)
    {
        var site = CreateSiteRoot(root, "Railroad", -20f, 120f);
        float gy = GroundY(-20f, 100f);

        Box(site, "Ballast", new Vector3(0f, gy + 0.08f, 0f), new Vector3(8f, 0.16f, 92f), _dark);

        for (float z = -45f; z <= 45f; z += 0.55f)
            Box(site, "Sleeper", new Vector3(0f, gy + 0.12f, z), new Vector3(2.4f, 0.1f, 0.22f), _wood);

        Box(site, "RailL", new Vector3(-0.75f, gy + 0.22f, 0f), new Vector3(0.08f, 0.12f, 90f), _metal);
        Box(site, "RailR", new Vector3(0.75f, gy + 0.22f, 0f), new Vector3(0.08f, 0.12f, 90f), _metal);

        FenceLine(site, new Vector3(-4f, gy, -42f), new Vector3(4f, gy, -42f), 2.8f);
        Box(site, "GatePostL", new Vector3(-4f, gy + 1.4f, -42f), new Vector3(0.12f, 2.8f, 0.12f), _metal);
        Box(site, "GatePostR", new Vector3(4f, gy + 1.4f, -42f), new Vector3(0.12f, 2.8f, 0.12f), _metal);

        BuildRoom(site, "Factory", 0f, 42f, gy, 24f, 16f, 8f, _concrete, true);
        Box(site, "Chimney", new Vector3(10f, gy + 12f, 48f), new Vector3(2f, 8f, 2f), _concrete);
        Box(site, "Signal", new Vector3(-8f, gy + 2.5f, -38f), new Vector3(0.15f, 2.5f, 0.15f), _metal);
        Box(site, "Crossbuck", new Vector3(-8f, gy + 3.8f, -38f), new Vector3(1.2f, 0.12f, 0.08f), _sign);
        Sign(site, new Vector3(-12f, gy + 1.5f, -42f), "Ж/Д К ЗАВОДУ", _sign);
    }

    static void BuildWasteland(Transform root)
    {
        var site = CreateSiteRoot(root, "Wasteland", -110f, 70f);
        float gy = GroundY(-110f, 70f);

        for (int i = 0; i < 12; i++)
        {
            float ox = RandomOffset(i, 18f);
            float oz = RandomOffset(i + 7, 16f);
            Box(site, "Debris", new Vector3(ox, gy + 0.25f, oz),
                new Vector3(1.2f + i * 0.1f, 0.4f, 0.8f), _concrete);
        }
        Box(site, "BrokenSlab", new Vector3(5f, gy + 0.08f, 4f), new Vector3(4f, 0.12f, 3f), _concrete);
        Box(site, "Tyre", new Vector3(-8f, gy + 0.35f, -5f), new Vector3(0.9f, 0.35f, 0.9f), _dark);
    }

    // ─── Primitives ───────────────────────────────────────────────────────────

    static Transform CreateSiteRoot(Transform parent, string name, float x, float z)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(x, 0f, z);
        return go.transform;
    }

    static Transform GetOrCreateRoot()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null) return existing.transform;
        return new GameObject(RootName).transform;
    }

    static void RemoveLegacyPlaceholders()
    {
        foreach (var n in LegacyRootNames)
        {
            var go = GameObject.Find(n);
            if (go != null && go.transform.parent == null)
                Undo.DestroyObjectImmediate(go);
        }
    }

    static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(t.GetChild(i).gameObject);
    }

    static float GroundY(float x, float z)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) return 0f;
        return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
    }

    static Transform Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        SetMat(go, mat);
        return go.transform;
    }

    static void Wall(Transform p, Material mat, Vector3 center, Vector3 size) => Box(p, "Wall", center, size, mat);

    static void Window(Transform p, Vector3 pos, Vector3 outward)
    {
        Box(p, "Window", pos, new Vector3(1.1f, 1.2f, 0.06f), _glass);
        Box(p, "FrameT", pos + outward * 0.04f + Vector3.up * 0.65f, new Vector3(1.2f, 0.08f, 0.1f), _metal);
        Box(p, "FrameB", pos + outward * 0.04f + Vector3.down * 0.65f, new Vector3(1.2f, 0.08f, 0.1f), _metal);
    }

    static void DoorOpening(Transform p, float gy, float cx, float cz, float w, float h, Vector3 normal)
    {
        Box(p, "DoorHead", new Vector3(cx, gy + SlabT + h + 0.15f, cz) + normal * 0.1f,
            new Vector3(normal.z != 0 ? w : WallT, 0.3f, normal.x != 0 ? w : WallT), _concrete);
    }

    static void FenceLine(Transform p, Vector3 aLocal, Vector3 bLocal, float height = 2.2f)
    {
        var dir = bLocal - aLocal;
        float len = dir.magnitude;
        if (len < 0.01f) return;
        var center = (aLocal + bLocal) * 0.5f + Vector3.up * (height * 0.5f);
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Fence";
        go.transform.SetParent(p);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        go.transform.localScale = new Vector3(FenceT, height, len);
        SetMat(go, _metal);
    }

    static void FenceRect(Transform p, float cx, float cz, float w, float h, float gy,
        float gateX = float.NaN, float gateZ = float.NaN)
    {
        float hx = w * 0.5f, hz = h * 0.5f;
        float y = gy;
        var corners = new[]
        {
            new Vector3(cx - hx, y, cz - hz), new Vector3(cx + hx, y, cz - hz),
            new Vector3(cx + hx, y, cz + hz), new Vector3(cx - hx, y, cz + hz)
        };
        for (int i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            if (!float.IsNaN(gateX) && i == 2)
            {
                var mid = (a + b) * 0.5f;
                FenceLine(p, a, new Vector3(gateX - 1.2f, y, mid.z));
                FenceLine(p, new Vector3(gateX + 1.2f, y, mid.z), b);
                continue;
            }
            FenceLine(p, a, b);
        }
        // столбы
        for (int i = 0; i < 4; i++)
            Box(p, "Post", corners[i] + Vector3.up * 1.1f, new Vector3(0.08f, 2.2f, 0.08f), _metal);
        // колючка сверху
        for (int i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            var c = (a + b) * 0.5f + Vector3.up * 2.25f;
            Box(p, "Barbed", c, new Vector3(FenceT, 0.08f, (a - b).magnitude * 0.9f), _rust);
        }
    }

    static void PipeRail(Transform p, Vector3 aLocal, Vector3 bLocal)
    {
        var dir = bLocal - aLocal;
        float len = dir.magnitude;
        var center = (aLocal + bLocal) * 0.5f;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Rail";
        go.transform.SetParent(p);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        go.transform.localScale = new Vector3(0.05f, len * 0.5f, 0.05f);
        SetMat(go, _rust);
        foreach (var t in new[] { 0.25f, 0.5f, 0.75f })
            Box(p, "RailPost", Vector3.Lerp(aLocal, bLocal, t), new Vector3(0.05f, 1f, 0.05f), _rust);
    }

    static void Sign(Transform p, Vector3 pos, string label, Material mat)
    {
        Box(p, "SignPostL", pos + new Vector3(-0.6f, -0.7f, 0f), new Vector3(0.06f, 1.4f, 0.06f), _metal);
        Box(p, "SignPostR", pos + new Vector3(0.6f, -0.7f, 0f), new Vector3(0.06f, 1.4f, 0.06f), _metal);
        Box(p, $"Sign_{label}", pos, new Vector3(2.4f, 0.9f, 0.04f), mat);
    }

    static float RandomOffset(int seed, float range)
    {
        float t = Mathf.Sin(seed * 12.989f) * 43758.5453f;
        return (t - Mathf.Floor(t)) * range - range * 0.5f;
    }

    static void SetMat(GameObject go, Material mat) => go.GetComponent<Renderer>().sharedMaterial = mat;

    static void EnsureMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/World"))
            AssetDatabase.CreateFolder("Assets", "World");
        if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            AssetDatabase.CreateFolder("Assets/World", "Materials");

        _concrete = GetOrCreateMat("Mat_Concrete", new Color(0.62f, 0.61f, 0.58f));
        _brick = GetOrCreateMat("Mat_Brick", new Color(0.55f, 0.38f, 0.32f));
        _metal = GetOrCreateMat("Mat_Metal", new Color(0.45f, 0.46f, 0.48f));
        _glass = GetOrCreateMat("Mat_Glass", new Color(0.55f, 0.65f, 0.75f, 0.6f));
        _wood = GetOrCreateMat("Mat_Wood", new Color(0.42f, 0.32f, 0.22f));
        _panel = GetOrCreateMat("Mat_SolarPanel", new Color(0.12f, 0.22f, 0.42f));
        _dark = GetOrCreateMat("Mat_Dark", new Color(0.28f, 0.27f, 0.25f));
        _water = GetOrCreateMat("Mat_Water", new Color(0.08f, 0.18f, 0.22f, 0.85f));
        _sign = GetOrCreateMat("Mat_Sign", new Color(0.75f, 0.72f, 0.65f));
        _roof = GetOrCreateMat("Mat_Roof", new Color(0.48f, 0.46f, 0.44f));
        _rust = GetOrCreateMat("Mat_Rust", new Color(0.52f, 0.32f, 0.22f));
    }

    static Material GetOrCreateMat(string name, Color color)
    {
        string path = $"{MaterialsFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        if (color.a < 1f)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.renderQueue = 3000;
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
