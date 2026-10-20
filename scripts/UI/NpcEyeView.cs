using System.Collections.Generic;
using Godot;
using OtakLalat.Brain.Core;
using OtakLalat.Npc;

namespace OtakLalat.UI;

/// <summary>
/// Kartu "pandangan NPC" dengan dua mode:
///   0 = Kamera: render 3D dari kepala NPC terpilih (ilustrasi, BUKAN input otak).
///   1 = Input Otak: apa yang benar-benar dikirim ke otak (<see cref="SensorFrame"/> per sisi kiri/kanan,
///       untuk NPC Otak ditampilkan sebagai laju Poisson ke neuron input).
/// Kamera hidup di SubViewport sendiri yang berbagi World3D dengan viewport map, jadi tidak mencuri kamera pemain.
/// </summary>
public partial class NpcEyeView : VBoxContainer
{
    static readonly Vector3 EyeOffset = new(0, 0.95f, -0.45f);
    // lapisan visual 20 (bit 19): tubuh NPC terpilih disembunyikan dari kamera mata, tetap terlihat di kamera lain
    const uint SelfLayer = 1u << 19;

    public OptionButton ModeSelect { get; }
    readonly SubViewport vp;
    readonly Camera3D eyeCam;
    readonly SubViewportContainer camBox;
    readonly SensorPanel sensor;
    readonly Label placeholder;
    readonly List<(VisualInstance3D node, uint layers)> hidden = new();
    NpcBase npc;

    public NpcEyeView()
    {
        SizeFlagsVertical = SizeFlags.ExpandFill;
        ModeSelect = new OptionButton { FocusMode = FocusModeEnum.None, FitToLongestItem = false };
        ModeSelect.AddItem("📷 Kamera NPC");
        ModeSelect.AddItem("🧠 Input Otak");
        ModeSelect.AddThemeFontSizeOverride("font_size", 10);

        var stack = new Control { CustomMinimumSize = new Vector2(0, 150), SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        AddChild(stack);

        camBox = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        camBox.SetAnchorsPreset(LayoutPreset.FullRect);
        vp = new SubViewport { Size = new Vector2I(320, 200), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, HandleInputLocally = false };
        camBox.AddChild(vp);
        eyeCam = new Camera3D { Fov = 110f, Far = 80f, Current = true, CullMask = 0xFFFFFu & ~SelfLayer };
        vp.AddChild(eyeCam);
        stack.AddChild(camBox);

        sensor = new SensorPanel { MouseFilter = MouseFilterEnum.Ignore };
        sensor.SetAnchorsPreset(LayoutPreset.FullRect);
        stack.AddChild(sensor);

        placeholder = new Label
        {
            Text = "Pilih hewan untuk melihat pandangannya",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore,
        };
        placeholder.SetAnchorsPreset(LayoutPreset.FullRect);
        placeholder.AddThemeFontSizeOverride("font_size", 11);
        placeholder.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.8f));
        stack.AddChild(placeholder);

        ModeSelect.ItemSelected += _ => Refresh();
        Refresh();
    }

    /// <summary>Pakai dunia 3D yang sama dengan viewport map (tanpa memuat ulang dunia).</summary>
    public void ShareWorld(SubViewport worldViewport) => vp.World3D = worldViewport.FindWorld3D();

    public void Bind(NpcBase n)
    {
        RestoreLayers();
        npc = n;
        sensor.Npc = n;
        if (n != null) HideSelf(n);
        Refresh();
    }

    void HideSelf(Node root)
    {
        foreach (var c in root.GetChildren())
        {
            if (c is VisualInstance3D v) { hidden.Add((v, v.Layers)); v.Layers = SelfLayer; }
            HideSelf(c);
        }
    }

    void RestoreLayers()
    {
        foreach (var (node, layers) in hidden)
            if (IsInstanceValid(node)) node.Layers = layers;
        hidden.Clear();
    }

    void Refresh()
    {
        bool cam = ModeSelect.Selected == 0;
        camBox.Visible = cam;
        sensor.Visible = !cam && npc != null;
        placeholder.Visible = npc == null;
    }

    public override void _Process(double delta)
    {
        if (npc != null && !IsInstanceValid(npc)) { hidden.Clear(); npc = null; sensor.Npc = null; Refresh(); }
        bool cam = ModeSelect.Selected == 0;
        bool active = npc != null && cam && IsVisibleInTree();
        vp.RenderTargetUpdateMode = active ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (active)
            eyeCam.GlobalTransform = new Transform3D(npc.GlobalBasis, npc.GlobalPosition + npc.GlobalBasis * EyeOffset);
    }

    /// <summary>Diagram kupu-kupu: kiri = mata/antena kiri, kanan = kanan; panjang batang = intensitas input.</summary>
    sealed partial class SensorPanel : Control
    {
        public NpcBase Npc;

        public override void _Process(double delta)
        {
            if (Visible) QueueRedraw();
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.04f, 0.06f, 0.1f, 0.9f));
            if (Npc == null) return;
            var f = Npc.Frame;
            var b = Npc as BrainNpc;
            var font = ThemeDB.FallbackFont;
            float w = Size.X, h = Size.Y, cx = w / 2f;
            const float labelW = 52f, pad = 5f;
            float zone = cx - labelW / 2f - pad;
            float top = 16f, rowH = (h - top - 26f) / 6f;

            DrawString(font, new Vector2(pad, 11), b != null ? "◀ KIRI  (laju Poisson, Hz)" : "◀ KIRI  (nilai sensor 0..1)", HorizontalAlignment.Left, -1, 9, new Color(0.6f, 0.7f, 0.8f));
            DrawString(font, new Vector2(w - pad - 40, 11), "KANAN ▶", HorizontalAlignment.Right, 40, 9, new Color(0.6f, 0.7f, 0.8f));

            var t = b?.Traits ?? default;
            float sugarGain = b != null ? SensorEncoder.SugarHz * t.Appetite * Mathf.Clamp(0.15f + f.Hunger, 0f, 1.2f) : 1f;
            (string name, Color col, float l, float r, float refv)[] rows =
            {
                ("Looming", new Color(1f, 0.35f, 0.35f), Rate(b, f.LoomL, SensorEncoder.LoomHz * t.Fearfulness), Rate(b, f.LoomR, SensorEncoder.LoomHz * t.Fearfulness), b != null ? SensorEncoder.LoomHz : 1f),
                ("Gula", new Color(0.4f, 0.9f, 0.5f), Rate(b, f.SugarL, sugarGain), Rate(b, f.SugarR, sugarGain), b != null ? SensorEncoder.SugarHz : 1f),
                ("Pahit", new Color(0.75f, 0.5f, 1f), Rate(b, f.BitterL, SensorEncoder.BitterHz * t.Pickiness), Rate(b, f.BitterR, SensorEncoder.BitterHz * t.Pickiness), b != null ? SensorEncoder.BitterHz : 1f),
                ("Bau makan", new Color(0.55f, 0.95f, 0.35f), Odor(b, f.OdorFoodL, f.OdorFoodR), Odor(b, f.OdorFoodR, f.OdorFoodL), b != null ? SensorEncoder.OdorHz : 1f),
                ("Bau racun", new Color(1f, 0.45f, 0.75f), Odor(b, f.OdorPoisonL, f.OdorPoisonR), Odor(b, f.OdorPoisonR, f.OdorPoisonL), b != null ? SensorEncoder.OdorHz : 1f),
                ("Bau netral", new Color(0.55f, 0.75f, 0.95f), Odor(b, f.OdorNeutralL, f.OdorNeutralR), Odor(b, f.OdorNeutralR, f.OdorNeutralL), b != null ? SensorEncoder.OdorHz : 1f),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                var (name, col, l, r, refv) = rows[i];
                float y = top + i * rowH;
                float bh = Mathf.Max(4f, rowH - 4f);
                DrawRect(new Rect2(pad, y, zone, bh), new Color(1, 1, 1, 0.06f));
                DrawRect(new Rect2(cx + labelW / 2f, y, zone, bh), new Color(1, 1, 1, 0.06f));
                float lw = zone * Mathf.Clamp(l / refv, 0f, 1f), rw = zone * Mathf.Clamp(r / refv, 0f, 1f);
                DrawRect(new Rect2(pad + zone - lw, y, lw, bh), col);
                DrawRect(new Rect2(cx + labelW / 2f, y, rw, bh), col);
                DrawString(font, new Vector2(cx - labelW / 2f, y + bh - 1), name, HorizontalAlignment.Center, labelW, 9, new Color(0.85f, 0.9f, 0.95f));
                string fmt = b != null ? "F0" : "F2";
                DrawString(font, new Vector2(pad + 2, y + bh - 1), l.ToString(fmt), HorizontalAlignment.Left, zone, 9, new Color(0, 0, 0, 0.9f));
                DrawString(font, new Vector2(w - pad - 2 - zone, y + bh - 1), r.ToString(fmt), HorizontalAlignment.Right, zone, 9, new Color(0, 0, 0, 0.9f));
            }

            float fy = top + 6 * rowH + 3;
            DrawRect(new Rect2(pad, fy, w - 2 * pad, 5), new Color(1, 1, 1, 0.08f));
            DrawRect(new Rect2(pad, fy, (w - 2 * pad) * Mathf.Clamp(f.Hunger, 0f, 1f), 5), new Color(1f, 0.75f, 0.2f));
            string sun = $"Lapar {f.Hunger:F2} · Matahari {Mathf.RadToDeg(f.SunBearing):F0}° · Putar {f.YawRate:F1} rad/s";
            if (f.Reward > 0) sun += " · REWARD";
            if (f.Punish > 0) sun += " · SAKIT";
            DrawString(font, new Vector2(pad, fy + 17), sun, HorizontalAlignment.Left, w - 2 * pad, 9, new Color(0.8f, 0.85f, 0.9f));
        }

        static float Rate(BrainNpc b, float v, float gain) => b != null ? v * gain : v;
        // ORN memproyeksi ke dua lobus antena dengan bias ipsilateral (sama dengan SensorEncoder.SetOdor)
        static float Odor(BrainNpc b, float same, float other) =>
            b != null ? SensorEncoder.OdorHz * SensorEncoder.Pn(SensorEncoder.OdorIpsi * same + (1f - SensorEncoder.OdorIpsi) * other) : same;
    }
}
