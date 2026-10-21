using System.Diagnostics;
using Godot;
using OtakLalat.Brain;
using OtakLalat.Brain.Core;

namespace OtakLalat.Npc;

/// <summary>NPC yang dikendalikan sub-otak lalat (FlyWire). Sifat = <see cref="TraitProfile"/> saja.</summary>
public partial class BrainNpc : NpcBase
{
    [Export] public TraitProfile Profile;
    public ulong Seed = 1;
    public const float UpbringingSeconds = 3f;
    public FlyBrain Brain { get; private set; }
    public Traits Traits;   // bisa diubah langsung (slider debug)

    public override string TypeName => Profile?.DisplayName ?? "Lalat";
    public override string AiMethod => "otak";
    public override Color BodyColor => Profile?.BodyColor ?? Colors.White;
    protected override float HungerRate => 0.02f * Traits.Appetite;

    public override void _Ready()
    {
        Traits = Profile?.ToTraits() ?? Traits.Neutral;
        FovDeg = Profile?.FovDeg ?? 360f;
        Brain = new FlyBrain(BrainAssets.Connectome, BrainAssets.Params, Seed);
        // "masa kecil" yang sama untuk semua NPC otak: bau buah pernah dibarengi rasa manis (setara pengetahuan
        // bawaan FSM bahwa bau makanan itu baik). Setelah ini semua valensi berubah hanya lewat pengalaman di dunia.
        Brain.Pretrain(OdorChannel.Food, reward: true, seconds: UpbringingSeconds);
        base._Ready();
    }

    public override MotorCommand Decide(float dt)
    {
        long t0 = Stopwatch.GetTimestamp();
        var cmd = Brain.Tick(Frame, Traits, dt, BrainTimeScale);
        DecideMs = (float)Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        return cmd;
    }

    public override string StatusText
    {
        get
        {
            var c = Command;
            string act = c.Escape ? "KABUR!" : c.Eat ? "makan" : "jelajah";
            return $"{act}  lapar {Hunger:P0}  val {c.Valence:+0.0;-0.0}";
        }
    }
}
