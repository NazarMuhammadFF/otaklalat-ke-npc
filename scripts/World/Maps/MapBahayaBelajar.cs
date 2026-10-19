using Godot;

namespace OtakLalat.World;

/// <summary>Map uji LearningRate: satu zona bahaya berbau netral di tengah lapangan tandus. Tanpa makanan, tanpa drone.</summary>
public partial class MapBahayaBelajar : MapBase
{
    public override string MapTitle => "Bahaya & Belajar";
    public override MapStats Stats => MapStats.Danger;
    protected override Color FloorColor => new(0.44f, 0.36f, 0.27f);

    protected override void BuildLayout()
    {
        AddChild(new DangerZone { Name = "Danger", Position = new Vector3(0, 0, -2), Radius = 3.5f });
        string[] trees = { "DeadTree_2", "DeadTree_4", "DeadTree_6", "DeadTree_3", "DeadTree_5" };
        for (int i = 0; i < trees.Length; i++)
        {
            float a = i * Mathf.Tau / trees.Length;
            SpawnProp($"res://assets/models/nature/{trees[i]}.gltf", new Vector3(Mathf.Cos(a) * 12f, 0, -2f + Mathf.Sin(a) * 12f), 0.55f, i * 70f);
        }
        AddZoneSign(new Vector3(0f, 4.5f, -2f), "MAP: BAHAYA & BELAJAR\n(Uji LearningRate - memori dopamin)", new Color(1f, 0.55f, 0.45f));
    }
}
