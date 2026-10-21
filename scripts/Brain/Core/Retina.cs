using System;
using System.Text.Json;

namespace OtakLalat.Brain.Core;

/// <summary>Cara penglihatan looming diteruskan ke otak (variabel eksperimen, bukan knob sifat).</summary>
public enum VisionMode { Scalar, Retinotopic }

/// <summary>
/// Retina 1D: tiap mata dibagi K sektor azimut (0 = depan ... K-1 = belakang). Murni matematika, tanpa Godot.
/// Bearing: + = kiri, 0 = lurus depan. Mata kiri melihat [-FrontOverlap, EyeLimit(fov)], mata kanan cerminannya
/// (tumpang tindih binokular di depan, sama dengan aturan skalar di NpcBase.Sense). FOV total 360 = tanpa titik buta;
/// FOV < 360 menyisakan titik buta (360 - FOV) di belakang dan mempersempit sektor (jumlah sektor & peta neuron tetap).
/// </summary>
public static class Retina
{
    public const float FrontOverlap = 0.3f;
    public const string GroupPrefix = "in_loom_";
    /// <summary>
    /// Penguat drive per sektor. Satu sektor hanya ~1/8 neuron looming, jadi tanpa penguat ancaman berukuran pemain
    /// (setengah lebar sudut ~0,3 rad) tidak mencapai ambang Giant Fiber, padahal jalur skalar mengaktifkan seluruh grup mata.
    /// Nilai 2 dikalibrasi (BehaviorTests) agar ancaman sebesar itu memicu kabur setara skalar; stimulus sangat kecil tetap lemah.
    /// </summary>
    public const float Gain = 2f;

    /// <summary>Total FOV gabungan kedua mata (rad) untuk spesies tanpa titik buta; nilai default semua parameter FOV.</summary>
    public const float FullFov = 2f * MathF.PI;

    /// <summary>Bearing terjauh (rad, dari depan) yang dilihat satu mata: setengah FOV total, paling jauh 180 derajat.</summary>
    public static float EyeLimit(float fovRad = FullFov) => MathF.Min(MathF.PI, fovRad / 2f);

    /// <summary>Lebar tiap sektor = rentang satu mata [-FrontOverlap, EyeLimit] dibagi jumlah sektor.</summary>
    public static float SectorWidth(int k, float fovRad = FullFov) => (EyeLimit(fovRad) + FrontOverlap) / k;
    public static float SectorCenter(int sector, int k, float fovRad = FullFov) => -FrontOverlap + (sector + 0.5f) * SectorWidth(k, fovRad);

    /// <summary>Tambahkan stimulus (arah <paramref name="bearing"/>, setengah lebar sudut <paramref name="halfWidth"/>, kekuatan s) ke sektor yang tertutup.</summary>
    public static void Add(float bearing, float halfWidth, float s, float[] binsL, float[] binsR, float fovRad = FullFov)
    {
        Fill(bearing, halfWidth, s, binsL, fovRad);
        Fill(-bearing, halfWidth, s, binsR, fovRad);
    }

    static void Fill(float bearing, float halfWidth, float s, float[] bins, float fovRad)
    {
        int k = bins.Length;
        float half = SectorWidth(k, fovRad) / 2f;
        for (int i = 0; i < k; i++)
            if (MathF.Abs(bearing - SectorCenter(i, k, fovRad)) <= halfWidth + half)
                bins[i] = MathF.Max(bins[i], s);
    }

    /// <summary>Muat data/brain/retina_map.json ke <see cref="Connectome.Groups"/> (in_loom_L_s0 ...). Mengembalikan jumlah sektor.</summary>
    public static int LoadMap(Connectome c, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        foreach (var g in root.GetProperty("groups").EnumerateObject())
        {
            var arr = new int[g.Value.GetArrayLength()];
            int i = 0;
            foreach (var x in g.Value.EnumerateArray()) arr[i++] = x.GetInt32();
            c.Groups[g.Name] = arr;
        }
        c.RetinaSectors = root.GetProperty("k").GetInt32();
        return c.RetinaSectors;
    }
}
