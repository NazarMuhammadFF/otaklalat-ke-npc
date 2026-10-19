using Godot;
using OtakLalat.Brain.Core;

namespace OtakLalat.Brain;

/// <summary>
/// Tipe NPC berbasis otak lalat = 6 knob (lihat <see cref="Traits"/>). Inilah SELURUH authoring per tipe.
/// </summary>
[GlobalClass]
public partial class TraitProfile : Resource
{
    [Export] public string DisplayName = "NPC";
    [Export] public Color BodyColor = Colors.White;
    [Export(PropertyHint.Range, "0,2,0.05")] public float Fearfulness = 1f;
    [Export(PropertyHint.Range, "0,2,0.05")] public float Appetite = 1f;
    [Export(PropertyHint.Range, "0,2,0.05")] public float Pickiness = 1f;
    [Export(PropertyHint.Range, "0,2,0.05")] public float Curiosity = 1f;
    [Export(PropertyHint.Range, "0,2,0.05")] public float LearningRate = 1f;
    [Export(PropertyHint.Range, "0,2,0.05")] public float NeuralNoise = 1f;

    public Traits ToTraits() => new()
    {
        Fearfulness = Fearfulness, Appetite = Appetite, Pickiness = Pickiness,
        Curiosity = Curiosity, LearningRate = LearningRate, NeuralNoise = NeuralNoise,
    };
}
