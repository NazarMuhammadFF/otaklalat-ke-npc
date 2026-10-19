using Godot;

namespace OtakLalat.World;

/// <summary>Map uji Appetite &amp; Pickiness: pasangan semak manis (kiri) dan beracun (kanan) berdampingan. Tanpa bahaya, tanpa drone.</summary>
public partial class MapMakanRacun : MapBase
{
    public override string MapTitle => "Makan vs Racun";
    public override MapStats Stats => MapStats.Food | MapStats.Poison;
    protected override Color FloorColor => new(0.30f, 0.56f, 0.28f);

    protected override void BuildLayout()
    {
        // lima pasangan makanan (x=-3) dan racun (x=+3) sejajar; jarak antar pasangan cukup agar bau tidak tumpang tindih
        for (int i = 0; i < 5; i++)
        {
            float z = -14f + i * 7f;
            SpawnEdible(new Vector3(-3f, 0, z), false);
            SpawnEdible(new Vector3(3f, 0, z), true);
            SpawnProp("res://assets/models/nature/Flower_1_Clump.gltf", new Vector3(-5f, 0, z + 1.5f), 1.1f, i * 70f);
            SpawnProp("res://assets/models/nature/Flower_2_Clump.gltf", new Vector3(5f, 0, z - 1.5f), 1.1f, i * 40f);
        }
        AddGroundPatch(new Vector3(-3f, 0.005f, 0f), new Vector2(6f, 36f), new Color(0.45f, 0.75f, 0.35f));
        AddGroundPatch(new Vector3(3f, 0.005f, 0f), new Vector2(6f, 36f), new Color(0.55f, 0.35f, 0.65f));
        AddZoneSign(new Vector3(-3f, 3.3f, -16f), "MAKANAN MANIS", new Color(0.6f, 1f, 0.55f));
        AddZoneSign(new Vector3(3f, 3.3f, -16f), "RACUN PAHIT", new Color(0.85f, 0.6f, 1f));
        AddZoneSign(new Vector3(0f, 4.5f, -18f), "MAP: MAKAN vs RACUN\n(Uji Appetite & Pickiness)", new Color(1f, 1f, 1f));
    }
}
