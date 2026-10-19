using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using OtakLalat.Brain.Core;
using Xunit;
using Xunit.Abstractions;

namespace OtakLalat.Tests;

public class BrainTests
{
    static readonly Lazy<Connectome> Brain = new(() =>
        Connectome.FromJson(File.ReadAllText(Path.Combine(Root, "data/brain/fly_npc_brain.json")), new LifParams().WSyn));

    static string Root
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "project.godot"))) d = d.Parent;
            return d?.FullName ?? throw new DirectoryNotFoundException("project.godot tidak ditemukan");
        }
    }

    readonly ITestOutputHelper output;
    public BrainTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Loader_CsrConsistent()
    {
        var c = Brain.Value;
        Assert.Equal(c.N + 1, c.IndPtr.Length);
        Assert.Equal(c.Indices.Length, c.IndPtr[^1]);
        Assert.All(c.Indices, j => Assert.InRange(j, 0, c.N - 1));
        foreach (var g in new[] { "in_sugar_L", "in_loom_L", "in_epg", "out_mn9", "out_gf", "out_turn_L", "kc", "mbon" })
            Assert.NotEmpty(c.Group(g));
        Assert.True(c.PlasticW0.Length > 1000, "sinapsis KC->MBON plastik");
    }

    /// <summary>LifSimulator C# vs referensi numpy (tools/connectome/make_reference.py).</summary>
    [Fact]
    public void Simulator_MatchesNumpyReference()
    {
        var c = Brain.Value;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "tests/reference/ref_rates.json")));
        var r = doc.RootElement;
        var p = new LifParams { Dt = r.GetProperty("dt").GetSingle() };
        float ms = r.GetProperty("ms").GetSingle();
        int trials = r.GetProperty("trials").GetInt32();
        var failures = new List<string>();
        foreach (var stim in r.GetProperty("stimuli").EnumerateObject())
        {
            var sums = new Dictionary<string, double>();
            for (int t = 0; t < trials; t++)
            {
                var s = new BrainState(c, p, (ulong)(100 + t));
                foreach (var inp in stim.Value.GetProperty("input").EnumerateObject())
                    s.SetDrive(s.AddDrive(inp.Name), inp.Value.GetSingle());
                s.Advance(ms);
                foreach (var o in stim.Value.GetProperty("rates_hz").EnumerateObject())
                    sums[o.Name] = sums.GetValueOrDefault(o.Name) + s.GroupRate(o.Name) / trials;
            }
            foreach (var o in stim.Value.GetProperty("rates_hz").EnumerateObject())
            {
                double expected = o.Value.GetDouble(), got = sums[o.Name];
                double tol = Math.Max(5.0, 0.2 * expected);
                string line = $"{stim.Name,-10} {o.Name,-18} numpy={expected,7:F1}  C#={got,7:F1} Hz";
                output.WriteLine(line + (Math.Abs(got - expected) > tol ? "  <-- BEDA" : ""));
                if (Math.Abs(got - expected) > tol) failures.Add(line);
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Simulator_DeterministicPerSeed()
    {
        var c = Brain.Value;
        int[] Run()
        {
            var s = new BrainState(c, new LifParams(), 42);
            s.SetDrive(s.AddDrive("in_loom_L"), 50);
            s.NoiseHz = 8;
            s.Advance(300);
            return (int[])s.Counts.Clone();
        }
        Assert.Equal(Run(), Run());
    }

    /// <summary>Hukuman (PPL1) yang dipasangkan dengan bau A melemahkan respons MBON-approach terhadap A, bukan B.</summary>
    [Fact]
    public void Plasticity_OdorSpecificAversiveLearning()
    {
        var c = Brain.Value;
        var brain = new FlyBrain(c, new LifParams(), 7);
        var t = Traits.Neutral;

        float Probe(string odor)
        {
            var probe = new BrainState(c, new LifParams(), 99);
            Array.Copy(brain.State.PlasticW, probe.PlasticW, probe.PlasticW.Length);
            probe.SetDrive(probe.AddDrive($"in_odor_{odor}_L"), 100);
            probe.SetDrive(probe.AddDrive($"in_odor_{odor}_R"), 100);
            probe.Advance(2000);
            return probe.GroupRate("out_mbon_approach") - probe.GroupRate("out_mbon_avoid");
        }

        float foodBefore = Probe("food"), neutralBefore = Probe("neutral");
        var f = new SensorFrame { OdorFoodL = 1, OdorFoodR = 1, Punish = 1 };
        for (int i = 0; i < 120; i++) brain.Tick(f, t, 1f / 60f);   // 2 detik bau + hukuman
        float foodAfter = Probe("food"), neutralAfter = Probe("neutral");
        output.WriteLine($"valensi food: {foodBefore:F1} -> {foodAfter:F1} Hz | neutral: {neutralBefore:F1} -> {neutralAfter:F1} Hz | w_rel={Plasticity.MeanRelativeWeight(brain.State):F3}");
        Assert.True(foodAfter < foodBefore - 2f, "valensi bau yang dihukum harus turun");
        Assert.True(Math.Abs(neutralAfter - neutralBefore) < Math.Abs(foodAfter - foodBefore), "bau lain relatif tidak terpengaruh");
    }

    /// <summary>Hadiah (PAM) yang dipasangkan dengan bau A menaikkan valensi A.</summary>
    [Fact]
    public void Plasticity_AppetitiveLearning()
    {
        var c = Brain.Value;
        var brain = new FlyBrain(c, new LifParams(), 8);
        float Probe(string odor)
        {
            var probe = new BrainState(c, new LifParams(), 99);
            Array.Copy(brain.State.PlasticW, probe.PlasticW, probe.PlasticW.Length);
            probe.SetDrive(probe.AddDrive($"in_odor_{odor}_L"), 100);
            probe.SetDrive(probe.AddDrive($"in_odor_{odor}_R"), 100);
            probe.Advance(2000);
            return probe.GroupRate("out_mbon_approach") - probe.GroupRate("out_mbon_avoid");
        }
        float before = Probe("food"), poisonBefore = Probe("poison");
        brain.Pretrain(OdorChannel.Food, reward: true, seconds: 3f);
        float after = Probe("food"), poisonAfter = Probe("poison");
        output.WriteLine($"valensi food: {before:F1} -> {after:F1} Hz | poison: {poisonBefore:F1} -> {poisonAfter:F1} Hz");
        Assert.True(after > before + 2f);
    }

    [Fact]
    public void Performance_StepCost()
    {
        var c = Brain.Value;
        var b = new FlyBrain(c, new LifParams(), 1);
        var f = new SensorFrame { OdorFoodL = 0.5f, OdorFoodR = 0.4f, Hunger = 0.5f };
        for (int i = 0; i < 30; i++) b.Tick(f, Traits.Neutral, 1f / 60f);
        var sw = Stopwatch.StartNew();
        int frames = 600;
        for (int i = 0; i < frames; i++) b.Tick(f, Traits.Neutral, 1f / 60f);
        double usPerFrame = sw.Elapsed.TotalMilliseconds * 1000 / frames;
        output.WriteLine($"neuron={c.N} sinapsis={c.Indices.Length}  {usPerFrame:F0} us/frame/NPC (60 Hz, dt={b.State.P.Dt} ms)");
        Assert.True(usPerFrame < 16000);
    }
}
