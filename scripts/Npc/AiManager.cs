using System.Diagnostics;
using Godot;

namespace OtakLalat.Npc;

/// <summary>
/// Menjalankan AI semua NPC per physics tick: indera (main thread) -> keputusan (paralel) -> tubuh (main thread).
/// Brain-LOD: NPC jauh dari kamera mensimulasikan sebagian waktu otak saja.
/// </summary>
public partial class AiManager : Node
{
    [Export] public bool LodEnabled = true;
    [Export] public float LodNear = 25f, LodFar = 50f;
    [Export] public bool Parallel = true;
    /// <summary>NPC yang sedang diinspeksi: selalu disimulasikan penuh (tanpa LOD) agar panel saraf tetap real-time dari sudut pandang mana pun.</summary>
    public NpcBase FocusNpc;

    /// <summary>Waktu dinding fase keputusan (ms) pada tick terakhir.</summary>
    public float LastDecideWallMs { get; private set; }
    /// <summary>Jumlah waktu CPU keputusan semua NPC (ms) pada tick terakhir.</summary>
    public float LastDecideCpuMs { get; private set; }

    public override void _Ready() => ProcessPhysicsPriority = -100;   // sebelum node lain

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var npcs = NpcBase.All;
        var cam = GetViewport()?.GetCamera3D();
        foreach (var n in npcs)
        {
            n.Sense(dt);
            n.BrainTimeScale = 1f;
            if (LodEnabled && cam != null && n != FocusNpc)
            {
                float d = cam.GlobalPosition.DistanceTo(n.GlobalPosition);
                n.BrainTimeScale = d < LodNear ? 1f : d < LodFar ? 0.5f : 0.25f;
            }
        }

        long t0 = Stopwatch.GetTimestamp();
        if (Parallel) System.Threading.Tasks.Parallel.For(0, npcs.Count, i => npcs[i].Command = npcs[i].Decide(dt));
        else for (int i = 0; i < npcs.Count; i++) npcs[i].Command = npcs[i].Decide(dt);
        LastDecideWallMs = (float)Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

        float cpu = 0f;
        foreach (var n in npcs) { n.Act(dt); cpu += n.DecideMs; }
        LastDecideCpuMs = cpu;
    }
}
