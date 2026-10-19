using System;
using Godot;
using OtakLalat.Brain.Core;

namespace OtakLalat.Brain;

/// <summary>Connectome dimuat sekali dan dibagi ke semua NPC.</summary>
public static class BrainAssets
{
    public const string BrainPath = "res://data/brain/fly_npc_brain.json";
    public static readonly LifParams Params = new();
    static readonly Lazy<Connectome> connectome = new(() =>
    {
        var json = FileAccess.GetFileAsString(BrainPath);
        if (string.IsNullOrEmpty(json))
            throw new InvalidOperationException($"{BrainPath} tidak ada. Jalankan tools/connectome/extract_subcircuits.py");
        var c = Connectome.FromJson(json, Params.WSyn);
        GD.Print($"[otak] {c.N} neuron, {c.Indices.Length} sinapsis, {c.PlasticW0.Length} sinapsis plastik");
        return c;
    });

    public static Connectome Connectome => connectome.Value;
}
