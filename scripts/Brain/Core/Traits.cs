namespace OtakLalat.Brain.Core;

/// <summary>
/// Knob sifat NPC. Setiap knob hanya mengubah gain/bias SATU jalur di otak lalat;
/// perilaku lainnya muncul dari connectome. Nilai 1 = netral (rentang wajar 0..2).
/// </summary>
public struct Traits
{
    public float Fearfulness;   // gain input looming (LPLC2/LC4)
    public float Appetite;      // gain GRN gula + laju naik rasa lapar
    public float Pickiness;     // gain GRN pahit
    public float Curiosity;     // drive tonik EPG (central complex)
    public float LearningRate;  // kekuatan plastisitas dopamin KC->MBON
    public float NeuralNoise;   // besar fluktuasi drive tonik central complex (variasi jelajah)

    public static Traits Neutral => new()
    {
        Fearfulness = 1, Appetite = 1, Pickiness = 1, Curiosity = 1, LearningRate = 1, NeuralNoise = 1,
    };

    public const int Count = 6;
}
