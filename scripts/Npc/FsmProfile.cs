using Godot;

namespace OtakLalat.Npc;

/// <summary>
/// Tipe NPC metode tradisional (FSM): setiap sifat harus diterjemahkan manual ke threshold/timer/peluang.
/// Dipakai sebagai baseline benchmark; jumlah field di sini dihitung tools/analysis/count_effort.py.
/// </summary>
[GlobalClass]
public partial class FsmProfile : Resource
{
    [Export] public string DisplayName = "NPC";
    [Export] public Color BodyColor = Colors.White;
    // takut
    [Export] public float FleeLoomThreshold = 0.3f;     // kekuatan looming minimum untuk kabur
    [Export] public float FleeDuration = 1.5f;          // detik
    // lapar / makan
    [Export] public float HungerRate = 0.02f;           // per detik
    [Export] public float SeekHungerThreshold = 0.35f;  // mulai mencari makanan
    [Export] public float EatHungerThreshold = 0.15f;   // berhenti makan di bawah ini
    [Export] public float OdorFollowGain = 2.5f;
    // pemilih
    [Export] public float PoisonEatChance = 0.05f;      // per detik kontak dengan racun
    // jelajah
    [Export] public float WanderTurnInterval = 2f;      // detik antar ganti arah
    [Export] public float WanderTurnMagnitude = 0.6f;
    // belajar
    [Export] public float RememberChance = 0.8f;        // peluang mengingat bau saat disakiti
    [Export] public float MemoryDuration = 240f;        // detik
    [Export] public float AvoidGain = 2.5f;
    // variasi
    [Export] public float TurnJitter = 0.15f;
}
