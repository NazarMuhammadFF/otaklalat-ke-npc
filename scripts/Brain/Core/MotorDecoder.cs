using System;

namespace OtakLalat.Brain.Core;

/// <summary>Perintah gerak untuk tubuh NPC.</summary>
public struct MotorCommand
{
    public float Forward;   // 0..2.5 (1 = jalan normal, >1 = lari)
    public float Turn;      // -1..1, + = kiri
    public bool Escape;     // Giant Fiber aktif
    public bool Eat;        // MN9 aktif (proboscis)
    public float Valence;   // -1..1 dari MBON (debug/metrik)
}

/// <summary>
/// "Tubuh/VNC" abstrak: membaca laju descending/motor neuron dan MBON lalu menerjemahkannya ke gerak.
/// Konstanta di sini SAMA untuk semua tipe NPC (bukan bagian authoring per tipe).
/// </summary>
public sealed class MotorDecoder
{
    public const float FastTauS = 0.15f, AdaptTauS = 3f;
    public const float GfThreshHz = 40f, Mn9ThreshHz = 30f;
    public const float EscapeSpeed = 2.5f;
    public const float TurnNormHz = 20f, ValenceNormHz = 10f, OdorTurnGain = 6f;

    readonly BrainState s;
    readonly int[] gf, loomL, loomR, mn9, turnL, turnR, app, avoid;

    // laju yang dihaluskan (Hz)
    public float Gf, LoomL, LoomR, Mn9, TurnL, TurnR, Approach, Avoid;
    float turnBias;   // adaptasi lambat (high-pass) agar asimetri tonik tidak membuat NPC berputar terus

    public MotorDecoder(BrainState state)
    {
        s = state;
        gf = s.C.Group("out_gf"); mn9 = s.C.Group("out_mn9");
        loomL = s.C.Group("out_loom_L"); loomR = s.C.Group("out_loom_R");
        turnL = s.C.Group("out_turn_L"); turnR = s.C.Group("out_turn_R");
        app = s.C.Group("out_mbon_approach"); avoid = s.C.Group("out_mbon_avoid");
    }

    public void ResetState() { Gf = LoomL = LoomR = Mn9 = TurnL = TurnR = Approach = Avoid = turnBias = 0f; }

    public MotorCommand Decode(in SensorFrame f, float dtSec)
    {
        float a = 1f - MathF.Exp(-dtSec / FastTauS);
        Gf += a * (s.GroupRate(gf) - Gf);
        Mn9 += a * (s.GroupRate(mn9) - Mn9);
        LoomL += a * (s.GroupRate(loomL) - LoomL);
        LoomR += a * (s.GroupRate(loomR) - LoomR);
        TurnL += a * (s.GroupRate(turnL) - TurnL);
        TurnR += a * (s.GroupRate(turnR) - TurnR);
        Approach += a * (s.GroupRate(app) - Approach);
        Avoid += a * (s.GroupRate(avoid) - Avoid);

        float diff = TurnL - TurnR;
        turnBias += (1f - MathF.Exp(-dtSec / AdaptTauS)) * (diff - turnBias);

        var cmd = new MotorCommand
        {
            Valence = Math.Clamp((Approach - Avoid) / ValenceNormHz, -1f, 1f),
            Escape = Gf > GfThreshHz,
            Eat = Mn9 > Mn9ThreshHz,
        };
        float wander = (diff - turnBias) / TurnNormHz;
        // klinotaksis: valensi MB menentukan naik/turun gradien bau
        float oL = f.OdorTotalL, oR = f.OdorTotalR;
        float grad = (oL - oR) / (oL + oR + 1e-3f);
        float odorTurn = OdorTurnGain * cmd.Valence * grad;

        if (cmd.Escape)
        {
            // DN looming ipsilateral aktif -> menjauh ke sisi kontralateral
            cmd.Turn = LoomL >= LoomR ? -1f : 1f;
            cmd.Forward = EscapeSpeed;
        }
        else
        {
            cmd.Turn = Math.Clamp(wander + odorTurn, -1f, 1f);
            cmd.Forward = cmd.Eat ? 0f : 1f;
        }
        return cmd;
    }
}
