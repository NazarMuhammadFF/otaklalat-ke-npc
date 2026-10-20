using System;

namespace OtakLalat.Brain.Core;

/// <summary>Input sensorik dari dunia game (dinormalisasi 0..1 kecuali disebutkan).</summary>
public struct SensorFrame
{
    public float LoomL, LoomR;                  // kekuatan looming per mata
    public float[] LoomBinsL, LoomBinsR;        // looming per sektor retina (null = mode skalar)
    public float SugarL, SugarR, BitterL, BitterR;
    public float OdorFoodL, OdorFoodR, OdorPoisonL, OdorPoisonR, OdorNeutralL, OdorNeutralR;
    public float YawRate;                       // rad/s, + = belok kiri (umpan balik gerak sendiri)
    public float SunBearing;                    // rad relatif arah hadap, + = kiri
    public float Hunger;
    public float Reward, Punish;                // sinyal penguat dari tubuh (makan / sakit)

    public readonly float OdorTotalL => OdorFoodL + OdorPoisonL + OdorNeutralL;
    public readonly float OdorTotalR => OdorFoodR + OdorPoisonR + OdorNeutralR;
}

/// <summary>Memetakan <see cref="SensorFrame"/> ke laju Poisson grup neuron input.</summary>
public sealed class SensorEncoder
{
    public const float LoomHz = 60f, SugarHz = 100f, BitterHz = 100f, OdorHz = 100f, OdorHalfSat = 0.05f, OdorIpsi = 0.6f;
    public const float EpgHz = 40f, PenHz = 60f, ErHz = 30f, DanHz = 80f;
    /// <summary>Fluktuasi drive tonik EPG (proses Ornstein-Uhlenbeck): sumber variasi jelajah.</summary>
    public const float DriveNoiseSigma = 0.6f, DriveNoiseTauS = 0.8f;

    readonly BrainState s;
    Rng rng;
    float ou;   // state OU, rata-rata 0
    readonly int loomL, loomR, sugarL, sugarR, bitterL, bitterR, epg, penL, penR, erL, erR, reward, punish;
    readonly int[] odor = new int[6];
    readonly int[] binL, binR;                  // drive per sektor retina (kosong bila peta belum dimuat)

    public SensorEncoder(BrainState state, ulong seed)
    {
        s = state;
        rng = new Rng(seed ^ 0x5EED5EEDUL);
        loomL = s.AddDrive("in_loom_L"); loomR = s.AddDrive("in_loom_R");
        sugarL = s.AddDrive("in_sugar_L"); sugarR = s.AddDrive("in_sugar_R");
        bitterL = s.AddDrive("in_bitter_L"); bitterR = s.AddDrive("in_bitter_R");
        epg = s.AddDrive("in_epg");
        penL = s.AddDrive("in_pen_L"); penR = s.AddDrive("in_pen_R");
        erL = s.AddDrive("in_er_L"); erR = s.AddDrive("in_er_R");
        reward = s.AddDrive("in_dan_reward"); punish = s.AddDrive("in_dan_punish");
        int k = s.C.RetinaSectors;
        binL = new int[k]; binR = new int[k];
        for (int i = 0; i < k; i++)
        {
            binL[i] = s.AddDrive($"{Retina.GroupPrefix}L_s{i}");
            binR[i] = s.AddDrive($"{Retina.GroupPrefix}R_s{i}");
        }
        string[] names = { "food_L", "food_R", "poison_L", "poison_R", "neutral_L", "neutral_R" };
        for (int i = 0; i < names.Length; i++) odor[i] = s.AddDrive("in_odor_" + names[i]);
    }

    /// <summary>Respons PN dengan normalisasi divisif antennal lobe (Olsen et al. 2010): bau lemah tetap terdeteksi.</summary>
    public static float Pn(float c) => c <= 0f ? 0f : (1f + OdorHalfSat) * c / (c + OdorHalfSat);

    /// <summary>ORN memproyeksi ke kedua antennal lobe dengan bias ipsilateral (Gaudry et al. 2013).</summary>
    void SetOdor(int i, float left, float right)
    {
        s.SetDrive(odor[i], OdorHz * Pn(OdorIpsi * left + (1f - OdorIpsi) * right));
        s.SetDrive(odor[i + 1], OdorHz * Pn(OdorIpsi * right + (1f - OdorIpsi) * left));
    }

    public void Apply(in SensorFrame f, in Traits t, float dtSec)
    {
        // OU: dx = -x/tau dt + sqrt(2/tau) dW  (varians stasioner 1)
        float z = (rng.NextFloat() + rng.NextFloat() + rng.NextFloat() - 1.5f) * 2f;   // ~N(0,1)
        ou += -ou * dtSec / DriveNoiseTauS + MathF.Sqrt(2f * dtSec / DriveNoiseTauS) * z;
        // Retinotopik: tiap sektor mendapat looming-nya sendiri; skalar: satu angka untuk seluruh grup mata.
        // Hanya satu jalur yang aktif pada satu waktu (yang lain 0) agar tidak terjadi penghitungan ganda.
        bool retina = f.LoomBinsL != null && f.LoomBinsR != null && f.LoomBinsL.Length == binL.Length && binL.Length > 0;
        s.SetDrive(loomL, retina ? 0f : LoomHz * t.Fearfulness * f.LoomL);
        s.SetDrive(loomR, retina ? 0f : LoomHz * t.Fearfulness * f.LoomR);
        for (int i = 0; i < binL.Length; i++)
        {
            s.SetDrive(binL[i], retina ? LoomHz * Retina.Gain * t.Fearfulness * f.LoomBinsL[i] : 0f);
            s.SetDrive(binR[i], retina ? LoomHz * Retina.Gain * t.Fearfulness * f.LoomBinsR[i] : 0f);
        }
        // lapar menaikkan sensitivitas GRN gula (Inagaki et al. 2014)
        float sugarGain = SugarHz * t.Appetite * Math.Clamp(0.15f + f.Hunger, 0f, 1.2f);
        s.SetDrive(sugarL, sugarGain * f.SugarL);
        s.SetDrive(sugarR, sugarGain * f.SugarR);
        s.SetDrive(bitterL, BitterHz * t.Pickiness * f.BitterL);
        s.SetDrive(bitterR, BitterHz * t.Pickiness * f.BitterR);
        SetOdor(0, f.OdorFoodL, f.OdorFoodR);
        SetOdor(2, f.OdorPoisonL, f.OdorPoisonR);
        SetOdor(4, f.OdorNeutralL, f.OdorNeutralR);
        s.SetDrive(epg, EpgHz * t.Curiosity * MathF.Max(0f, 1f + DriveNoiseSigma * t.NeuralNoise * ou));
        float yaw = Math.Clamp(f.YawRate / 2f, -1f, 1f);
        s.SetDrive(penL, PenHz * MathF.Max(0f, yaw));
        s.SetDrive(penR, PenHz * MathF.Max(0f, -yaw));
        // ring neuron (inhibitorik) membawa posisi landmark ke kompas EPG
        s.SetDrive(erL, ErHz * (0.5f + 0.5f * MathF.Sin(f.SunBearing)));
        s.SetDrive(erR, ErHz * (0.5f - 0.5f * MathF.Sin(f.SunBearing)));
        s.SetDrive(reward, DanHz * f.Reward);
        s.SetDrive(punish, DanHz * f.Punish);
    }
}
