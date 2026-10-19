using Godot;
using OtakLalat.Npc;

namespace OtakLalat.World;

/// <summary>Predator: melayang, lalu sesekali menukik cepat ke satu NPC (stimulus looming).</summary>
public partial class Drone : Node3D, ILooming
{
    [Export] public float AttackInterval = 8f;
    [Export] public float DiveSpeed = 6.5f;
    [Export] public float CruiseHeight = 6f;
    [Export] public float ArenaHalfSize = 18f;

    public Vector3 LoomPosition => GlobalPosition;
    public float LoomRadius => 0.7f;
    /// <summary>Dipanggil saat serangan dimulai (dipakai benchmark untuk mengukur latensi reaksi).</summary>
    public System.Action<NpcBase> AttackStarted;

    enum Mode { Cruise, Dive, Retreat }
    Mode mode = Mode.Cruise;
    NpcBase target;
    Vector3 cruiseGoal;
    float timer;
    RandomNumberGenerator rng = new();
    MeshInstance3D groundRing;
    StandardMaterial3D ringMat;

    public ulong Seed { set => rng.Seed = value; }

    public override void _EnterTree() => WorldRegistry.Loomers.Add(this);
    public override void _ExitTree() => WorldRegistry.Loomers.Remove(this);

    public override void _Ready()
    {
        var bodyMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.15f, 0.15f, 0.18f),
            EmissionEnabled = true,
            Emission = new Color(0.9f, 0.2f, 0.15f),
            EmissionEnergyMultiplier = 0.35f,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = LoomRadius, Height = LoomRadius * 1.4f },
            MaterialOverride = bodyMat,
        });
        // Cincin rotor drone
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = LoomRadius * 0.9f, OuterRadius = LoomRadius * 1.25f },
            MaterialOverride = bodyMat,
            Scale = new Vector3(1f, 0.25f, 1f),
        });

        ringMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.25f, 0.15f, 0.28f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        groundRing = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1.4f, BottomRadius = 1.4f, Height = 0.02f },
            MaterialOverride = ringMat,
        };
        AddChild(groundRing);

        timer = AttackInterval;
        NewCruiseGoal();
    }

    /// <summary>Memicu predator menukik langsung ke NPC tertentu (untuk skenario uji interaktif).</summary>
    public void DiveAt(NpcBase npc)
    {
        if (npc == null || !IsInstanceValid(npc)) return;
        target = npc;
        GlobalPosition = npc.GlobalPosition + new Vector3(3.5f, CruiseHeight, 3.5f);
        mode = Mode.Dive;
        AttackStarted?.Invoke(target);
    }

    void NewCruiseGoal() =>
        cruiseGoal = new Vector3(rng.RandfRange(-ArenaHalfSize, ArenaHalfSize), CruiseHeight, rng.RandfRange(-ArenaHalfSize, ArenaHalfSize));

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        switch (mode)
        {
            case Mode.Cruise:
                MoveTo(cruiseGoal, 3f, dt);
                if (GlobalPosition.DistanceTo(cruiseGoal) < 0.5f) NewCruiseGoal();
                timer -= dt;
                if (timer <= 0f && NpcBase.All.Count > 0)
                {
                    target = NpcBase.All[rng.RandiRange(0, NpcBase.All.Count - 1)];
                    mode = Mode.Dive;
                    AttackStarted?.Invoke(target);
                }
                break;
            case Mode.Dive:
                if (!IsInstanceValid(target)) { mode = Mode.Retreat; break; }
                var aim = target.GlobalPosition + new Vector3(0, 1f, 0);
                MoveTo(aim, DiveSpeed, dt);
                if (GlobalPosition.DistanceTo(aim) < 1.2f)
                {
                    target.OnPredatorHit();
                    mode = Mode.Retreat;
                }
                else if (GlobalPosition.Y < 1.5f && GlobalPosition.DistanceTo(aim) > 6f) mode = Mode.Retreat;
                break;
            case Mode.Retreat:
                MoveTo(new Vector3(GlobalPosition.X, CruiseHeight, GlobalPosition.Z), 4f, dt);
                if (GlobalPosition.Y >= CruiseHeight - 0.2f) { mode = Mode.Cruise; timer = AttackInterval * rng.RandfRange(0.7f, 1.3f); }
                break;
        }
        if (groundRing != null)
        {
            groundRing.GlobalPosition = new Vector3(GlobalPosition.X, 0.03f, GlobalPosition.Z);
            ringMat.AlbedoColor = mode == Mode.Dive
                ? new Color(1f, 0.15f, 0.1f, 0.55f)
                : new Color(0.2f, 0.2f, 0.25f, 0.22f);
        }
    }

    void MoveTo(Vector3 p, float speed, float dt) => GlobalPosition = GlobalPosition.MoveToward(p, speed * dt);
}
