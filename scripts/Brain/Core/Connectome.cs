using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OtakLalat.Brain.Core;

/// <summary>
/// Sub-otak lalat hasil tools/connectome/extract_subcircuits.py (read-only, dipakai bersama semua NPC).
/// Matriks CSR: baris = neuron presinaps; bobot = jumlah sinapsis bertanda (dikali w_syn saat load).
/// </summary>
public sealed class Connectome
{
    public int N { get; private set; }
    public int[] IndPtr { get; private set; }
    public int[] Indices { get; private set; }
    public float[] Weights { get; private set; }        // mV
    public string[] Types { get; private set; }
    public char[] Sides { get; private set; }
    public Dictionary<string, int[]> Groups { get; } = new();
    /// <summary>Jumlah sektor retina (0 = peta retinotopik belum dimuat; lihat <see cref="Retina.LoadMap"/>).</summary>
    public int RetinaSectors { get; internal set; }

    // --- plastisitas KC -> MBON (Plasticity) ---
    public int[] PlasticSlot { get; private set; }      // per entri CSR: indeks sinapsis plastik atau -1
    public int[] PlasticPre { get; private set; }       // neuron KC
    public int[] PlasticMbon { get; private set; }      // indeks MBON lokal (0..M-1)
    public float[] PlasticW0 { get; private set; }
    public int[] Dans { get; private set; }             // neuron DAN, urutan: reward lalu punish
    public float[,] DanMbon { get; private set; }       // [dan, mbon], dinormalisasi per kolom MBON

    public int[] Group(string name) => Groups.TryGetValue(name, out var g) ? g : Array.Empty<int>();

    public static Connectome FromJson(string json, float wSyn)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var c = new Connectome();
        var neurons = root.GetProperty("neurons");
        c.N = neurons.GetArrayLength();
        c.Types = new string[c.N];
        c.Sides = new char[c.N];
        int k = 0;
        foreach (var n in neurons.EnumerateArray())
        {
            c.Types[k] = n.GetProperty("type").GetString();
            c.Sides[k] = n.GetProperty("side").GetString()[0];
            k++;
        }
        foreach (var g in root.GetProperty("groups").EnumerateObject())
            c.Groups[g.Name] = ToInts(g.Value);

        var csr = root.GetProperty("csr");
        c.IndPtr = ToInts(csr.GetProperty("indptr"));
        c.Indices = ToInts(csr.GetProperty("indices"));
        var w = csr.GetProperty("weights");
        c.Weights = new float[w.GetArrayLength()];
        k = 0;
        foreach (var x in w.EnumerateArray()) c.Weights[k++] = x.GetSingle() * wSyn;

        c.BuildPlasticity(root.GetProperty("dan_mbon"));
        return c;
    }

    void BuildPlasticity(JsonElement danMbon)
    {
        var kc = new HashSet<int>(Group("kc"));
        var mbon = Group("mbon");
        var mbonLocal = new Dictionary<int, int>();
        for (int i = 0; i < mbon.Length; i++) mbonLocal[mbon[i]] = i;

        PlasticSlot = new int[Indices.Length];
        var pre = new List<int>(); var post = new List<int>(); var w0 = new List<float>();
        for (int i = 0; i < N; i++)
            for (int e = IndPtr[i]; e < IndPtr[i + 1]; e++)
            {
                PlasticSlot[e] = -1;
                if (kc.Contains(i) && mbonLocal.TryGetValue(Indices[e], out int m) && Weights[e] > 0)
                {
                    PlasticSlot[e] = pre.Count;
                    pre.Add(i); post.Add(m); w0.Add(Weights[e]);
                }
            }
        PlasticPre = pre.ToArray(); PlasticMbon = post.ToArray(); PlasticW0 = w0.ToArray();

        var reward = Group("in_dan_reward"); var punish = Group("in_dan_punish");
        Dans = new int[reward.Length + punish.Length];
        reward.CopyTo(Dans, 0); punish.CopyTo(Dans, reward.Length);
        DanMbon = new float[Dans.Length, mbon.Length];
        int d = 0;
        foreach (var row in danMbon.EnumerateArray())
        {
            int m = 0;
            foreach (var x in row.EnumerateArray()) DanMbon[d, m++] = x.GetSingle();
            d++;
        }
        for (int m = 0; m < mbon.Length; m++)
        {
            float s = 0;
            for (d = 0; d < Dans.Length; d++) s += DanMbon[d, m];
            if (s > 0) for (d = 0; d < Dans.Length; d++) DanMbon[d, m] /= s;
        }
    }

    static int[] ToInts(JsonElement arr)
    {
        var r = new int[arr.GetArrayLength()];
        int i = 0;
        foreach (var x in arr.EnumerateArray()) r[i++] = x.GetInt32();
        return r;
    }
}
