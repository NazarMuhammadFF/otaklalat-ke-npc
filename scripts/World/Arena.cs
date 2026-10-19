using System.Collections.Generic;
using Godot;
using OtakLalat.Brain;
using OtakLalat.Npc;

namespace OtakLalat.World;

/// <summary>
/// Membangun arena 3D: lantai, dinding, rintangan, makanan, racun, zona bahaya, drone predator, pemain, NPC.
/// Mode benchmark diaktifkan lewat argumen: -- --bench=brain|fsm --n=50 --seed=1 --duration=120
/// </summary>
public partial class Arena : Node3D
{
    public const float HalfSize = 20f;
    public static readonly string[] TypeIds = { "rusa", "babi", "kucing", "penduduk" };

    [Export] public int BrainPerType = 3;
    [Export] public int FsmPerType = 3;

    public AiManager Ai { get; private set; }
    public Drone Predator { get; private set; }
    public Player PlayerNode { get; private set; }
    public RandomNumberGenerator Rng { get; } = new();
    public readonly List<Label3D> ZoneLabels = new();
    ulong nextSeed = 1;

    public override void _Ready()
    {
        var args = ParseArgs();
        Rng.Seed = ulong.Parse(args.GetValueOrDefault("seed", "1"));
        nextSeed = Rng.Seed * 1000;
        WorldRegistry.Clear();
        BuildEnvironment();
        BuildLayout();

        Ai = new AiManager { Name = "AiManager" };
        AddChild(Ai);
        Predator = new Drone { Name = "Drone", Seed = Rng.Seed, Position = new Vector3(0, 6, 0), ArenaHalfSize = HalfSize - 2 };
        AddChild(Predator);

        if (args.ContainsKey("bench"))
        {
            AddChild(new Benchmark.BenchmarkRunner(this, args));
            return;
        }
        PlayerNode = new Player { Name = "Player", Position = new Vector3(0, 0.5f, HalfSize - 3) };
        PlayerNode.DropFood = p => SpawnEdible(new Vector3(p.X, 0, p.Z), false);
        AddChild(PlayerNode);
        foreach (var t in TypeIds)
        {
            for (int i = 0; i < BrainPerType; i++) SpawnBrain(t, RandomSpot());
            for (int i = 0; i < FsmPerType; i++) SpawnFsm(t, RandomSpot());
        }
        AddChild(new UI.DebugOverlay(this));
    }

    static Dictionary<string, string> ParseArgs()
    {
        var d = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            d[kv[0]] = kv.Length > 1 ? kv[1] : "1";
        }
        return d;
    }

    void BuildEnvironment()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
        };
        AddChild(new WorldEnvironment { Environment = env });
        var sun = new DirectionalLight3D { ShadowEnabled = true, RotationDegrees = new Vector3(-50, 60, 0) };
        AddChild(sun);
        // landmark: matahari = kebalikan arah rambat cahaya (dipakai ring neuron sebagai kompas)
        var lightDir = -sun.Basis.Z; lightDir.Y = 0;
        WorldRegistry.SunDirection = -lightDir.Normalized();

        var floor = new StaticBody3D { Name = "Floor" };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(HalfSize * 2, 1, HalfSize * 2) }, Position = new Vector3(0, -0.5f, 0) });
        floor.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(HalfSize * 2, 1, HalfSize * 2) }, Position = new Vector3(0, -0.5f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.55f, 0.3f) },
        });
        AddChild(floor);
        var wallMat = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.4f, 0.35f) };
        for (int i = 0; i < 4; i++)
        {
            bool x = i < 2;
            float s = i % 2 == 0 ? 1 : -1;
            AddBox(new Vector3(x ? s * HalfSize : 0, 1, x ? 0 : s * HalfSize), x ? new Vector3(0.5f, 2, HalfSize * 2) : new Vector3(HalfSize * 2, 2, 0.5f), wallMat);
        }
    }

    const float RockSize = 2.5f;
    static readonly Vector3[] Rocks = { new(-8, 0.75f, -6), new(7, 0.75f, 9), new(10, 0.75f, -10), new(-12, 0.75f, 10), new(2, 0.75f, -14) };

    void BuildLayout()
    {
        // 1. Petak lantai 4 Kuadran Eksperimen agar batas fungsi tiap area tampak jelas dari kamera atas (F2)
        AddGroundPatch(new Vector3(-10f, 0.005f, -10f), new Vector2(18f, 18f), new Color(0.28f, 0.54f, 0.25f)); // Kuadran 1: Kebun Makanan & Racun
        AddGroundPatch(new Vector3(-9f, 0.006f, 6f), new Vector2(18f, 20f), new Color(0.44f, 0.36f, 0.27f));    // Kuadran 2: Suaka Bahaya & Trauma
        AddGroundPatch(new Vector3(10f, 0.005f, 10f), new Vector2(18f, 18f), new Color(0.43f, 0.55f, 0.29f));   // Kuadran 3: Koridor Predator Looming
        AddGroundPatch(new Vector3(10f, 0.005f, -10f), new Vector2(18f, 18f), new Color(0.25f, 0.46f, 0.24f));  // Kuadran 4: Hutan Navigasi Kompas

        // 2. Rintangan batu alam dengan tekstur PBR Rocks.png
        var rockMat = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.60f, 0.58f), Roughness = 0.9f };
        if (ResourceLoader.Exists("res://assets/textures/Rocks.png"))
            rockMat.AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/Rocks.png");
        foreach (var r in Rocks) AddBox(r, new Vector3(RockSize, 1.5f, RockSize), rockMat);

        // 3. Makanan manis (Bush_Large_Flowers) & Racun pahit (Bush_Small)
        Vector3[] food = { new(-14, 0, -14), new(14, 0, -3), new(-3, 0, 12), new(5, 0, -6), new(-15, 0, 3), new(15, 0, 15), new(-12, 0, -10) };
        foreach (var p in food) SpawnEdible(p, false);
        Vector3[] poison = { new(-10, 0, -15), new(12, 0, -1), new(0, 0, 14), new(-15, 0, 6), new(-15, 0, -11) };
        foreach (var p in poison) SpawnEdible(p, true);

        // 4. Zona Bahaya utama di (-4, 0, -2) radius 3.5m (sesuai acuan benchmark analyze_bench.py)
        AddChild(new DangerZone { Name = "Danger", Position = new Vector3(-4, 0, -2), Radius = 3.5f });

        // 5. Dekorasi alam 3D per kuadran (tanpa mengganggu jalur fisika utama)
        BuildQuadrantProps();

        // 6. Papan penanda 3D tiap kuadran untuk memandu pengamatan manusia
        AddZoneSign(new Vector3(-12f, 3.3f, -12f), "KUADRAN 1: KEBUN MAKANAN & RACUN\n(Uji Appetite vs Pickiness)", new Color(0.6f, 1f, 0.55f));
        AddZoneSign(new Vector3(-6f, 3.3f, 2f), "KUADRAN 2: SUAKA BAHAYA TERKONDISI\n(Uji LearningRate & Memori Dopamin)", new Color(1f, 0.55f, 0.45f));
        AddZoneSign(new Vector3(11f, 3.3f, 11f), "KUADRAN 3: KORIDOR PREDATOR\n(Uji Fearfulness / Refleks Looming)", new Color(1f, 0.85f, 0.45f));
        AddZoneSign(new Vector3(10f, 3.3f, -11f), "KUADRAN 4: HUTAN NAVIGASI KOMPAS\n(Uji Curiosity & NeuralNoise)", new Color(0.55f, 0.9f, 1f));
    }

    void AddGroundPatch(Vector3 pos, Vector2 size, Color col)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size.X, 0.01f, size.Y) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = col, Roughness = 0.95f },
            Position = pos,
        });
    }

    void BuildQuadrantProps()
    {
        // Kuadran 4 (Timur Laut): Pohon Maple rindang di atas formasi batu & tepi hutan sebagai landmark visual
        SpawnProp("res://assets/models/nature/MapleTree_1.gltf", new Vector3(10f, 1.4f, -10f), 0.55f, 25f);
        SpawnProp("res://assets/models/nature/MapleTree_2.gltf", new Vector3(2f, 1.4f, -14f), 0.52f, 140f);
        SpawnProp("res://assets/models/nature/MapleTree_3.gltf", new Vector3(17.5f, 0f, -17f), 0.6f, 70f);
        SpawnProp("res://assets/models/nature/MapleTree_1.gltf", new Vector3(17.5f, 0f, -8f), 0.5f, 210f);

        // Kuadran 2 (Barat Daya): Pohon kering di atas batu & tepi area tandus
        SpawnProp("res://assets/models/nature/DeadTree_2.gltf", new Vector3(-12f, 1.4f, 10f), 0.55f, 45f);
        SpawnProp("res://assets/models/nature/DeadTree_4.gltf", new Vector3(-17.5f, 0f, 15f), 0.55f, 120f);
        SpawnProp("res://assets/models/nature/DeadTree_6.gltf", new Vector3(-8f, 1.4f, -6f), 0.48f, 190f);

        // Kuadran 1 (Barat Laut): Rumpun bunga di sekitar kebun makanan
        SpawnProp("res://assets/models/nature/Flower_1_Clump.gltf", new Vector3(-13f, 0f, -12.5f), 1.1f, 0f);
        SpawnProp("res://assets/models/nature/Flower_2_Clump.gltf", new Vector3(-11f, 0f, -13.5f), 1.1f, 85f);
        SpawnProp("res://assets/models/nature/Flower_4_Clump.gltf", new Vector3(-15.5f, 0f, -12.5f), 1.1f, 160f);

        // Kuadran 3 (Tenggara): Rumput sabana rendah di koridor terbuka predator
        SpawnProp("res://assets/models/nature/Grass_Small.gltf", new Vector3(9f, 0f, 6f), 1.3f, 30f);
        SpawnProp("res://assets/models/nature/Grass_Small.gltf", new Vector3(13f, 0f, 11f), 1.3f, 110f);
        SpawnProp("res://assets/models/nature/Grass_Small.gltf", new Vector3(6f, 0f, 14f), 1.3f, 240f);
    }

    void SpawnProp(string path, Vector3 pos, float scale, float rotYDeg)
    {
        if (!ResourceLoader.Exists(path)) return;
        var node = GD.Load<PackedScene>(path)?.Instantiate<Node3D>();
        if (node == null) return;
        node.Position = pos;
        node.Scale = new Vector3(scale, scale, scale);
        node.RotationDegrees = new Vector3(0, rotYDeg, 0);
        AddChild(node);
    }

    void AddZoneSign(Vector3 pos, string text, Color color)
    {
        var lbl = new Label3D
        {
            Position = pos,
            Text = text,
            Modulate = color,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 36,
            OutlineSize = 12,
            PixelSize = 0.008f,
        };
        ZoneLabels.Add(lbl);
        AddChild(lbl);
    }

    void AddBox(Vector3 pos, Vector3 size, Material mat)
    {
        var b = new StaticBody3D { Position = pos };
        b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        b.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat });
        AddChild(b);
    }

    public Edible SpawnEdible(Vector3 p, bool poison)
    {
        var e = new Edible { IsPoison = poison, Position = p };
        AddChild(e);
        return e;
    }

    public Vector3 RandomSpot()
    {
        for (int i = 0; i < 50; i++)
        {
            var p = new Vector3(Rng.RandfRange(-HalfSize + 2, HalfSize - 2), 0.1f, Rng.RandfRange(-HalfSize + 2, HalfSize - 2));
            bool bad = false;
            foreach (var z in WorldRegistry.Dangers) bad |= z.GlobalPosition.DistanceTo(p) < z.Radius + 1.5f;
            foreach (var r in Rocks) bad |= Mathf.Abs(r.X - p.X) < RockSize / 2 + 1f && Mathf.Abs(r.Z - p.Z) < RockSize / 2 + 1f;
            if (!bad) return p;
        }
        return Vector3.Zero;
    }

    public BrainNpc SpawnBrain(string type, Vector3 pos)
    {
        var n = new BrainNpc
        {
            Profile = GD.Load<TraitProfile>($"res://data/npc_types/brain/{type}.tres"),
            Seed = nextSeed++, Position = pos, Rotation = new Vector3(0, Rng.RandfRange(-Mathf.Pi, Mathf.Pi), 0),
        };
        AddChild(n);
        return n;
    }

    public BaselineNpc SpawnFsm(string type, Vector3 pos)
    {
        var n = new BaselineNpc
        {
            Profile = GD.Load<FsmProfile>($"res://data/npc_types/fsm/{type}.tres"),
            Seed = nextSeed++, Position = pos, Rotation = new Vector3(0, Rng.RandfRange(-Mathf.Pi, Mathf.Pi), 0),
        };
        AddChild(n);
        return n;
    }
}
