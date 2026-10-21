using System.Collections.Generic;
using RetinaMath = OtakLalat.Brain.Core.Retina;
using Godot;
using OtakLalat.Brain.Core;
using OtakLalat.Brain;
using OtakLalat.Npc;
using OtakLalat.World;

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
    readonly CheckButton retinaToggle, floorToggle;
    FovFloorOverlay floorOverlay;
    readonly List<(VisualInstance3D node, uint layers)> hidden = new();
    NpcBase npc;

    public NpcEyeView()
    {
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 4);

        ModeSelect = new OptionButton { FocusMode = FocusModeEnum.None, FitToLongestItem = true };
        ModeSelect.AddItem("📷 Kamera NPC");
        ModeSelect.AddItem("🧠 Input Otak");
        ModeSelect.AddItem("🔬 Retina (sektor)");
        ModeSelect.AddItem("🧭 Retina top-down");
        ModeSelect.AddThemeFontSizeOverride("font_size", 10);
        ModeSelect.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // ubah jalur penglihatan NPC Otak terpilih secara langsung (tanpa spawn ulang); selalu terlihat, aktif bila NPC Otak dipilih
        retinaToggle = new CheckButton { Text = "Retina", FocusMode = FocusModeEnum.None, Disabled = true, TooltipText = "Pakai penglihatan retinotopik (8 sektor) untuk NPC Otak terpilih" };
        retinaToggle.AddThemeFontSizeOverride("font_size", 10);
        retinaToggle.Toggled += on =>
        {
            if (npc is BrainNpc) npc.Vision = on ? VisionMode.Retinotopic : VisionMode.Scalar;
        };
        var modeRow = new HBoxContainer();
        modeRow.AddThemeConstantOverride("separation", 4);
        modeRow.AddChild(ModeSelect);
        modeRow.AddChild(retinaToggle);
        AddChild(modeRow);

        // overlay baji sektor di lantai dunia 3D untuk NPC terpilih (opsional, hanya di mode top-down)
        floorToggle = new CheckButton { Text = "Baji sektor di lantai 3D", FocusMode = FocusModeEnum.None, Visible = false };
        floorToggle.AddThemeFontSizeOverride("font_size", 10);
        floorToggle.Toggled += on => { if (floorOverlay != null) floorOverlay.Visible = on; };
        AddChild(floorToggle);

        var stack = new Control { CustomMinimumSize = new Vector2(0, 120), SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
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
    public void ShareWorld(SubViewport worldViewport)
    {
        vp.World3D = worldViewport.FindWorld3D();
        if (floorOverlay == null)
        {
            floorOverlay = new FovFloorOverlay { Visible = floorToggle.ButtonPressed };
            worldViewport.AddChild(floorOverlay);
        }
    }

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
        sensor.Retina = ModeSelect.Selected == 2;
        sensor.TopDown = ModeSelect.Selected == 3;
        floorToggle.Visible = ModeSelect.Selected == 3;
        placeholder.Visible = npc == null;
        retinaToggle.Disabled = npc is not BrainNpc;
        if (npc != null) retinaToggle.SetPressedNoSignal(npc.Vision == VisionMode.Retinotopic);
    }

    public override void _Process(double delta)
    {
        if (npc != null && !IsInstanceValid(npc)) { hidden.Clear(); npc = null; sensor.Npc = null; Refresh(); }
        floorOverlay?.Update(npc);
        bool cam = ModeSelect.Selected == 0;
        bool active = npc != null && cam && IsVisibleInTree();
        vp.RenderTargetUpdateMode = active ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (active)
            eyeCam.GlobalTransform = new Transform3D(npc.GlobalBasis, npc.GlobalPosition + npc.GlobalBasis * EyeOffset);
    }

    /// <summary>Baji sektor retina NPC terpilih di lantai dunia 3D (dikeluarkan dari kamera mata lewat SelfLayer).</summary>
    sealed partial class FovFloorOverlay : Node3D
    {
        const float Radius = 3.5f, Y = 0.07f;
        readonly ImmediateMesh mesh = new();

        public FovFloorOverlay()
        {
            AddChild(new MeshInstance3D
            {
                Mesh = mesh, Layers = SelfLayer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            });
        }

        public void Update(NpcBase npc)
        {
            mesh.ClearSurfaces();
            if (!Visible || npc == null || !IsInstanceValid(npc)) return;
            int k = BrainAssets.Connectome.RetinaSectors > 0 ? BrainAssets.Connectome.RetinaSectors : 8;
            bool brain = npc is BrainNpc;
            float fov = Mathf.DegToRad(brain ? npc.FovDeg : 360f);
            float limit = Retina.EyeLimit(fov), sw = Retina.SectorWidth(k, fov);
            var pos = npc.GlobalPosition + new Vector3(0, Y, 0);
            var fwd = -npc.GlobalBasis.Z; var lft = -npc.GlobalBasis.X;
            Vector3 P(float bearing, float r) => pos + (fwd * Mathf.Cos(bearing) + lft * Mathf.Sin(bearing)) * r;

            mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
            void Wedge(float a0, float a1, Color col)
            {
                const int steps = 5;
                for (int s = 0; s < steps; s++)
                {
                    float x0 = a0 + (a1 - a0) * s / steps, x1 = a0 + (a1 - a0) * (s + 1) / steps;
                    mesh.SurfaceSetColor(col); mesh.SurfaceAddVertex(pos);
                    mesh.SurfaceSetColor(col); mesh.SurfaceAddVertex(P(x0, Radius));
                    mesh.SurfaceSetColor(col); mesh.SurfaceAddVertex(P(x1, Radius));
                }
            }
            if (limit < Mathf.Pi - 0.01f) Wedge(limit, Mathf.Tau - limit, new Color(0.6f, 0.6f, 0.6f, 0.22f));
            for (int eye = 0; eye < 2; eye++)
                for (int i = 0; i < k; i++)
                {
                    bool left = eye == 0;
                    float c0 = Retina.SectorCenter(i, k, fov) - sw / 2f, c1 = c0 + sw;
                    if (!left) (c0, c1) = (-c1, -c0);
                    float lv = brain ? SensorPanel.Level(npc, left, i) : 0f;
                    Wedge(c0, c1, SensorPanel.EyeColor(left, lv) with { A = 0.18f + 0.55f * lv });   // lebih pekat dari 2D agar terlihat di atas rumput
                }
            mesh.SurfaceEnd();
        }
    }

    /// <summary>Diagram kupu-kupu: kiri = mata/antena kiri, kanan = kanan; panjang batang = intensitas input.</summary>
    sealed partial class SensorPanel : Control
    {
        public NpcBase Npc;
        public bool Retina, TopDown;

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
            if (TopDown) { DrawTopDown(b, font); return; }
            if (Retina) { DrawRetina(f, b, font); return; }
            float w = Size.X, h = Size.Y, cx = w / 2f;
            const float labelW = 52f, pad = 5f;
            float zone = cx - labelW / 2f - pad;
            float top = 16f, rowH = (h - top - 26f) / 6f;

            DrawString(font, new Vector2(pad, 11), b != null ? "◀ KIRI  (laju Poisson, Hz)" : "◀ KIRI  (nilai sensor 0..1)", HorizontalAlignment.Left, -1, 9, new Color(0.6f, 0.7f, 0.8f));
            DrawString(font, new Vector2(w - pad - 40, 11), "KANAN ▶", HorizontalAlignment.Right, 40, 9, new Color(0.6f, 0.7f, 0.8f));

            var t = b?.Traits ?? default;
            float loomL = f.LoomL, loomR = f.LoomR;
            if (f.LoomBinsL != null && f.LoomBinsR != null)
            {
                loomL = 0f; loomR = 0f;
                foreach (var x in f.LoomBinsL) loomL = Mathf.Max(loomL, x);
                foreach (var x in f.LoomBinsR) loomR = Mathf.Max(loomR, x);
            }
            float sugarGain = b != null ? SensorEncoder.SugarHz * t.Appetite * Mathf.Clamp(0.15f + f.Hunger, 0f, 1.2f) : 1f;
            (string name, Color col, float l, float r, float refv)[] rows =
            {
                ("Looming", new Color(1f, 0.35f, 0.35f), Rate(b, loomL, SensorEncoder.LoomHz * t.Fearfulness), Rate(b, loomR, SensorEncoder.LoomHz * t.Fearfulness), b != null ? SensorEncoder.LoomHz : 1f),
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

        /// <summary>Tingkat aktivasi sektor 0..1 (bin dikali Fearfulness); 0 bila NPC tidak memakai retina.</summary>
        internal static float Level(NpcBase n, bool leftEye, int i)
        {
            var bins = leftEye ? n.Frame.LoomBinsL : n.Frame.LoomBinsR;
            if (n is not BrainNpc b || bins == null || i >= bins.Length) return 0f;
            return Mathf.Clamp(bins[i] * b.Traits.Fearfulness, 0f, 1f);
        }

        internal static Color EyeColor(bool leftEye, float level) =>
            (leftEye ? new Color(0.3f, 0.8f, 1f) : new Color(1f, 0.65f, 0.25f)) with { A = 0.10f + 0.75f * level };

        static Vector2 Polar(Vector2 c, float bearing, float r) => c + new Vector2(-System.MathF.Sin(bearing), -System.MathF.Cos(bearing)) * r;

        void DrawTopDown(BrainNpc b, Font font)
        {
            float w = Size.X, h = Size.Y;
            var c = new Vector2(w / 2f, h / 2f + 4f);
            float R = Mathf.Max(20f, Mathf.Min(w / 2f - 6f, h / 2f - 12f));
            const float rangeM = 10f;
            int k = BrainAssets.Connectome.RetinaSectors > 0 ? BrainAssets.Connectome.RetinaSectors : 8;
            float fovDeg = b != null ? Npc.FovDeg : 360f;
            float fov = Mathf.DegToRad(fovDeg);
            float limit = RetinaMath.EyeLimit(fov);

            // cincin jarak 5 m dan 10 m
            foreach (float m in new[] { 5f, 10f })
                DrawArc(c, R * m / rangeM, 0f, Mathf.Tau, 48, new Color(1, 1, 1, 0.10f), 1f);
            DrawString(font, c + new Vector2(R * 0.5f + 2, 10), "5m", HorizontalAlignment.Left, -1, 8, new Color(1, 1, 1, 0.35f));

            // titik buta: abu-abu dengan batas putus-putus
            if (limit < Mathf.Pi - 0.01f)
            {
                float span = Mathf.Tau - 2f * limit;
                var pts = new List<Vector2> { c };
                for (int s = 0; s <= 24; s++) pts.Add(Polar(c, limit + span * s / 24f, R));
                DrawColoredPolygon(pts.ToArray(), new Color(0.6f, 0.6f, 0.6f, 0.14f));
                var gray = new Color(0.75f, 0.75f, 0.75f, 0.7f);
                DrawDashedLine(c, Polar(c, limit, R), gray, 1f, 4f);
                DrawDashedLine(c, Polar(c, -limit, R), gray, 1f, 4f);
                for (int s = 0; s < 24; s += 2)
                    DrawLine(Polar(c, limit + span * s / 24f, R), Polar(c, limit + span * (s + 1) / 24f, R), gray, 1f);
                DrawString(font, Polar(c, Mathf.Pi, R * 0.55f) + new Vector2(-30, 0), $"titik buta {360f - fovDeg:F0}°", HorizontalAlignment.Center, 60, 9, new Color(0.8f, 0.8f, 0.8f));
            }

            // sektor retina kiri/kanan (baji), warna mengikuti tingkat aktivasi
            float sw = RetinaMath.SectorWidth(k, fov);
            for (int eye = 0; eye < 2; eye++)
            {
                bool left = eye == 0;
                for (int i = 0; i < k; i++)
                {
                    float c0 = RetinaMath.SectorCenter(i, k, fov) - sw / 2f, c1 = c0 + sw;
                    if (!left) (c0, c1) = (-c1, -c0);
                    var arc = new List<Vector2>();
                    for (int s = 0; s <= 6; s++) arc.Add(Polar(c, c0 + (c1 - c0) * s / 6f, R));
                    var poly = new List<Vector2> { c };
                    poly.AddRange(arc);
                    float lv = b != null ? Level(Npc, left, i) : 0f;
                    var col = EyeColor(left, lv);
                    DrawColoredPolygon(poly.ToArray(), col);
                    var edge = col with { A = 0.5f };
                    DrawLine(c, arc[0], edge, 1f); DrawLine(c, arc[^1], edge, 1f);
                    DrawPolyline(arc.ToArray(), edge, 1f);
                    if (b != null && lv > 0.02f)
                    {
                        var bins = left ? Npc.Frame.LoomBinsL : Npc.Frame.LoomBinsR;
                        float hz = SensorEncoder.LoomHz * RetinaMath.Gain * b.Traits.Fearfulness * bins[i];
                        DrawString(font, Polar(c, (c0 + c1) / 2f, R * 0.78f) + new Vector2(-12, 3), hz.ToString("F0"), HorizontalAlignment.Center, 24, 8, Colors.White);
                    }
                }
            }

            // NPC di tengah menghadap ke atas
            var tri = new[] { c + new Vector2(0, -9), c + new Vector2(-6, 6), c + new Vector2(6, 6) };
            DrawColoredPolygon(tri, Npc.BodyColor);
            DrawPolyline(new[] { tri[0], tri[1], tri[2], tri[0] }, Colors.White, 1f);

            // benda looming (Player, drone) pada posisi relatif terhadap NPC
            var fwd = -Npc.GlobalBasis.Z; var lft = -Npc.GlobalBasis.X;
            foreach (var l in WorldRegistry.Loomers)
            {
                var d = l.LoomPosition - Npc.GlobalPosition;
                var rel = new Vector2(-d.Dot(lft), -d.Dot(fwd)) * (R / rangeM);
                bool outside = rel.Length() > R;
                if (outside) rel = rel.Normalized() * R;
                float rad = Mathf.Max(4f, l.LoomRadius * R / rangeM);
                var colL = l is Player ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.4f, 0.35f);
                if (outside) DrawArc(c + rel, rad, 0f, Mathf.Tau, 16, colL, 1.5f);
                else DrawCircle(c + rel, rad, colL with { A = 0.85f });
                DrawString(font, c + rel + new Vector2(rad + 2, 3), l is Player ? "KAMU" : "Drone", HorizontalAlignment.Left, -1, 9, colL);
            }

            string note = b == null ? "FSM: tanpa retina (hanya posisi benda)"
                : Npc.Vision == VisionMode.Retinotopic ? $"{Npc.TypeName}: FOV {fovDeg:F0}°, {k} sektor/mata"
                : $"{Npc.TypeName}: FOV {fovDeg:F0}° (penglihatan Skalar: sektor tidak aktif)";
            DrawString(font, new Vector2(5, 11), note, HorizontalAlignment.Left, w - 10, 9, new Color(0.7f, 0.8f, 0.9f));
        }

        void DrawRetina(in SensorFrame f, BrainNpc b, Font font)
        {
            float w = Size.X, h = Size.Y, cx = w / 2f;
            if (b == null || f.LoomBinsL == null || f.LoomBinsR == null)
            {
                string msg = b == null ? "NPC FSM: tanpa retina (membaca looming skalar L/R)" : "NPC ini memakai penglihatan Skalar. Nyalakan saklar 'Retina' di samping dropdown, atau spawn dengan 'Otak·retina'.";
                DrawMultilineString(font, new Vector2(6, h / 2 - 8), msg, HorizontalAlignment.Center, w - 12, 10, -1, new Color(0.75f, 0.8f, 0.85f));
                return;
            }
            int k = f.LoomBinsL.Length;
            const float labelW = 40f, pad = 5f;
            float zone = cx - labelW / 2f - pad, top = 16f, rowH = (h - top - 14f) / k;
            var t = b.Traits;
            DrawString(font, new Vector2(pad, 11), "◀ MATA KIRI (Hz)", HorizontalAlignment.Left, -1, 9, new Color(0.6f, 0.7f, 0.8f));
            DrawString(font, new Vector2(w - pad - 70, 11), "MATA KANAN ▶", HorizontalAlignment.Right, 70, 9, new Color(0.6f, 0.7f, 0.8f));
            for (int i = 0; i < k; i++)
            {
                float y = top + i * rowH, bh = Mathf.Max(4f, rowH - 3f);
                float l = SensorEncoder.LoomHz * t.Fearfulness * f.LoomBinsL[i], r = SensorEncoder.LoomHz * t.Fearfulness * f.LoomBinsR[i];
                DrawRect(new Rect2(pad, y, zone, bh), new Color(1, 1, 1, 0.06f));
                DrawRect(new Rect2(cx + labelW / 2f, y, zone, bh), new Color(1, 1, 1, 0.06f));
                float lw = zone * Mathf.Clamp(l / SensorEncoder.LoomHz, 0f, 1f), rw = zone * Mathf.Clamp(r / SensorEncoder.LoomHz, 0f, 1f);
                var col = new Color(1f, 0.35f + 0.03f * i, 0.35f);
                DrawRect(new Rect2(pad + zone - lw, y, lw, bh), col);
                DrawRect(new Rect2(cx + labelW / 2f, y, rw, bh), col);
                string name = i == 0 ? "S0 dpn" : i == k - 1 ? $"S{i} blk" : $"S{i}";
                DrawString(font, new Vector2(cx - labelW / 2f, y + bh - 1), name, HorizontalAlignment.Center, labelW, 9, new Color(0.85f, 0.9f, 0.95f));
                DrawString(font, new Vector2(pad + 2, y + bh - 1), l.ToString("F0"), HorizontalAlignment.Left, zone, 9, new Color(0, 0, 0, 0.9f));
                DrawString(font, new Vector2(w - pad - 2 - zone, y + bh - 1), r.ToString("F0"), HorizontalAlignment.Right, zone, 9, new Color(0, 0, 0, 0.9f));
            }
        }

        static float Rate(BrainNpc b, float v, float gain) => b != null ? v * gain : v;
        // ORN memproyeksi ke dua lobus antena dengan bias ipsilateral (sama dengan SensorEncoder.SetOdor)
        static float Odor(BrainNpc b, float same, float other) =>
            b != null ? SensorEncoder.OdorHz * SensorEncoder.Pn(SensorEncoder.OdorIpsi * same + (1f - SensorEncoder.OdorIpsi) * other) : same;
    }
}
