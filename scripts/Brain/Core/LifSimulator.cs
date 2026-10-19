using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace OtakLalat.Brain.Core;

/// <summary>Konstanta model LIF Shiu et al. 2024 (satuan mV, ms).</summary>
public sealed class LifParams
{
    public float V0 = -52f, VRst = -52f, VTh = -45f;
    public float TMbr = 20f, Tau = 5f, TRfc = 2.2f, TDly = 1.8f;
    public float WSyn = 0.275f, FPoi = 250f;
    public float Dt = 0.5f;          // langkah game (referensi Shiu: 0.1 ms)
    public float NoiseW = 4f;        // mV per kejadian noise latar (sub-threshold; 7 mV diperlukan untuk spike)
}

/// <summary>
/// State otak satu NPC. Skema diskret identik dengan tools/connectome/lif.py.
/// Bobot dibagi bersama lewat <see cref="Connectome"/>; hanya bobot KC->MBON yang disalin per NPC.
/// </summary>
public sealed class BrainState
{
    public readonly Connectome C;
    public readonly LifParams P;
    readonly int n, nPad, delaySteps, rfcSteps;   // nPad: kelipatan lebar SIMD
    readonly float kv, decayG, poiW;
    const float SnapG = 1e-4f;   // mV; jauh di bawah resolusi yang berarti (threshold 7 mV)

    readonly float[] v, g, kick;
    readonly int[] rfc;
    readonly int[] noRfc;      // jumlah drive aktif yang menargetkan neuron ini
    readonly float[][] buf;
    int slot;
    float pendingMs;
    Rng rng;

    readonly List<int[]> driveMembers = new();
    readonly List<float> driveHz = new();
    readonly List<int> spiking = new(64);

    /// <summary>Laju noise latar per neuron (Hz).</summary>
    public float NoiseHz;
    /// <summary>Neuron penerima noise latar (null = semua).</summary>
    public int[] NoiseTargets;
    /// <summary>Jumlah spike per neuron sejak <see cref="ResetWindow"/>.</summary>
    public readonly int[] Counts;
    public float WindowMs { get; private set; }
    public long TotalSteps { get; private set; }

    // plastisitas KC->MBON
    public readonly float[] PlasticW;
    public readonly float[] KcTrace;

    public BrainState(Connectome c, LifParams p, ulong seed)
    {
        C = c; P = p; n = c.N;
        delaySteps = Math.Max(1, (int)MathF.Round(p.TDly / p.Dt));
        rfcSteps = (int)MathF.Round(p.TRfc / p.Dt);
        kv = p.Dt / p.TMbr;
        decayG = MathF.Exp(-p.Dt / p.Tau);
        poiW = p.WSyn * p.FPoi;
        nPad = (n + Vector<float>.Count - 1) / Vector<float>.Count * Vector<float>.Count;
        v = new float[nPad]; g = new float[nPad]; kick = new float[nPad];
        rfc = new int[nPad]; noRfc = new int[n];
        buf = new float[delaySteps][];
        for (int i = 0; i < delaySteps; i++) buf[i] = new float[nPad];
        Counts = new int[n];
        KcTrace = new float[n];
        PlasticW = (float[])c.PlasticW0.Clone();
        rng = new Rng(seed);
        Reset();
    }

    public void Reset()
    {
        Array.Fill(v, P.V0);
        Array.Clear(g); Array.Clear(kick); Array.Clear(rfc);
        foreach (var b in buf) Array.Clear(b);
        ResetWindow();
    }

    /// <summary>Daftarkan grup neuron sebagai input Poisson (seperti PoissonInput Shiu). Mengembalikan id drive.</summary>
    public int AddDrive(string group) => AddDrive(C.Group(group));

    public int AddDrive(int[] members)
    {
        driveMembers.Add(members);
        driveHz.Add(0f);
        return driveMembers.Count - 1;
    }

    public void SetDrive(int id, float hz)
    {
        hz = MathF.Max(0f, hz);
        float old = driveHz[id];
        driveHz[id] = hz;
        // target Poisson aktif tidak punya periode refraktori (sama seperti Shiu et al.)
        if ((old > 0) == (hz > 0)) return;
        int delta = hz > 0 ? 1 : -1;
        foreach (int i in driveMembers[id]) noRfc[i] += delta;
    }

    public float GetDrive(int id) => driveHz[id];

    /// <summary>Majukan simulasi sebesar ms (sisa pecahan langkah dibawa ke panggilan berikutnya).</summary>
    public void Advance(float ms)
    {
        pendingMs += ms;
        int steps = (int)(pendingMs / P.Dt);
        pendingMs -= steps * P.Dt;
        for (int s = 0; s < steps; s++) Step();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Step()
    {
        float dtS = P.Dt * 1e-3f;
        // input Poisson: jumlah kejadian per grup ~ Poisson(n * rate * dt), anggota dipilih acak
        for (int d = 0; d < driveMembers.Count; d++)
        {
            float hz = driveHz[d];
            if (hz <= 0) continue;
            var m = driveMembers[d];
            int k = rng.Poisson(m.Length * hz * dtS);
            for (int j = 0; j < k; j++) kick[m[rng.NextInt(m.Length)]] += poiW;
        }
        if (NoiseHz > 0)
        {
            int m = NoiseTargets?.Length ?? n;
            int k = rng.Poisson(m * NoiseHz * dtS);
            for (int j = 0; j < k; j++)
            {
                int r = rng.NextInt(m);
                kick[NoiseTargets == null ? r : NoiseTargets[r]] += P.NoiseW;
            }
        }

        var b = buf[slot];
        spiking.Clear();
        UpdateNeurons(b);
        Propagate(b);
        slot = (slot + 1) % delaySteps;
        WindowMs += P.Dt;
        TotalSteps++;
    }

    /// <summary>
    /// Update membran semua neuron secara SIMD tanpa percabangan (lebih cepat daripada melewati neuron
    /// istirahat karena pola aktif/istirahat tidak bisa ditebak CPU). Spike ditangani skalar per lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    void UpdateNeurons(float[] buffer)
    {
        int W = Vector<float>.Count;
        var vKv = new Vector<float>(kv); var vDecay = new Vector<float>(decayG);
        var vV0 = new Vector<float>(P.V0); var vTh = new Vector<float>(P.VTh);
        var vSnap = new Vector<float>(SnapG);
        var vSv = v.AsSpan(); var gS = g.AsSpan(); var kS = kick.AsSpan(); var bS = buffer.AsSpan(); var rS = rfc.AsSpan();
        for (int i = 0; i < nPad; i += W)
        {
            var gi = new Vector<float>(gS.Slice(i)) + new Vector<float>(bS.Slice(i));
            var vi = new Vector<float>(vSv.Slice(i));
            var ri = new Vector<int>(rS.Slice(i));
            var active = Vector.Equals(ri, Vector<int>.Zero);
            var vNew = vi + vKv * (vV0 - vi + gi);
            var gNew = gi * vDecay;
            gNew = Vector.ConditionalSelect(Vector.LessThan(Vector.Abs(gNew), vSnap), Vector<float>.Zero, gNew);   // cegah denormal
            vi = Vector.ConditionalSelect(active, vNew, vi) + new Vector<float>(kS.Slice(i));
            gi = Vector.ConditionalSelect(active, gNew, gi);
            ri = Vector.ConditionalSelect(active, ri, ri - Vector<int>.One);
            Vector<float>.Zero.CopyTo(bS.Slice(i));
            Vector<float>.Zero.CopyTo(kS.Slice(i));
            vi.CopyTo(vSv.Slice(i)); gi.CopyTo(gS.Slice(i)); ri.CopyTo(rS.Slice(i));
            if (Vector.GreaterThanAny(vi, vTh))
            {
                for (int j = i; j < i + W; j++)
                {
                    if (v[j] <= P.VTh) continue;
                    v[j] = P.VRst; g[j] = 0f;
                    rfc[j] = noRfc[j] > 0 ? 0 : rfcSteps;
                    spiking.Add(j);
                }
            }
        }
    }

    /// <summary>Propagasi spike: tiba di slot yang sama, delaySteps langkah kemudian.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    unsafe void Propagate(float[] buffer)
    {
        fixed (int* ip = C.IndPtr, idx = C.Indices, ps = C.PlasticSlot)
        fixed (float* w = C.Weights, pw = PlasticW, pb = buffer)
        {
            foreach (int i in spiking)
            {
                Counts[i]++;
                KcTrace[i] += 1f;
                int end = ip[i + 1];
                for (int e = ip[i]; e < end; e++)
                {
                    int sl = ps[e];
                    pb[idx[e]] += sl < 0 ? w[e] : pw[sl];
                }
            }
        }
    }

    public void ResetWindow()
    {
        Array.Clear(Counts);
        WindowMs = 0f;
    }

    /// <summary>Laju rata-rata grup (Hz) dalam jendela saat ini.</summary>
    public float GroupRate(int[] members)
    {
        if (members.Length == 0 || WindowMs <= 0) return 0f;
        long s = 0;
        foreach (int i in members) s += Counts[i];
        return s * 1000f / (members.Length * WindowMs);
    }

    public float GroupRate(string group) => GroupRate(C.Group(group));

    public float Voltage(int i) => v[i];
}
