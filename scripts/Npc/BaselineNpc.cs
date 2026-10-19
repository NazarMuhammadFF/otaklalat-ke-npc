using System;
using System.Diagnostics;
using Godot;
using OtakLalat.Brain.Core;

namespace OtakLalat.Npc;

/// <summary>
/// Baseline metode tradisional: Finite State Machine dengan semua sifat didefinisikan manual
/// (<see cref="FsmProfile"/>). Memakai indera dan tubuh yang SAMA dengan <see cref="BrainNpc"/>.
/// </summary>
public partial class BaselineNpc : NpcBase
{
    public enum State { Wander, SeekFood, Eat, Flee, Avoid }

    [Export] public FsmProfile Profile;
    public ulong Seed = 1;
    public State Current { get; private set; } = State.Wander;

    Rng rng;
    float stateTimer, wanderTurn, wanderTimer;
    float fleeDir;
    // memori eksplisit: bau mana yang pernah menyakiti dan sampai kapan diingat
    readonly float[] odorMemory = new float[3];   // 0 food, 1 poison, 2 neutral
    bool hurtPending;

    public override string TypeName => Profile?.DisplayName ?? "FSM";
    public override string AiMethod => "FSM";
    public override Color BodyColor => Profile?.BodyColor ?? Colors.Gray;
    protected override float HungerRate => Profile.HungerRate;

    public override void _Ready()
    {
        Profile ??= new FsmProfile();
        rng = new Rng(Seed);
        base._Ready();
    }

    protected override void OnHurt() => hurtPending = true;

    public override MotorCommand Decide(float dt)
    {
        long t0 = Stopwatch.GetTimestamp();
        var p = Profile;
        var f = Frame;
        stateTimer -= dt;
        for (int i = 0; i < odorMemory.Length; i++) odorMemory[i] = MathF.Max(0f, odorMemory[i] - dt);

        // belajar: ingat bau terkuat saat disakiti
        if (hurtPending)
        {
            hurtPending = false;
            if (rng.NextFloat() < p.RememberChance)
            {
                float[] o = { f.OdorFoodL + f.OdorFoodR, f.OdorPoisonL + f.OdorPoisonR, f.OdorNeutralL + f.OdorNeutralR };
                int k = o[0] >= o[1] && o[0] >= o[2] ? 0 : o[1] >= o[2] ? 1 : 2;
                if (o[k] > 0.05f) odorMemory[k] = p.MemoryDuration;
            }
        }

        // transisi
        float loom = MathF.Max(f.LoomL, f.LoomR);
        bool touchingFood = f.SugarL + f.SugarR > 0;
        bool touchingPoison = f.BitterL + f.BitterR > 0;
        if (loom > p.FleeLoomThreshold && Current != State.Flee)
        {
            Current = State.Flee; stateTimer = p.FleeDuration;
            fleeDir = f.LoomL >= f.LoomR ? -1f : 1f;
        }
        else if (Current == State.Flee && stateTimer > 0) { }
        else if (touchingFood && Hunger > p.EatHungerThreshold && odorMemory[0] <= 0) Current = State.Eat;
        else if (touchingPoison && rng.NextFloat() < p.PoisonEatChance * dt) Current = State.Eat;
        else if (RememberedOdor(f) > 0.05f) Current = State.Avoid;
        else if (Hunger > p.SeekHungerThreshold && f.OdorFoodL + f.OdorFoodR > 0.02f && odorMemory[0] <= 0) Current = State.SeekFood;
        else Current = State.Wander;

        // aksi
        var cmd = new MotorCommand { Forward = 1f };
        switch (Current)
        {
            case State.Flee:
                cmd.Escape = true; cmd.Forward = MotorDecoder.EscapeSpeed; cmd.Turn = fleeDir;
                break;
            case State.Eat:
                cmd.Eat = true; cmd.Forward = 0f;
                if (touchingFood && Hunger <= p.EatHungerThreshold) Current = State.Wander;
                break;
            case State.Avoid:
                cmd.Turn = -p.AvoidGain * Gradient(f, RememberedSide(f));
                cmd.Valence = -1f;
                break;
            case State.SeekFood:
                cmd.Turn = p.OdorFollowGain * Gradient(f, 0);
                cmd.Valence = 1f;
                break;
            default:
                wanderTimer -= dt;
                if (wanderTimer <= 0f)
                {
                    wanderTimer = p.WanderTurnInterval * (0.5f + rng.NextFloat());
                    wanderTurn = (rng.NextFloat() * 2f - 1f) * p.WanderTurnMagnitude;
                }
                cmd.Turn = wanderTurn;
                break;
        }
        cmd.Turn = Math.Clamp(cmd.Turn + (rng.NextFloat() * 2f - 1f) * p.TurnJitter, -1f, 1f);
        DecideMs = (float)Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        return cmd;
    }

    float RememberedOdor(in SensorFrame f) =>
        MathF.Max(odorMemory[0] > 0 ? f.OdorFoodL + f.OdorFoodR : 0f,
        MathF.Max(odorMemory[1] > 0 ? f.OdorPoisonL + f.OdorPoisonR : 0f,
                  odorMemory[2] > 0 ? f.OdorNeutralL + f.OdorNeutralR : 0f));

    int RememberedSide(in SensorFrame f)
    {
        float a = odorMemory[0] > 0 ? f.OdorFoodL + f.OdorFoodR : 0f;
        float b = odorMemory[1] > 0 ? f.OdorPoisonL + f.OdorPoisonR : 0f;
        float c = odorMemory[2] > 0 ? f.OdorNeutralL + f.OdorNeutralR : 0f;
        return a >= b && a >= c ? 0 : b >= c ? 1 : 2;
    }

    static float Gradient(in SensorFrame f, int kind)
    {
        (float l, float r) = kind switch
        {
            0 => (f.OdorFoodL, f.OdorFoodR),
            1 => (f.OdorPoisonL, f.OdorPoisonR),
            _ => (f.OdorNeutralL, f.OdorNeutralR),
        };
        return (l - r) / (l + r + 1e-3f);
    }

    public override string StatusText => $"{Current}  lapar {Hunger:P0}";
}
