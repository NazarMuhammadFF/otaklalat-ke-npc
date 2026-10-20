using System.Collections.Generic;
using Godot;
using OtakLalat.Brain;
using OtakLalat.Brain.Core;
using OtakLalat.Npc;

namespace OtakLalat.World;

/// <summary>Metrik yang relevan untuk satu map (menentukan baris statistik & tombol skenario di overlay).</summary>
[System.Flags]
public enum MapStats { None = 0, Food = 1, Poison = 2, Escape = 4, Danger = 8, Explore = 16 }

/// <summary>
/// Basis semua map uji: langit, matahari, lantai, dinding, scheduler AI, pemain, overlay, dan API spawn.
/// Subkelas hanya mengisi <see cref="BuildLayout"/> dengan stimulus yang relevan untuk satu jenis uji.
/// NPC TIDAK di-spawn otomatis (kecuali <see cref="SpawnInitialNpcs"/> di-override), pengguna menaruhnya lewat overlay.
/// Mode benchmark diaktifkan lewat argumen: -- --bench=brain|fsm --n=50 --seed=1 --duration=120 (hanya <see cref="Arena"/>).
/// </summary>
public abstract partial class MapBase : Node3D
{
    public const float HalfSize = 20f;
    public const float RockSize = 2.5f;
    public static readonly string[] TypeIds = { "rusa", "babi", "kucing", "penduduk" };

    public abstract string MapTitle { get; }
    public virtual MapStats Stats => MapStats.None;
    protected virtual bool UseDrone => false;
    /// <summary>Pohon dari <see cref="SpawnProp"/> diberi collider batang (Arena benchmark menonaktifkan agar hasil lama tetap sebanding).</summary>
    protected virtual bool PropCollision => true;
    protected virtual Color FloorColor => new(0.35f, 0.55f, 0.3f);
    protected virtual Vector3 PlayerStart => new(0, 0.5f, HalfSize - 3);

    public AiManager Ai { get; private set; }
    public Drone Predator { get; private set; }
    public Player PlayerNode { get; private set; }
    public RandomNumberGenerator Rng { get; } = new();
    public readonly List<Label3D> ZoneLabels = new();
    public bool SuppressOverlay { get; set; }
    protected readonly List<Vector3> Obstacles = new();
    ulong nextSeed = 1;

    protected abstract void BuildLayout();
    protected virtual void SpawnInitialNpcs() { }
    protected virtual bool TryStartBenchmark(Dictionary<string, string> args) => false;

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
        if (UseDrone)
        {
            Predator = new Drone { Name = "Drone", Seed = Rng.Seed, Position = new Vector3(0, 6, 0), ArenaHalfSize = HalfSize - 2 };
            AddChild(Predator);
        }

        if (TryStartBenchmark(args)) return;

        PlayerNode = new Player { Name = "Player", Position = PlayerStart };
        PlayerNode.DropFood = p => SpawnEdible(new Vector3(p.X, 0, p.Z), false);
        AddChild(PlayerNode);
        SpawnInitialNpcs();
        if (!SuppressOverlay) AddChild(new UI.DebugOverlay(this));
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
            MaterialOverride = new StandardMaterial3D { AlbedoColor = FloorColor },
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

    // ---- helper layout untuk subkelas ----

    protected void AddGroundPatch(Vector3 pos, Vector2 size, Color col)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size.X, 0.01f, size.Y) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = col, Roughness = 0.95f },
            Position = pos,
        });
    }

    protected void AddRocks(IEnumerable<Vector3> positions)
    {
        var rockMat = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.60f, 0.58f), Roughness = 0.9f };
        if (ResourceLoader.Exists("res://assets/textures/Rocks.png"))
            rockMat.AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/Rocks.png");
        foreach (var r in positions)
        {
            AddBox(r, new Vector3(RockSize, 1.5f, RockSize), rockMat);
            Obstacles.Add(r);
        }
    }

    protected void SpawnProp(string path, Vector3 pos, float scale, float rotYDeg)
    {
        if (!ResourceLoader.Exists(path)) return;
        var node = GD.Load<PackedScene>(path)?.Instantiate<Node3D>();
        if (node == null) return;
        node.Position = pos;
        node.Scale = new Vector3(scale, scale, scale);
        node.RotationDegrees = new Vector3(0, rotYDeg, 0);
        AddChild(node);
        if (PropCollision && path.Contains("Tree_"))
        {
            var trunk = new StaticBody3D { Position = new Vector3(pos.X, 1f, pos.Z) };
            trunk.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.35f, Height = 2f } });
            AddChild(trunk);
            Obstacles.Add(new Vector3(pos.X, 0f, pos.Z));
        }
    }

    protected void AddZoneSign(Vector3 pos, string text, Color color)
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

    protected void AddBox(Vector3 pos, Vector3 size, Material mat)
    {
        var b = new StaticBody3D { Position = pos };
        b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        b.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat });
        AddChild(b);
    }

    // ---- API spawn ----

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
            foreach (var r in Obstacles) bad |= Mathf.Abs(r.X - p.X) < RockSize / 2 + 1f && Mathf.Abs(r.Z - p.Z) < RockSize / 2 + 1f;
            if (!bad) return p;
        }
        return Vector3.Zero;
    }

    /// <summary>Tempatkan NPC; <paramref name="pos"/> dijepit ke dalam dinding arena.</summary>
    public NpcBase SpawnNpc(string type, bool brain, Vector3 pos, VisionMode vision = VisionMode.Scalar)
    {
        float lim = HalfSize - 1f;
        pos = new Vector3(Mathf.Clamp(pos.X, -lim, lim), 0.1f, Mathf.Clamp(pos.Z, -lim, lim));
        return brain ? SpawnBrain(type, pos, vision) : SpawnFsm(type, pos);
    }

    public BrainNpc SpawnBrain(string type, Vector3 pos, VisionMode vision = VisionMode.Scalar)
    {
        var n = new BrainNpc
        {
            Profile = GD.Load<TraitProfile>($"res://data/npc_types/brain/{type}.tres"),
            Seed = nextSeed++, Position = pos, Rotation = new Vector3(0, Rng.RandfRange(-Mathf.Pi, Mathf.Pi), 0),
            Vision = vision,
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

    public void ClearNpcs()
    {
        foreach (var n in NpcBase.All.ToArray()) n.QueueFree();
    }
}
