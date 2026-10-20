using Godot;
using OtakLalat.Npc;

namespace OtakLalat.World;

/// <summary>
/// Arena gabungan 4 kuadran (layout lama). Dipertahankan sebagai scene BENCHMARK headless
/// (res://scenes/Arena.tscn) agar data analisis lama tetap sebanding. Untuk uji interaktif pakai map di scenes/maps/.
/// </summary>
public partial class Arena : MapBase
{
    [Export] public int BrainPerType = 3;
    [Export] public int FsmPerType = 3;

    public override string MapTitle => "Arena Gabungan (benchmark)";
    public override MapStats Stats => MapStats.Food | MapStats.Poison | MapStats.Escape | MapStats.Danger;
    protected override bool UseDrone => true;
    protected override bool PropCollision => false;

    protected override bool TryStartBenchmark(System.Collections.Generic.Dictionary<string, string> args)
    {
        if (!args.ContainsKey("bench")) return false;
        AddChild(new Benchmark.BenchmarkRunner(this, args));
        return true;
    }

    protected override void SpawnInitialNpcs()
    {
        foreach (var t in TypeIds)
        {
            for (int i = 0; i < BrainPerType; i++) SpawnBrain(t, RandomSpot());
            for (int i = 0; i < FsmPerType; i++) SpawnFsm(t, RandomSpot());
        }
    }

    static readonly Vector3[] Rocks = { new(-8, 0.75f, -6), new(7, 0.75f, 9), new(10, 0.75f, -10), new(-12, 0.75f, 10), new(2, 0.75f, -14) };

    protected override void BuildLayout()
    {
        // 1. Petak lantai 4 Kuadran Eksperimen agar batas fungsi tiap area tampak jelas dari kamera atas (F2)
        AddGroundPatch(new Vector3(-10f, 0.005f, -10f), new Vector2(18f, 18f), new Color(0.28f, 0.54f, 0.25f)); // Kuadran 1: Kebun Makanan & Racun
        AddGroundPatch(new Vector3(-9f, 0.006f, 6f), new Vector2(18f, 20f), new Color(0.44f, 0.36f, 0.27f));    // Kuadran 2: Suaka Bahaya & Trauma
        AddGroundPatch(new Vector3(10f, 0.005f, 10f), new Vector2(18f, 18f), new Color(0.43f, 0.55f, 0.29f));   // Kuadran 3: Koridor Predator Looming
        AddGroundPatch(new Vector3(10f, 0.005f, -10f), new Vector2(18f, 18f), new Color(0.25f, 0.46f, 0.24f));  // Kuadran 4: Hutan Navigasi Kompas

        // 2. Rintangan batu alam dengan tekstur PBR Rocks.png
        AddRocks(Rocks);

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
}
