using System;
using System.Runtime.CompilerServices;

namespace OtakLalat.Brain.Core;

/// <summary>
/// Plastisitas KC->MBON termodulasi dopamin (Hige et al. 2015; Cohn et al. 2015):
/// koaktivasi KC dan DAN di kompartemen suatu MBON melemahkan sinapsis KC->MBON itu.
/// Kompartemen diambil dari connectome (sinapsis DAN->MBON), bukan didefinisikan manual.
///
///   dw = -lr * min(1, trace_KC * TraceGain) * dopamin_MBON * w0 * dt  +  (w0 - w) / tauRecovery * dt
/// </summary>
public static class Plasticity
{
    public const float BaseRate = 1f;          // laju belajar dasar (per detik), dikali trait LearningRate
    public const float TraceTauS = 1.0f;       // jejak eligibilitas KC (detik)
    public const float RecoveryTauS = 600f;    // lupa perlahan (detik)
    public const float DanThreshHz = 65f;      // di bawah ini DAN tidak memicu plastisitas (bau saja <= ~50 Hz)
    public const float DanRefHz = 90f;         // laju DAN yang dianggap "penuh" (drive penguat game ~80 Hz)
    public const float DanTauS = 0.3f;         // penghalusan laju DAN
    public const float TraceGain = 0.2f;       // eligibilitas jenuh pada ~5 spike KC dalam jendela jejak

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    /// <param name="danRate">laju DAN yang dihaluskan (Hz), state per NPC, panjang = jumlah DAN</param>
    public static void Update(BrainState s, float dtSec, float learningRate, float[] danRate, float[] mbonDopamine)
    {
        var c = s.C;
        int nDan = c.Dans.Length, nMbon = mbonDopamine.Length;
        Array.Clear(mbonDopamine);
        float a = 1f - MathF.Exp(-dtSec / DanTauS);
        for (int d = 0; d < nDan; d++)
        {
            float inst = s.WindowMs > 0 ? s.Counts[c.Dans[d]] * 1000f / s.WindowMs : 0f;
            danRate[d] += a * (inst - danRate[d]);
            // hanya burst DAN yang kuat (penguat) memicu plastisitas; aktivitas DAN akibat bau saja di bawah ambang
            float da = (danRate[d] - DanThreshHz) / (DanRefHz - DanThreshHz);
            if (da <= 0f) continue;
            for (int m = 0; m < nMbon; m++) mbonDopamine[m] += c.DanMbon[d, m] * da;
        }

        var pre = c.PlasticPre; var mb = c.PlasticMbon; var w0 = c.PlasticW0; var w = s.PlasticW;
        float rec = dtSec / RecoveryTauS;
        for (int k = 0; k < w.Length; k++)
        {
            float elig = MathF.Min(1f, s.KcTrace[pre[k]] * TraceGain);
            float dw = -learningRate * elig * mbonDopamine[mb[k]] * w0[k] * dtSec
                       + (w0[k] - w[k]) * rec;
            w[k] = Math.Clamp(w[k] + dw, 0f, w0[k]);
        }

        float decay = MathF.Exp(-dtSec / TraceTauS);
        foreach (int i in c.Group("kc")) s.KcTrace[i] *= decay;
    }

    /// <summary>Rata-rata kekuatan relatif sinapsis plastik (1 = belum belajar apa pun).</summary>
    public static float MeanRelativeWeight(BrainState s)
    {
        double sum = 0;
        var w0 = s.C.PlasticW0;
        for (int k = 0; k < w0.Length; k++) sum += s.PlasticW[k] / w0[k];
        return w0.Length == 0 ? 1f : (float)(sum / w0.Length);
    }
}
