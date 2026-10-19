using System;
using System.IO;
using OtakLalat.Brain.Core;
using Xunit;
using Xunit.Abstractions;

namespace OtakLalat.Tests;

/// <summary>Uji perilaku tertutup (closed-loop) dengan tubuh 2D sederhana, tanpa Godot.</summary>
public class BehaviorTests
{
    readonly ITestOutputHelper output;
    public BehaviorTests(ITestOutputHelper output) => this.output = output;

    static Connectome Load()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(d.FullName, "project.godot"))) d = d.Parent;
        return Connectome.FromJson(File.ReadAllText(Path.Combine(d.FullName, "data/brain/fly_npc_brain.json")), new LifParams().WSyn);
    }

    // sama dengan OdorSource (Spread 3) + ambang deteksi NpcBase (0.05)
    static float Conc(float x, float y)
    {
        float d = MathF.Sqrt(x * x + y * y) / 3f;
        return Math.Clamp((1f / (1f + d * d) - 0.05f) / 0.95f, 0f, 1f);
    }

    /// <summary>Jarak minimum ke sumber bau makanan dalam 40 detik, dimulai 9 m dengan arah acak.</summary>
    float RunOdorTrial(Connectome c, ulong seed, Traits t, bool withOdor, out float meanValence)
    {
        var brain = new FlyBrain(c, new LifParams(), seed);
        var rng = new Random((int)seed);
        float ang0 = (float)(rng.NextDouble() * 2 * Math.PI);
        float x = 9f * MathF.Cos(ang0), y = 9f * MathF.Sin(ang0), h = (float)(rng.NextDouble() * 2 * Math.PI);
        float minD = 9f, dt = 1f / 30f, yaw = 0f, vsum = 0f;
        int n = 0;
        for (float time = 0; time < 40f; time += dt)
        {
            float fx = MathF.Cos(h), fy = MathF.Sin(h), lx = -fy, ly = fx;   // kiri = +90 derajat
            var f = new SensorFrame { Hunger = 0.6f, YawRate = yaw };
            if (withOdor)
            {
                f.OdorFoodL = MathF.Min(1, Conc(x + 0.4f * fx + 0.45f * lx, y + 0.4f * fy + 0.45f * ly));
                f.OdorFoodR = MathF.Min(1, Conc(x + 0.4f * fx - 0.45f * lx, y + 0.4f * fy - 0.45f * ly));
            }
            var cmd = brain.Tick(f, t, dt);
            yaw = cmd.Turn * 3f;
            h += yaw * dt;
            x += fx * cmd.Forward * 2.2f * dt; y += fy * cmd.Forward * 2.2f * dt;
            minD = MathF.Min(minD, MathF.Sqrt(x * x + y * y));
            vsum += cmd.Valence; n++;
        }
        meanValence = vsum / n;
        return minD;
    }

    [Fact]
    public void FoodOdor_IsApproachedMoreThanChance()
    {
        var c = Load();
        float withOdor = 0, without = 0;
        const int trials = 10;
        for (int s = 0; s < trials; s++)
        {
            float a = RunOdorTrial(c, (ulong)(s + 1), Traits.Neutral, true, out float val);
            float b = RunOdorTrial(c, (ulong)(s + 1), Traits.Neutral, false, out _);
            output.WriteLine($"seed {s + 1}: min jarak dengan bau {a:F2} m (valensi rata2 {val:+0.00;-0.00}), tanpa bau {b:F2} m");
            withOdor += a / trials; without += b / trials;
        }
        output.WriteLine($"rata-rata: dengan bau {withOdor:F2} m, tanpa bau {without:F2} m");
        Assert.True(withOdor < without, "bau makanan harus membuat NPC lebih dekat ke sumbernya");
    }

    /// <summary>Looming dari kiri memicu Giant Fiber dan belok ke kanan (menjauh); trait Fearfulness menurunkan ambang.</summary>
    [Fact]
    public void Looming_TriggersEscapeAwayFromThreat_ScaledByFearfulness()
    {
        var c = Load();
        (int escapes, float turn) Run(float fear, float loom)
        {
            var b = new FlyBrain(c, new LifParams(), 3);
            var t = Traits.Neutral; t.Fearfulness = fear;
            int esc = 0; float turnSum = 0;
            for (int i = 0; i < 60; i++)
            {
                var cmd = b.Tick(new SensorFrame { LoomL = loom }, t, 1 / 60f);
                if (cmd.Escape) { esc++; turnSum += cmd.Turn; }
            }
            return (esc, esc > 0 ? turnSum / esc : 0f);
        }
        var brave = Run(0.4f, 0.6f);
        var neutral = Run(1f, 0.6f);
        var fearful = Run(1.8f, 0.6f);
        output.WriteLine($"frame kabur (dari 60) pada looming 0.6: berani {brave.escapes}, netral {neutral.escapes} (belok {neutral.turn:+0.0;-0.0}), penakut {fearful.escapes}");
        Assert.True(neutral.escapes > 0, "looming harus memicu GF");
        Assert.True(neutral.turn < 0, "ancaman di kiri -> belok kanan (negatif)");
        Assert.True(fearful.escapes >= neutral.escapes && neutral.escapes >= brave.escapes);
        Assert.True(fearful.escapes > brave.escapes);
    }

    /// <summary>Gula memicu MN9 (makan) hanya bila cukup lapar; ambang lapar bergantung trait Appetite.</summary>
    [Fact]
    public void Feeding_DependsOnHungerAndAppetite()
    {
        var c = Load();
        bool Eats(float hunger, float appetite)
        {
            var b = new FlyBrain(c, new LifParams(), 4);
            var t = Traits.Neutral; t.Appetite = appetite;
            int eat = 0;
            for (int i = 0; i < 60; i++) if (b.Tick(new SensorFrame { SugarL = 1, SugarR = 1, Hunger = hunger }, t, 1 / 60f).Eat) eat++;
            return eat > 20;
        }
        output.WriteLine($"appetite 1: lapar 0.1 {Eats(0.1f, 1f)}, 0.8 {Eats(0.8f, 1f)} | appetite 1.8: lapar 0.3 {Eats(0.3f, 1.8f)} | appetite 0.8: lapar 0.3 {Eats(0.3f, 0.8f)}");
        Assert.False(Eats(0.1f, 1f));
        Assert.True(Eats(0.8f, 1f));
        Assert.True(Eats(0.3f, 1.8f));
        Assert.False(Eats(0.3f, 0.8f));
    }
}
