using System;

namespace OtakLalat.Brain.Core;

public enum OdorChannel { Food, Poison, Neutral }

/// <summary>Satu otak NPC: encoder -> simulasi LIF -> decoder -> plastisitas.</summary>
public sealed class FlyBrain
{
    public readonly BrainState State;
    public readonly SensorEncoder Encoder;
    public readonly MotorDecoder Decoder;
    readonly float[] mbonDopamine, danRate;

    public MotorCommand LastCommand { get; private set; }
    /// <summary>Jika tidak null, jumlah spike tiap tick disalin ke sini (raster di overlay debug).</summary>
    public int[] CaptureCounts;

    public FlyBrain(Connectome c, LifParams p, ulong seed)
    {
        State = new BrainState(c, p, seed);
        Encoder = new SensorEncoder(State, seed);
        Decoder = new MotorDecoder(State);
        mbonDopamine = new float[c.Group("mbon").Length];
        danRate = new float[c.Dans.Length];
    }

    /// <param name="timeScale">fraksi waktu otak yang disimulasikan (brain-LOD; 1 = real-time)</param>
    public MotorCommand Tick(in SensorFrame f, in Traits t, float dtSec, float timeScale = 1f)
    {
        Encoder.Apply(f, t, dtSec);
        State.Advance(dtSec * 1000f * timeScale);
        if (State.WindowMs <= 0) return LastCommand;   // belum ada langkah penuh
        LastCommand = Decoder.Decode(f, dtSec);
        Plasticity.Update(State, State.WindowMs * 1e-3f, Plasticity.BaseRate * t.LearningRate, danRate, mbonDopamine);
        if (CaptureCounts != null) Array.Copy(State.Counts, CaptureCounts, CaptureCounts.Length);
        State.ResetWindow();
        return LastCommand;
    }

    /// <summary>
    /// Pengalaman awal ("masa kecil"): bau dipasangkan dengan hadiah atau hukuman memakai plastisitas yang sama.
    /// Valensi bawaan dibentuk lewat pengalaman, bukan aturan tertulis.
    /// </summary>
    public void Pretrain(OdorChannel odor, bool reward, float seconds, float learningRate = 1f)
    {
        var f = new SensorFrame { Reward = reward ? 1f : 0f, Punish = reward ? 0f : 1f };
        switch (odor)
        {
            case OdorChannel.Food: f.OdorFoodL = f.OdorFoodR = 1f; break;
            case OdorChannel.Poison: f.OdorPoisonL = f.OdorPoisonR = 1f; break;
            default: f.OdorNeutralL = f.OdorNeutralR = 1f; break;
        }
        var t = Traits.Neutral; t.LearningRate = learningRate; t.Curiosity = 0f;
        const float dt = 1f / 60f;
        for (float time = 0; time < seconds; time += dt) Tick(f, t, dt);
        // istirahat tanpa stimulus agar aktivitas kembali tenang sebelum masuk dunia
        var rest = new SensorFrame();
        for (float time = 0; time < 1f; time += dt) Tick(rest, t, dt);
        Decoder.ResetState();
    }
}
