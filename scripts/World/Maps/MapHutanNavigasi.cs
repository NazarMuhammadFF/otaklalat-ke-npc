using System.Collections.Generic;
using Godot;

namespace OtakLalat.World;

/// <summary>
/// Map uji Curiosity &amp; NeuralNoise: ekosistem hutan alami yang rindang dan detail.
/// Mencakup jaringan jalur jalan tanah bercabang, variasi pohon (Maple, Birch, DeadTree),
/// kayu tumbang berlumut, formasi batu alam &amp; hamparan batu kerikil, serta semak, bunga liar, dan rumput rapat.
/// Dioptimalkan dengan shared mesh &amp; material batching agar tetap ringan (60 FPS).
/// </summary>
public partial class MapHutanNavigasi : MapBase
{
    public override string MapTitle => "Hutan Navigasi";
    public override MapStats Stats => MapStats.Explore;
    protected override Color FloorColor => new(0.21f, 0.39f, 0.20f);

    static readonly string[] MapleModels =
    {
        "res://assets/models/nature/MapleTree_1.gltf",
        "res://assets/models/nature/MapleTree_2.gltf",
        "res://assets/models/nature/MapleTree_3.gltf",
        "res://assets/models/nature/MapleTree_4.gltf",
        "res://assets/models/nature/MapleTree_5.gltf",
    };

    static readonly string[] BirchModels =
    {
        "res://assets/models/nature/BirchTree_1.gltf",
        "res://assets/models/nature/BirchTree_2.gltf",
        "res://assets/models/nature/BirchTree_3.gltf",
        "res://assets/models/nature/BirchTree_4.gltf",
        "res://assets/models/nature/BirchTree_5.gltf",
    };

    static readonly string[] DeadTreeModels =
    {
        "res://assets/models/nature/DeadTree_1.gltf",
        "res://assets/models/nature/DeadTree_2.gltf",
        "res://assets/models/nature/DeadTree_3.gltf",
        "res://assets/models/nature/DeadTree_4.gltf",
        "res://assets/models/nature/DeadTree_5.gltf",
        "res://assets/models/nature/DeadTree_6.gltf",
        "res://assets/models/nature/DeadTree_7.gltf",
        "res://assets/models/nature/DeadTree_8.gltf",
    };

    static readonly string[] BushModels =
    {
        "res://assets/models/nature/Bush.gltf",
        "res://assets/models/nature/Bush_Large.gltf",
        "res://assets/models/nature/Bush_Small.gltf",
        "res://assets/models/nature/Bush_Flowers.gltf",
        "res://assets/models/nature/Bush_Small_Flowers.gltf",
        "res://assets/models/nature/Bush_Large_Flowers.gltf",
    };

    static readonly string[] GrassModels =
    {
        "res://assets/models/nature/Grass_Large.gltf",
        "res://assets/models/nature/Grass_Large_Extruded.gltf",
        "res://assets/models/nature/Grass_Small.gltf",
    };

    static readonly string[] FlowerModels =
    {
        "res://assets/models/nature/Flower_1_Clump.gltf",
        "res://assets/models/nature/Flower_2_Clump.gltf",
        "res://assets/models/nature/Flower_3_Clump.gltf",
        "res://assets/models/nature/Flower_4_Clump.gltf",
        "res://assets/models/nature/Flower_5_Clump.gltf",
        "res://assets/models/nature/Flower_1.gltf",
        "res://assets/models/nature/Flower_2.gltf",
    };

    readonly List<Vector2> trailPoints = new();
    readonly Dictionary<string, PackedScene> sceneCache = new();

    // Shared Materials & Meshes untuk GPU batching
    StandardMaterial3D rockMat, mossyRockMat, pebbleWarmMat, barkLogMat, woodEndMat;
    StandardMaterial3D deepMossMat, gladeMossMat, trailBorderMat, trailCoreMat;
    CylinderMesh unitDiscMesh, steppingStoneMesh;
    SphereMesh mainBoulderMesh, sideBoulderMesh, pebbleMesh;

    protected override void BuildLayout()
    {
        InitSharedResources();
        BuildForestFloorAndTrails();
        BuildBouldersAndPebbles();
        BuildFallenLogs();
        BuildMultiSpeciesTrees();
        BuildUndergrowthAndFoliage();

        AddZoneSign(
            new Vector3(0f, 6.8f, -18f),
            "MAP: HUTAN NAVIGASI ALAMI\n(Uji Curiosity & NeuralNoise — Kompas Matahari & Eksplorasi Jalur)",
            new Color(0.55f, 0.92f, 1f)
        );
    }

    void InitSharedResources()
    {
        Texture2D rockTex = ResourceLoader.Exists("res://assets/textures/Rocks.png")
            ? GD.Load<Texture2D>("res://assets/textures/Rocks.png")
            : null;
        Texture2D barkTex = ResourceLoader.Exists("res://assets/textures/MapleTree_Bark.png")
            ? GD.Load<Texture2D>("res://assets/textures/MapleTree_Bark.png")
            : null;
        Texture2D barkNorm = ResourceLoader.Exists("res://assets/textures/MapleTree_Bark_Normal.png")
            ? GD.Load<Texture2D>("res://assets/textures/MapleTree_Bark_Normal.png")
            : null;

        rockMat = new StandardMaterial3D { AlbedoColor = new Color(0.60f, 0.59f, 0.56f), AlbedoTexture = rockTex, Roughness = 0.92f };
        mossyRockMat = new StandardMaterial3D { AlbedoColor = new Color(0.46f, 0.54f, 0.43f), AlbedoTexture = rockTex, Roughness = 0.95f };
        pebbleWarmMat = new StandardMaterial3D { AlbedoColor = new Color(0.68f, 0.64f, 0.57f), AlbedoTexture = rockTex, Roughness = 0.88f };
        barkLogMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.48f, 0.36f, 0.25f),
            AlbedoTexture = barkTex,
            NormalEnabled = barkNorm != null,
            NormalTexture = barkNorm,
            Roughness = 0.94f,
        };
        woodEndMat = new StandardMaterial3D { AlbedoColor = new Color(0.68f, 0.54f, 0.36f), Roughness = 0.90f };

        deepMossMat = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.32f, 0.16f), Roughness = 0.96f };
        gladeMossMat = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.45f, 0.22f), Roughness = 0.96f };
        trailBorderMat = new StandardMaterial3D { AlbedoColor = new Color(0.37f, 0.29f, 0.20f), Roughness = 0.96f };
        trailCoreMat = new StandardMaterial3D { AlbedoColor = new Color(0.49f, 0.39f, 0.27f), Roughness = 0.95f };

        unitDiscMesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.008f, RadialSegments = 14 };
        steppingStoneMesh = new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.38f, Height = 0.04f, RadialSegments = 7 };
        mainBoulderMesh = new SphereMesh { Radius = 0.85f, Height = 1.5f, RadialSegments = 7, Rings = 4 };
        sideBoulderMesh = new SphereMesh { Radius = 0.65f, Height = 1.1f, RadialSegments = 6, Rings = 3 };
        pebbleMesh = new SphereMesh { Radius = 0.5f, Height = 0.9f, RadialSegments = 6, Rings = 3 };
    }

    Node3D SpawnCachedProp(string path, Vector3 pos, float scale, float rotYDeg, bool castShadow = true)
    {
        if (!sceneCache.TryGetValue(path, out var scene))
        {
            if (!ResourceLoader.Exists(path)) return null;
            scene = GD.Load<PackedScene>(path);
            sceneCache[path] = scene;
        }
        var node = scene?.Instantiate<Node3D>();
        if (node == null) return null;
        node.Position = pos;
        node.Scale = new Vector3(scale, scale, scale);
        node.RotationDegrees = new Vector3(0, rotYDeg, 0);
        if (!castShadow) SetShadowRecursive(node, GeometryInstance3D.ShadowCastingSetting.Off);
        AddChild(node);
        return node;
    }

    static void SetShadowRecursive(Node n, GeometryInstance3D.ShadowCastingSetting mode)
    {
        if (n is GeometryInstance3D g) g.CastShadow = mode;
        foreach (Node c in n.GetChildren()) SetShadowRecursive(c, mode);
    }

    // =========================================================================
    // 1. LANTAI HUTAN ORGANIK & BEBERAPA JALUR JALAN SETAPAK (FOREST TRAILS)
    // =========================================================================
    void BuildForestFloorAndTrails()
    {
        // Hamparan lumut gelap di bawah rumpun kanopi pohon agar kontras dengan jalur jalan
        AddDiscPatch(new Vector3(-12f, 0.003f, -11f), 7.5f, deepMossMat);
        AddDiscPatch(new Vector3(12f, 0.003f, -12f), 7.0f, deepMossMat);
        AddDiscPatch(new Vector3(-13f, 0.003f, 11f), 6.8f, deepMossMat);
        AddDiscPatch(new Vector3(13f, 0.003f, 11f), 7.2f, deepMossMat);
        AddDiscPatch(new Vector3(-6f, 0.003f, 4f), 4.5f, deepMossMat);
        AddDiscPatch(new Vector3(7f, 0.003f, -3f), 4.8f, deepMossMat);
        AddDiscPatch(new Vector3(0f, 0.004f, 0f), 4.2f, gladeMossMat);

        // Jalur 1: Jalur Utama Selatan -> Persimpangan Tengah -> Utara (berkelok alami)
        Vector2[] mainTrail =
        {
            new(0f, 18.2f), new(-1.2f, 15.0f), new(-2.6f, 11.5f), new(-2.8f, 8.0f),
            new(-1.8f, 4.5f), new(-0.6f, 1.8f), new(0f, 0f), new(1.2f, -2.8f),
            new(2.5f, -6.0f), new(2.8f, -9.5f), new(1.6f, -13.0f), new(0.4f, -16.0f), new(0f, -18.2f)
        };
        BuildTrailPath(mainTrail, 2.3f);

        // Jalur 2: Jalur Lintas Barat -> Persimpangan Tengah -> Timur
        Vector2[] crossTrail =
        {
            new(-18.0f, 2.5f), new(-14.5f, 1.8f), new(-11.0f, 0.2f), new(-7.5f, -1.2f),
            new(-3.8f, -0.8f), new(0f, 0f), new(3.6f, 0.8f), new(7.2f, 2.2f),
            new(11.0f, 2.8f), new(14.8f, 1.6f), new(18.0f, 0.4f)
        };
        BuildTrailPath(crossTrail, 2.0f);

        // Jalur 3: Jalur Cabang Lingkar Timur Laut (Menuju Lembah Birch)
        Vector2[] northEastLoop =
        {
            new(2.8f, -9.5f), new(6.2f, -10.5f), new(9.8f, -9.2f),
            new(12.2f, -6.0f), new(12.5f, -2.0f), new(11.0f, 2.8f)
        };
        BuildTrailPath(northEastLoop, 1.65f);

        // Jalur 4: Jalur Cabang Lingkar Barat Daya (Menuju Rumpun Kayu Tumbang)
        Vector2[] southWestLoop =
        {
            new(-11.0f, 0.2f), new(-12.5f, 4.0f), new(-11.2f, 7.8f),
            new(-7.8f, 10.5f), new(-2.6f, 11.5f)
        };
        BuildTrailPath(southWestLoop, 1.65f);

        // Jalur 5: Jalur Setapak Tenggara
        Vector2[] southEastSpur =
        {
            new(-1.2f, 15.0f), new(3.5f, 14.2f), new(7.8f, 12.0f),
            new(10.5f, 8.2f), new(11.0f, 2.8f)
        };
        BuildTrailPath(southEastSpur, 1.55f);

        // Area Persimpangan Tengah (Central Clearing)
        AddDiscPatch(new Vector3(0f, 0.008f, 0f), 3.0f, trailBorderMat);
        AddDiscPatch(new Vector3(0f, 0.012f, 0f), 2.2f, trailCoreMat);
    }

    void BuildTrailPath(Vector2[] waypoints, float width)
    {
        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            Vector2 a = waypoints[i];
            Vector2 b = waypoints[i + 1];
            float dist = a.DistanceTo(b);
            int steps = Mathf.Max(2, Mathf.CeilToInt(dist / 1.15f));
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector2 p = a.Lerp(b, t);
                trailPoints.Add(p);

                float jitter = Mathf.Sin(i * 3.1f + s * 1.7f) * 0.12f;
                float rOuter = (width * 0.55f) + jitter;
                float rInner = (width * 0.38f) + jitter * 0.5f;

                AddDiscPatch(new Vector3(p.X, 0.007f, p.Y), rOuter, trailBorderMat);
                AddDiscPatch(new Vector3(p.X, 0.011f, p.Y), rInner, trailCoreMat);
            }
        }

        // Batu pijakan pipih (stepping stones) di sepanjang jalan setapak
        for (int i = 0; i < waypoints.Length; i++)
        {
            Vector2 wp = waypoints[i];
            float ang = i * 1.1f;
            SpawnFlatSteppingStone(new Vector3(wp.X + Mathf.Cos(ang) * 0.3f, 0.02f, wp.Y + Mathf.Sin(ang) * 0.3f), i * 47f);
        }
    }

    void AddDiscPatch(Vector3 pos, float radius, Material mat)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = unitDiscMesh,
            MaterialOverride = mat,
            Position = pos,
            Scale = new Vector3(radius, 1f, radius),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    void SpawnFlatSteppingStone(Vector3 pos, float rotYDeg)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = steppingStoneMesh,
            MaterialOverride = pebbleWarmMat,
            Position = pos,
            RotationDegrees = new Vector3(0, rotYDeg, 0),
            Scale = new Vector3(1.2f, 1f, 0.85f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    bool IsNearTrail(Vector3 pos, float minClearance)
    {
        Vector2 p2 = new(pos.X, pos.Z);
        foreach (var tp in trailPoints)
        {
            if (p2.DistanceTo(tp) < minClearance) return true;
        }
        return false;
    }

    // =========================================================================
    // 2. FORMASI BATU BESAR ALAMI & HAMPARAN BATU KERIKIL (PEBBLES)
    // =========================================================================
    void BuildBouldersAndPebbles()
    {
        (Vector3 pos, Vector3 scale, float rotY, bool mossy)[] boulders =
        {
            (new(-8.2f, 0.55f, -5.8f), new(1.8f, 1.2f, 1.5f), 25f, true),
            (new(6.8f, 0.60f, 8.2f),   new(1.9f, 1.3f, 1.4f), 110f, false),
            (new(9.5f, 0.50f, -12.2f), new(1.7f, 1.1f, 1.6f), 65f, true),
            (new(-13.5f, 0.65f, 9.2f), new(2.1f, 1.4f, 1.6f), 195f, true),
            (new(3.2f, 0.50f, -14.5f), new(1.6f, 1.1f, 1.3f), 310f, false),
            (new(-14.2f, 0.55f, -3.5f),new(1.8f, 1.2f, 1.5f), 145f, true),
            (new(14.8f, 0.60f, -5.2f), new(2.0f, 1.3f, 1.5f), 40f, false),
            (new(-5.5f, 0.48f, 15.2f), new(1.6f, 1.0f, 1.4f), 85f, true),
            (new(14.2f, 0.58f, 12.8f), new(1.9f, 1.3f, 1.6f), 230f, true),
            (new(-9.5f, 0.52f, -14.0f),new(1.7f, 1.1f, 1.4f), 15f, false),
        };

        foreach (var (pos, scale, rotY, mossy) in boulders)
        {
            SpawnNaturalBoulderCluster(pos, scale, rotY, mossy);
        }

        // Hamparan batu kerikil (Pebble Clusters) di tepi jalur jalan dan sekitar batu/akar
        Vector3[] pebbleCenters =
        {
            new(-1.8f, 0f, 13.5f), new(1.5f, 0f, 10.2f), new(-4.2f, 0f, 6.8f),
            new(1.8f, 0f, 3.2f), new(-2.5f, 0f, -2.2f), new(3.8f, 0f, -4.5f),
            new(0.8f, 0f, -8.2f), new(4.2f, 0f, -11.5f), new(-1.5f, 0f, -14.8f),
            new(-13.0f, 0f, 3.2f), new(-9.2f, 0f, 1.2f), new(-5.5f, 0f, -2.5f),
            new(5.2f, 0f, -0.8f), new(8.8f, 0f, 4.0f), new(13.2f, 0f, 3.2f),
            new(7.8f, 0f, -9.0f), new(11.5f, 0f, -4.2f), new(-11.0f, 0f, 6.2f),
            new(5.2f, 0f, 12.8f), new(9.2f, 0f, 9.5f), new(-6.5f, 0f, -4.2f),
            new(5.5f, 0f, 6.8f), new(-11.5f, 0f, 8.2f), new(12.8f, 0f, 11.2f),
        };

        for (int i = 0; i < pebbleCenters.Length; i++)
        {
            SpawnPebbleScatter(pebbleCenters[i], 4 + (i % 3), i * 17);
        }
    }

    void SpawnNaturalBoulderCluster(Vector3 pos, Vector3 scale, float rotYDeg, bool mossy)
    {
        var mat = mossy ? mossyRockMat : rockMat;

        AddChild(new MeshInstance3D
        {
            Mesh = mainBoulderMesh,
            MaterialOverride = mat,
            Position = pos,
            RotationDegrees = new Vector3(12f, rotYDeg, -8f),
            Scale = scale,
        });

        float rad = Mathf.DegToRad(rotYDeg + 55f);
        var sidePos = new Vector3(pos.X + Mathf.Cos(rad) * 1.05f, pos.Y * 0.55f, pos.Z + Mathf.Sin(rad) * 1.05f);
        AddChild(new MeshInstance3D
        {
            Mesh = sideBoulderMesh,
            MaterialOverride = mossy ? rockMat : mossyRockMat,
            Position = sidePos,
            RotationDegrees = new Vector3(-15f, rotYDeg + 70f, 10f),
            Scale = scale * 0.68f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        var body = new StaticBody3D { Position = new Vector3(pos.X, 0.8f, pos.Z) };
        body.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = Mathf.Max(scale.X, scale.Z) * 0.78f, Height = 1.8f }
        });
        AddChild(body);
        Obstacles.Add(new Vector3(pos.X, 0.75f, pos.Z));
    }

    void SpawnPebbleScatter(Vector3 center, int count, int seedOffset)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = seedOffset + i * 2.399f;
            float dist = 0.25f + (i % 5) * 0.28f;
            Vector3 p = new(
                center.X + Mathf.Cos(angle) * dist,
                0.04f,
                center.Z + Mathf.Sin(angle) * dist
            );

            float sx = 0.14f + ((i + seedOffset) % 4) * 0.06f;
            float sy = 0.06f + (i % 3) * 0.03f;
            float sz = 0.12f + ((i * 3 + seedOffset) % 4) * 0.05f;

            var mat = (i % 3 == 0) ? mossyRockMat : (i % 2 == 0 ? pebbleWarmMat : rockMat);
            AddChild(new MeshInstance3D
            {
                Mesh = pebbleMesh,
                MaterialOverride = mat,
                Position = p,
                RotationDegrees = new Vector3((i * 19) % 25, (i * 67 + seedOffset) % 360, (i * 13) % 20),
                Scale = new Vector3(sx, sy, sz),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    // =========================================================================
    // 3. KAYU TUMBANG BERLUMUT (FALLEN LOGS & HOLLOW TRUNKS)
    // =========================================================================
    void BuildFallenLogs()
    {
        (Vector3 pos, float length, float radius, float yawDeg, string deadTreeModel)[] fallenLogs =
        {
            (new(-5.8f, 0f, 3.2f),  3.6f, 0.32f, 38f,  DeadTreeModels[0]),
            (new(6.5f, 0f, -3.5f),  3.8f, 0.34f, -45f, DeadTreeModels[1]),
            (new(-9.5f, 0f, 12.2f), 4.0f, 0.35f, 15f,  DeadTreeModels[2]),
            (new(11.8f, 0f, 6.2f),  3.4f, 0.30f, 112f, DeadTreeModels[3]),
            (new(-4.5f, 0f, -8.8f), 3.7f, 0.32f, -25f, DeadTreeModels[4]),
            (new(8.2f, 0f, -15.2f), 3.5f, 0.30f, 75f,  DeadTreeModels[5]),
            (new(-15.2f, 0f, -7.5f),3.8f, 0.33f, -65f, DeadTreeModels[6]),
            (new(2.5f, 0f, 16.2f),  3.3f, 0.29f, 18f,  DeadTreeModels[7]),
        };

        for (int i = 0; i < fallenLogs.Length; i++)
        {
            var (pos, length, radius, yawDeg, modelPath) = fallenLogs[i];
            SpawnFallenLog(pos, length, radius, yawDeg, modelPath, i);
        }
    }

    void SpawnFallenLog(Vector3 pos, float length, float radius, float yawDeg, string deadTreeModel, int idx)
    {
        var logRoot = new Node3D
        {
            Position = new Vector3(pos.X, radius * 0.82f, pos.Z),
            RotationDegrees = new Vector3(0f, yawDeg, 0f),
        };
        AddChild(logRoot);

        var barkCyl = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = radius * 0.82f,
                BottomRadius = radius * 1.05f,
                Height = length,
                RadialSegments = 10,
            },
            MaterialOverride = barkLogMat,
            RotationDegrees = new Vector3(0f, 0f, 90f),
        };
        logRoot.AddChild(barkCyl);

        foreach (float side in new[] { -1f, 1f })
        {
            float rEnd = side < 0 ? radius * 0.78f : radius * 0.98f;
            logRoot.AddChild(new MeshInstance3D
            {
                Mesh = unitDiscMesh,
                MaterialOverride = woodEndMat,
                Position = new Vector3(side * (length * 0.5f), 0f, 0f),
                RotationDegrees = new Vector3(0f, 0f, 90f),
                Scale = new Vector3(rEnd, 1f, rEnd),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        if (ResourceLoader.Exists(deadTreeModel))
        {
            var branchNode = GD.Load<PackedScene>(deadTreeModel)?.Instantiate<Node3D>();
            if (branchNode != null)
            {
                branchNode.Position = new Vector3(-length * 0.35f, -0.05f, 0f);
                branchNode.RotationDegrees = new Vector3(0f, 0f, -84f);
                branchNode.Scale = new Vector3(0.42f, 0.42f, 0.42f);
                SetShadowRecursive(branchNode, GeometryInstance3D.ShadowCastingSetting.Off);
                logRoot.AddChild(branchNode);
            }
        }

        SpawnCachedProp(GrassModels[idx % GrassModels.Length], pos + new Vector3(0.4f, 0f, 0.35f), 0.95f, yawDeg + 20f, castShadow: false);
        SpawnCachedProp(BushModels[2], pos + new Vector3(-0.5f, 0f, -0.35f), 0.45f, yawDeg - 35f, castShadow: false);

        var body = new StaticBody3D
        {
            Position = new Vector3(pos.X, 0.45f, pos.Z),
            RotationDegrees = new Vector3(0f, yawDeg, 0f),
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(length * 0.92f, 0.9f, radius * 2.2f) }
        });
        AddChild(body);
        Obstacles.Add(new Vector3(pos.X, 0.5f, pos.Z));
    }

    // =========================================================================
    // 4. BERBAGAI MACAM POHON RINDANG (MAPLE, BIRCH, & POHON TUA)
    // =========================================================================
    void BuildMultiSpeciesTrees()
    {
        (Vector3 pos, float scale, float rotY)[] mapleSpots =
        {
            (new(-5.8f, 0f, -1.2f),  0.75f, 25f),
            (new(5.5f, 0f, 4.8f),    0.78f, 80f),
            (new(-14.5f, 0f, -12.5f),0.92f, 135f),
            (new(-11.2f, 0f, -15.8f),0.82f, 210f),
            (new(14.5f, 0f, -1.2f),  0.85f, 45f),
            (new(-4.8f, 0f, 8.8f),   0.72f, 300f),
            (new(12.2f, 0f, 14.8f),  0.90f, 155f),
            (new(-4.5f, 0f, -14.8f), 0.80f, 95f),
            (new(16.2f, 0f, -14.2f), 0.95f, 185f),
            (new(-16.2f, 0f, 14.2f), 0.88f, 260f),
            (new(-16.5f, 0f, -1.5f), 0.84f, 15f),
            (new(16.5f, 0f, 7.5f),   0.86f, 120f),
            (new(-12.5f, 0f, 15.5f), 0.80f, 70f),
            (new(7.5f, 0f, 16.5f),   0.85f, 330f),
            (new(-16.0f, 0f, -8.5f), 0.78f, 190f),
            (new(13.8f, 0f, -9.8f),  0.82f, 240f),
        };

        for (int i = 0; i < mapleSpots.Length; i++)
        {
            var (pos, scale, rotY) = mapleSpots[i];
            if (IsNearTrail(pos, 1.55f)) continue;
            SpawnTreeWithCollider(MapleModels[i % MapleModels.Length], pos, scale, rotY, 0.45f);
        }

        (Vector3 pos, float scale, float rotY)[] birchSpots =
        {
            (new(4.8f, 0f, -6.5f),   0.76f, 40f),
            (new(8.5f, 0f, -6.2f),   0.84f, 115f),
            (new(10.8f, 0f, -14.8f), 0.88f, 205f),
            (new(-8.2f, 0f, -10.2f), 0.80f, 60f),
            (new(-13.8f, 0f, -6.2f), 0.75f, 150f),
            (new(-15.5f, 0f, 6.2f),  0.86f, 280f),
            (new(-8.5f, 0f, 6.5f),   0.72f, 320f),
            (new(8.2f, 0f, 0.2f),    0.74f, 10f),
            (new(15.2f, 0f, -8.2f),  0.85f, 90f),
            (new(15.8f, 0f, 4.2f),   0.80f, 175f),
            (new(6.2f, 0f, 11.5f),   0.78f, 245f),
            (new(-6.2f, 0f, 12.8f),  0.82f, 35f),
            (new(-11.8f, 0f, 13.2f), 0.76f, 130f),
            (new(11.2f, 0f, -0.8f),  0.70f, 290f),
            (new(-2.2f, 0f, -6.2f),  0.72f, 220f),
            (new(16.2f, 0f, 11.2f),  0.84f, 50f),
        };

        for (int i = 0; i < birchSpots.Length; i++)
        {
            var (pos, scale, rotY) = birchSpots[i];
            if (IsNearTrail(pos, 1.50f)) continue;
            SpawnTreeWithCollider(BirchModels[i % BirchModels.Length], pos, scale, rotY, 0.38f);
        }

        (Vector3 pos, float scale, float rotY)[] oldTreeSpots =
        {
            (new(-16.8f, 0f, -15.5f), 0.78f, 45f),
            (new(16.8f, 0f, -16.5f),  0.82f, 130f),
            (new(-17.0f, 0f, 10.5f),  0.74f, 215f),
            (new(16.5f, 0f, 15.8f),   0.80f, 305f),
            (new(-10.8f, 0f, -3.2f),  0.65f, 85f),
            (new(13.5f, 0f, -15.5f),  0.72f, 165f),
        };

        for (int i = 0; i < oldTreeSpots.Length; i++)
        {
            var (pos, scale, rotY) = oldTreeSpots[i];
            if (IsNearTrail(pos, 1.50f)) continue;
            SpawnTreeWithCollider(DeadTreeModels[i % DeadTreeModels.Length], pos, scale, rotY, 0.35f);
        }
    }

    void SpawnTreeWithCollider(string path, Vector3 pos, float scale, float rotYDeg, float trunkRadius)
    {
        SpawnCachedProp(path, pos, scale, rotYDeg, castShadow: true);
        var body = new StaticBody3D { Position = new Vector3(pos.X, 1.0f, pos.Z) };
        body.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = trunkRadius, Height = 2.2f }
        });
        AddChild(body);
        Obstacles.Add(new Vector3(pos.X, 0.75f, pos.Z));
    }

    // =========================================================================
    // 5. SEMAK BELUKAR, BUNGA LIAR, & HAMPARAN RUMPUT (UNDERGROWTH)
    // =========================================================================
    void BuildUndergrowthAndFoliage()
    {
        (Vector3 pos, float scale, int modelIdx)[] bushes =
        {
            (new(-4.8f, 0f, 0.2f),    0.65f, 3),
            (new(4.5f, 0f, 3.5f),     0.60f, 0),
            (new(-13.2f, 0f, -11.0f), 0.75f, 1),
            (new(13.2f, 0f, -2.5f),   0.70f, 5),
            (new(-3.8f, 0f, 9.8f),    0.58f, 4),
            (new(11.0f, 0f, 13.5f),   0.72f, 1),
            (new(-3.5f, 0f, -13.5f),  0.62f, 3),
            (new(15.0f, 0f, -13.0f),  0.68f, 0),
            (new(-15.0f, 0f, 13.0f),  0.74f, 5),
            (new(7.5f, 0f, -5.0f),    0.55f, 4),
            (new(-7.5f, 0f, -8.8f),   0.64f, 2),
            (new(-14.2f, 0f, 5.0f),   0.66f, 3),
            (new(14.5f, 0f, 5.5f),    0.62f, 1),
            (new(5.2f, 0f, 10.2f),    0.58f, 4),
            (new(-5.2f, 0f, 13.8f),   0.65f, 0),
            (new(9.5f, 0f, -13.5f),   0.60f, 2),
            (new(-15.5f, 0f, -2.8f),  0.68f, 5),
            (new(15.5f, 0f, 9.2f),    0.64f, 3),
        };

        for (int i = 0; i < bushes.Length; i++)
        {
            var (pos, scale, modelIdx) = bushes[i];
            if (IsNearTrail(pos, 1.35f)) continue;
            SpawnCachedProp(BushModels[modelIdx % BushModels.Length], pos, scale, i * 47f, castShadow: false);
        }

        Vector3[] flowerSpots =
        {
            new(-2.2f, 0f, 3.2f), new(2.4f, 0f, 1.8f), new(-1.8f, 0f, -3.5f),
            new(2.8f, 0f, -1.5f), new(-4.2f, 0f, 12.8f), new(1.2f, 0f, 14.5f),
            new(4.5f, 0f, -8.2f), new(-0.8f, 0f, -11.5f), new(-9.5f, 0f, 2.2f),
            new(-5.8f, 0f, -2.8f), new(6.2f, 0f, 0.5f), new(9.2f, 0f, 4.8f),
            new(13.5f, 0f, -3.8f), new(8.2f, 0f, -11.2f), new(-10.5f, 0f, 9.2f),
            new(5.8f, 0f, 14.2f), new(9.8f, 0f, 10.5f), new(-13.8f, 0f, -0.5f),
            new(13.8f, 0f, 0.2f), new(-2.8f, 0f, 6.2f),
        };

        for (int i = 0; i < flowerSpots.Length; i++)
        {
            var pos = flowerSpots[i];
            if (IsNearTrail(pos, 1.15f)) continue;
            SpawnCachedProp(FlowerModels[i % FlowerModels.Length], pos, 0.85f + (i % 3) * 0.15f, i * 63f, castShadow: false);
        }

        for (int gx = -17; gx <= 17; gx += 3)
        {
            for (int gz = -17; gz <= 17; gz += 3)
            {
                int hash = Mathf.Abs(gx * 73 + gz * 37);
                if (hash % 3 == 0) continue;

                float ox = ((hash % 11) - 5) * 0.22f;
                float oz = (((hash / 3) % 11) - 5) * 0.22f;
                Vector3 pos = new(gx + ox, 0f, gz + oz);

                if (IsNearTrail(pos, 1.25f)) continue;

                string model = GrassModels[hash % GrassModels.Length];
                float scale = 0.85f + (hash % 5) * 0.14f;
                float rotY = (hash * 29) % 360;
                SpawnCachedProp(model, pos, scale, rotY, castShadow: false);
            }
        }
    }
}
