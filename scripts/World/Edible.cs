using Godot;

namespace OtakLalat.World;

/// <summary>Makanan (manis) atau racun (pahit). Habis dimakan lalu muncul lagi.</summary>
public partial class Edible : Node3D
{
    [Export] public bool IsPoison;
    [Export] public float Amount = 4f;          // detik makan sampai habis
    [Export] public float RespawnSeconds = 20f;
    [Export] public float Radius = 0.5f;
    public float Remaining { get; private set; }
    public bool Available => Remaining > 0f;
    OdorSource odor;
    Node3D visualRoot;
    float respawnTimer;
    static PackedScene foodBushScene, poisonBushScene;

    public override void _EnterTree() => WorldRegistry.Edibles.Add(this);
    public override void _ExitTree() => WorldRegistry.Edibles.Remove(this);

    public override void _Ready()
    {
        Remaining = Amount;
        visualRoot = new Node3D { Name = "Visual" };
        AddChild(visualRoot);

        // 1. Model semak 3D dari assets/models/nature/
        string bushPath = IsPoison
            ? "res://assets/models/nature/Bush_Small.gltf"
            : "res://assets/models/nature/Bush_Large_Flowers.gltf";
        ref var cached = ref (IsPoison ? ref poisonBushScene : ref foodBushScene);
        cached ??= ResourceLoader.Exists(bushPath) ? GD.Load<PackedScene>(bushPath) : null;
        if (cached != null)
        {
            var bush = cached.Instantiate<Node3D>();
            float s = IsPoison ? 0.65f : 0.72f;
            bush.Scale = new Vector3(s, s, s);
            // Origin mesh Bush berada di tengah vertikal; angkat agar bagian bawah menapak lantai
            bush.Position = new Vector3(0, IsPoison ? 0.42f : 0.55f, 0);
            visualRoot.AddChild(bush);
        }

        // 2. Penanda buah manis (oranye keemasan) vs kristal racun pahit (ungu menyala)
        var coreColor = IsPoison ? new Color(0.68f, 0.18f, 0.88f) : new Color(1f, 0.62f, 0.12f);
        visualRoot.AddChild(new MeshInstance3D
        {
            Mesh = IsPoison
                ? new PrismMesh { Size = new Vector3(0.55f, 0.65f, 0.55f) }
                : new SphereMesh { Radius = Radius * 0.65f, Height = Radius * 1.3f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = coreColor,
                EmissionEnabled = true,
                Emission = coreColor,
                EmissionEnergyMultiplier = 0.6f,
            },
            Position = new Vector3(0, IsPoison ? 0.65f : 0.75f, 0),
        });

        // 3. Visualisasi cincin transparan jangkauan aroma (OdorSpread) di lantai
        var plumeColor = IsPoison ? new Color(0.65f, 0.2f, 0.85f, 0.14f) : new Color(0.95f, 0.72f, 0.15f, 0.14f);
        visualRoot.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 3.0f, BottomRadius = 3.0f, Height = 0.02f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = plumeColor,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Position = new Vector3(0, 0.015f, 0),
        });

        odor = new OdorSource { Kind = IsPoison ? OdorKind.Poison : OdorKind.Food, Spread = 3f };
        AddChild(odor);
    }

    /// <summary>Konsumsi selama dt detik; mengembalikan true jika ada yang termakan.</summary>
    public bool Consume(float dt)
    {
        if (!Available) return false;
        Remaining -= dt;
        if (Remaining <= 0f) { respawnTimer = RespawnSeconds; ShowItem(false); }
        return true;
    }

    void ShowItem(bool v) { visualRoot.Visible = v; odor.Active = v; }

    public override void _PhysicsProcess(double delta)
    {
        if (Available) return;
        respawnTimer -= (float)delta;
        if (respawnTimer <= 0f) { Remaining = Amount; ShowItem(true); }
    }
}
