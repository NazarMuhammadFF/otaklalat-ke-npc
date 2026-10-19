using Godot;

namespace OtakLalat.World;

/// <summary>Map uji Curiosity &amp; NeuralNoise: hutan maple dan batu sebagai landmark/rintangan. Tanpa makanan, bahaya, atau drone.</summary>
public partial class MapHutanNavigasi : MapBase
{
    public override string MapTitle => "Hutan Navigasi";
    public override MapStats Stats => MapStats.Explore;
    protected override Color FloorColor => new(0.24f, 0.44f, 0.24f);

    static readonly Vector3[] Rocks = { new(-8, 0.75f, -6), new(7, 0.75f, 9), new(10, 0.75f, -10), new(-12, 0.75f, 10), new(2, 0.75f, -14) };

    protected override void BuildLayout()
    {
        AddRocks(Rocks);
        string[] maples = { "MapleTree_1", "MapleTree_2", "MapleTree_3" };
        Vector3[] spots = { new(-6, 0, 2), new(5, 0, -3), new(-14, 0, -12), new(14, 0, 4), new(0, 0, 12), new(12, 0, 14), new(-4, 0, -14), new(16, 0, -14), new(-16, 0, 14) };
        for (int i = 0; i < spots.Length; i++)
        {
            SpawnProp($"res://assets/models/nature/{maples[i % maples.Length]}.gltf", spots[i], 0.55f, i * 55f);
            Obstacles.Add(spots[i]);
        }
        AddZoneSign(new Vector3(0f, 6.5f, -18f), "MAP: HUTAN NAVIGASI\n(Uji Curiosity & NeuralNoise - kompas matahari)", new Color(0.55f, 0.9f, 1f));
    }
}
