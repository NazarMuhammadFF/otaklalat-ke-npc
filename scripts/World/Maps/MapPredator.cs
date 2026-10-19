using Godot;

namespace OtakLalat.World;

/// <summary>Map uji Fearfulness: lapangan terbuka tanpa tempat sembunyi, drone aktif, pemain bisa sprint ke NPC.</summary>
public partial class MapPredator : MapBase
{
    public override string MapTitle => "Koridor Predator";
    public override MapStats Stats => MapStats.Escape;
    protected override bool UseDrone => true;
    protected override Color FloorColor => new(0.50f, 0.60f, 0.32f);

    protected override void BuildLayout()
    {
        for (int i = 0; i < 6; i++)
            SpawnProp("res://assets/models/nature/Grass_Small.gltf", new Vector3(-15f + i * 6f, 0, (i % 2 == 0 ? -14f : 14f)), 1.3f, i * 60f);
        AddZoneSign(new Vector3(0f, 6.5f, -18f), "MAP: KORIDOR PREDATOR\n(Uji Fearfulness - refleks looming)", new Color(1f, 0.85f, 0.45f));
    }
}
