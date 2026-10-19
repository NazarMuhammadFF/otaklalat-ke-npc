using Godot;

namespace OtakLalat.World;

/// <summary>Sumber bau: konsentrasi = Strength / (1 + (d / Spread)^2).</summary>
public partial class OdorSource : Node3D
{
    [Export] public OdorKind Kind;
    [Export] public float Strength = 1f;
    [Export] public float Spread = 3f;
    public bool Active = true;

    public override void _EnterTree() => WorldRegistry.Odors.Add(this);
    public override void _ExitTree() => WorldRegistry.Odors.Remove(this);

    public float ConcentrationAt(Vector3 p)
    {
        if (!Active) return 0f;
        float d = GlobalPosition.DistanceTo(p) / Spread;
        return Strength / (1f + d * d);
    }
}
