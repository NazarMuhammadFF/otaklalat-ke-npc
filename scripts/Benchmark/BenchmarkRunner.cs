using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using OtakLalat.Npc;
using OtakLalat.World;

namespace OtakLalat.Benchmark;

/// <summary>
/// Benchmark headless. Contoh:
///   Godot --headless --fixed-fps 60 res://scenes/Arena.tscn -- --bench=brain --n=50 --seed=1 --duration=120
/// Argumen: bench=brain|fsm, n, seed, duration (detik game), lod=0|1, parallel=0|1, out=folder.
/// Keluaran CSV di bench_out/&lt;bench&gt;_n&lt;n&gt;_s&lt;seed&gt;/.
/// </summary>
public partial class BenchmarkRunner : Node
{
    sealed class Attack { public float T; public int Npc; public float Latency = -1; public bool Hit; public int HitsBefore; }

    readonly Arena arena;
    readonly Dictionary<string, string> args;
    string method, outDir;
    int n, traceId;
    float duration, t;
    readonly List<NpcBase> npcs = new();
    readonly List<Attack> attacks = new();
    readonly StringBuilder ticks = new("t,decide_wall_ms,decide_cpu_ms,managed_mb\n");
    long ticksCount;
    readonly Dictionary<NpcBase, int> ids = new();

    public BenchmarkRunner() { }

    public BenchmarkRunner(Arena arena, Dictionary<string, string> args)
    {
        this.arena = arena;
        this.args = args;
        Name = "BenchmarkRunner";
    }

    string Arg(string k, string d) => args.TryGetValue(k, out var v) ? v : d;

    public override void _Ready()
    {
        method = Arg("bench", "brain");
        n = int.Parse(Arg("n", "50"));
        duration = float.Parse(Arg("duration", "120"), CultureInfo.InvariantCulture);
        string seed = Arg("seed", "1");
        outDir = Path.Combine(ProjectSettings.GlobalizePath(Arg("out", "res://bench_out")), $"{method}{(Arg("vision", "scalar") == "retina" ? "_retina" : "")}_n{n}_s{seed}");
        arena.Ai.LodEnabled = Arg("lod", "0") == "1";
        arena.Ai.Parallel = Arg("parallel", "1") == "1";
        traceId = int.Parse(Arg("trace", "-1"));
        ProcessPhysicsPriority = 100;   // setelah AiManager

        // kamera di tepi arena setinggi mata pemain (acuan jarak brain-LOD: dekat < 25 m, jauh > 50 m)
        arena.AddChild(new Camera3D { Position = new Vector3(0, 1.7f, Arena.HalfSize - 2), Current = true });
        for (int i = 0; i < n; i++)
        {
            string type = Arena.TypeIds[i % Arena.TypeIds.Length];
            NpcBase npc = method == "fsm" ? arena.SpawnFsm(type, arena.RandomSpot()) : arena.SpawnBrain(type, arena.RandomSpot(), Arg("vision", "scalar") == "retina" ? OtakLalat.Brain.Core.VisionMode.Retinotopic : OtakLalat.Brain.Core.VisionMode.Scalar);
            ids[npc] = i;
            npcs.Add(npc);
        }
        arena.Predator.AttackInterval = 4f;
        arena.Predator.AttackStarted = target =>
            attacks.Add(new Attack { T = t, Npc = ids.GetValueOrDefault(target, -1), HitsBefore = target.Metrics.PredatorHits });
        GD.Print($"[bench] {method} n={n} durasi={duration}s lod={arena.Ai.LodEnabled} paralel={arena.Ai.Parallel} -> {outDir}");
    }

    public override void _PhysicsProcess(double delta)
    {
        t += (float)delta;
        ticksCount++;
        ticks.Append(CultureInfo.InvariantCulture, $"{t:F4},{arena.Ai.LastDecideWallMs:F4},{arena.Ai.LastDecideCpuMs:F4},{GC.GetTotalMemory(false) / 1e6:F1}\n");
        foreach (var a in attacks)
        {
            if (a.Npc < 0 || t - a.T > 4f) continue;
            var npc = npcs[a.Npc];
            if (a.Latency < 0 && npc.Command.Escape) a.Latency = t - a.T;
            if (npc.Metrics.PredatorHits > a.HitsBefore) a.Hit = true;
        }
        if (traceId >= 0 && traceId < npcs.Count && ticksCount % 30 == 0 && npcs[traceId] is BrainNpc tb)
        {
            var f = tb.Frame; var d = tb.Brain.Decoder;
            GD.Print(FormattableString.Invariant($"[trace {t:F1}] loom {f.LoomL:F2}/{f.LoomR:F2} odorF {f.OdorFoodL:F2}/{f.OdorFoodR:F2} odorP {f.OdorPoisonL:F2}/{f.OdorPoisonR:F2} odorN {f.OdorNeutralL:F2}/{f.OdorNeutralR:F2} sug {f.SugarL:F0}{f.SugarR:F0} bit {f.BitterL:F0}{f.BitterR:F0} rew {f.Reward:F0} pun {f.Punish:F0} | app {d.Approach:F1} av {d.Avoid:F1} gf {d.Gf:F0} mn9 {d.Mn9:F0} | w {Brain.Core.Plasticity.MeanRelativeWeight(tb.Brain.State):F3}"));
        }
        if (t >= duration) Finish();
    }

    void Finish()
    {
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "ticks.csv"), ticks.ToString());

        var ns = new StringBuilder("id,method,type,food_s,poison_bites,predator_hits,danger_entries,time_in_danger,escapes,punishments,distance,act_walk,act_turn,act_eat,act_escape,act_poison\n");
        var path = new StringBuilder("id,t,x,z,action,valence\n");
        foreach (var npc in npcs)
        {
            var m = npc.Metrics;
            float A(int k) => m.ActionTime.GetValueOrDefault(k);
            ns.Append(CultureInfo.InvariantCulture,
                $"{ids[npc]},{method},{npc.TypeName},{m.FoodSeconds:F2},{m.PoisonBites},{m.PredatorHits},{m.DangerEntries},{m.TimeInDanger:F2},{m.EscapeEvents},{m.Punishments},{m.Distance:F2},{A(0):F2},{A(1):F2},{A(2):F2},{A(3):F2},{A(4):F2}\n");
            foreach (var p in m.Path)
                path.Append(CultureInfo.InvariantCulture, $"{ids[npc]},{p.t:F2},{p.x:F3},{p.z:F3},{p.action},{p.valence:F3}\n");
        }
        File.WriteAllText(Path.Combine(outDir, "npcs.csv"), ns.ToString());
        File.WriteAllText(Path.Combine(outDir, "path.csv"), path.ToString());

        var at = new StringBuilder("t,npc,type,latency,hit\n");
        foreach (var a in attacks)
            at.Append(CultureInfo.InvariantCulture, $"{a.T:F2},{a.Npc},{(a.Npc >= 0 ? npcs[a.Npc].TypeName : "")},{a.Latency:F3},{(a.Hit ? 1 : 0)}\n");
        File.WriteAllText(Path.Combine(outDir, "attacks.csv"), at.ToString());

        var danger = WorldRegistry.Dangers.FirstOrDefault();
        File.WriteAllText(Path.Combine(outDir, "meta.json"),
            "{" + string.Join(",", args.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"")) +
            $",\"cores\":{System.Environment.ProcessorCount},\"ticks\":{ticksCount}" +
            (danger != null ? FormattableString.Invariant($",\"danger\":[{danger.GlobalPosition.X},{danger.GlobalPosition.Z},{danger.Radius}]") : "") + "}");
        GD.Print($"[bench] selesai -> {outDir}");
        GetTree().Quit();
    }
}
