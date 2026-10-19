using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OtakLalat.Brain.Core;

namespace OtakLalat.UI;

/// <summary>Raster spike dari sampel neuron otak NPC terpilih.</summary>
public partial class SpikeRaster : Control
{
    const int Columns = 240;
    int[] rows = Array.Empty<int>();
    int[] split = Array.Empty<int>();
    readonly Queue<bool[]> cols = new();
    FlyBrain brain;

    public void Bind(FlyBrain b)
    {
        brain = b;
        cols.Clear();
        var c = b.State.C;
        var list = new List<int>();
        var s = new List<int>();
        void Take(string g, int max) { foreach (var i in c.Group(g).Take(max)) list.Add(i); }
        foreach (var g in new[] { "in_loom_L", "in_loom_R", "in_sugar_L", "in_bitter_L", "in_odor_food_L", "in_odor_poison_L", "in_odor_neutral_L", "in_epg" }) Take(g, 8);
        s.Add(list.Count);
        Take("kc", 40); Take("mbon", 24); Take("in_dan_reward", 6); Take("in_dan_punish", 6);
        s.Add(list.Count);
        foreach (var g in new[] { "out_gf", "out_loom_L", "out_loom_R", "out_mn9", "out_turn_L", "out_turn_R" }) Take(g, 8);
        rows = list.ToArray();
        split = s.ToArray();
    }

    public void Push()
    {
        if (brain?.CaptureCounts == null) return;
        var col = new bool[rows.Length];
        for (int r = 0; r < rows.Length; r++) col[r] = brain.CaptureCounts[rows[r]] > 0;
        cols.Enqueue(col);
        while (cols.Count > Columns) cols.Dequeue();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.05f, 0.05f, 0.08f));
        if (rows.Length == 0) return;
        float h = Size.Y / rows.Length, w = Size.X / Columns;
        int x = 0;
        foreach (var col in cols)
        {
            for (int r = 0; r < col.Length; r++)
                if (col[r]) DrawRect(new Rect2(x * w, r * h, MathF.Max(1, w), MathF.Max(1, h)), r < split[0] ? Colors.Cyan : r < split[1] ? Colors.Yellow : Colors.OrangeRed);
            x++;
        }
        foreach (var s in split) DrawLine(new Vector2(0, s * h), new Vector2(Size.X, s * h), new Color(1, 1, 1, 0.3f));
    }
}
