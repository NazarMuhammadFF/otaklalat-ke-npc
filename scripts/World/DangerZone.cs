using Godot;

namespace OtakLalat.World;

/// <summary>Zona bahaya (lantai berduri): berbau netral, menyakiti NPC yang berada di dalamnya.</summary>
public partial class DangerZone : Node3D
{
    [Export] public float Radius = 3f;

    public override void _EnterTree() => WorldRegistry.Dangers.Add(this);
    public override void _ExitTree() => WorldRegistry.Dangers.Remove(this);

    public override void _Ready()
    {
        // 1. Area inti sengatan bahaya (merah menyala)
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = Radius, BottomRadius = Radius, Height = 0.05f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.85f, 0.15f, 0.15f, 0.55f),
                EmissionEnabled = true,
                Emission = new Color(0.85f, 0.15f, 0.15f),
                EmissionEnergyMultiplier = 0.4f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
            Position = new Vector3(0, 0.03f, 0),
        });

        // 2. Lingkaran luar jangkauan bau netral (tempat NPC yang sudah belajar trauma berbelok menghindar)
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = Radius * 1.7f, OuterRadius = Radius * 1.85f, Rings = 32, RingSegments = 12 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.95f, 0.35f, 0.2f, 0.32f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Position = new Vector3(0, 0.02f, 0),
            Scale = new Vector3(1f, 0.15f, 1f),
        });

        // 3. Pohon kering (DeadTree) sebagai penanda visual Suaka Bahaya
        string[] deadTrees = { "res://assets/models/nature/DeadTree_1.gltf", "res://assets/models/nature/DeadTree_5.gltf" };
        Vector3[] offsets = { new(-1.2f, 0f, -0.8f), new(1.4f, 0f, 1.1f) };
        for (int i = 0; i < deadTrees.Length; i++)
        {
            if (!ResourceLoader.Exists(deadTrees[i])) continue;
            var tree = GD.Load<PackedScene>(deadTrees[i])?.Instantiate<Node3D>();
            if (tree == null) continue;
            tree.Position = offsets[i];
            tree.Scale = new Vector3(0.45f, 0.45f, 0.45f);
            tree.RotationDegrees = new Vector3(0, i * 115f, 0);
            AddChild(tree);
        }

        AddChild(new OdorSource { Kind = OdorKind.Neutral, Spread = 3.5f });
    }

    public bool Contains(Vector3 p)
    {
        var d = p - GlobalPosition; d.Y = 0;
        return d.Length() < Radius;
    }
}
