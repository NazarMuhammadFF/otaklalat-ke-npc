using System.Collections.Generic;
using System.Linq;
using Godot;
using OtakLalat.Brain.Core;
using OtakLalat.Npc;
using OtakLalat.World;

namespace OtakLalat.UI;

/// <summary>
/// Overlay evaluasi interaktif berbasis Bento Grid:
/// - Kolom Kiri: Map & Navigasi Kamera, Spawner Populasi, Skenario Uji, Papan Skor Komparatif.
/// - Kolom Kanan: Inspeksi Entitas Terpilih, 6 Knob Sifat Biologis (Live Sliders), Spike Raster (2.635 LIF).
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    MapBase arena;
    Control hudRoot;
    VBoxContainer leftBento, rightBento;
    Label info, statsLabel, detail;
    PanelContainer cardScenarios, cardInspect, cardTraits, cardRaster;
    readonly List<HSlider> sliders = new();
    readonly List<Label> sliderValLabels = new();
    SpikeRaster raster;
    NpcBase selected;
    bool labels = true;
    bool clickSpawn;
    OptionButton typeOpt, methodOpt;
    static readonly string[] TraitNames = { "Fearfulness", "Appetite", "Pickiness", "Curiosity", "LearningRate", "NeuralNoise" };

    public DebugOverlay() { }
    public DebugOverlay(MapBase arena) => this.arena = arena;

    public override void _Ready()
    {
        hudRoot = new Control { Name = "HudRoot", MouseFilter = Control.MouseFilterEnum.Ignore };
        hudRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(hudRoot);

        // =========================================================================
        // KOLOM BENTO KIRI: Manajemen Skenario & Pemantauan Global (Lebar 380px)
        // =========================================================================
        leftBento = new VBoxContainer
        {
            Name = "LeftBento",
            OffsetLeft = 14f,
            OffsetTop = 14f,
            OffsetRight = 394f,
            CustomMinimumSize = new Vector2(380, 0),
        };
        leftBento.AddThemeConstantOverride("separation", 10);
        hudRoot.AddChild(leftBento);

        // --- Card 1: 🗺️ Map & Kamera ---
        var cardMap = CreateBentoCard("🗺️ MAP & KAMERA", new Color(0.22f, 0.74f, 0.97f), out var mapBox);
        info = new Label();
        info.AddThemeColorOverride("font_color", new Color(0.92f, 0.96f, 1f));
        mapBox.AddChild(info);

        var camRow = new HBoxContainer();
        AddBtn(camRow, "F1: FPS", () => arena.PlayerNode?.SetCameraMode(Player.CamMode.FirstPerson));
        AddBtn(camRow, "F2: Atas", () => arena.PlayerNode?.SetCameraMode(Player.CamMode.TopDown));
        AddBtn(camRow, "F3: Ikuti", () =>
        {
            if (selected == null) Select(NpcBase.All.OfType<BrainNpc>().FirstOrDefault() ?? NpcBase.All.FirstOrDefault());
            arena.PlayerNode?.SetCameraMode(Player.CamMode.FollowNpc, selected);
        });
        AddBtn(camRow, "⛶ F11", ToggleFullscreen);
        mapBox.AddChild(camRow);

        string current = arena.SceneFilePath;
        var mapRow = new HBoxContainer();
        for (int i = 0; i < MapRegistry.Maps.Length; i++)
        {
            var (title, path) = MapRegistry.Maps[i];
            string shortTitle = i switch
            {
                0 => "Makan",
                1 => "Bahaya",
                2 => "Predator",
                3 => "Hutan",
                _ => title
            };
            AddBtn(mapRow, $"{i + 1}:{shortTitle}", () => GoToMap(path), disabled: path == current);
        }
        mapBox.AddChild(mapRow);
        leftBento.AddChild(cardMap);

        // --- Card 2: 🐾 Spawner Populasi ---
        var cardSpawn = CreateBentoCard("🐾 SPAWNER POPULASI", new Color(0.20f, 0.83f, 0.60f), out var spawnBox);
        var spawnRow = new HBoxContainer();
        typeOpt = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (var t in MapBase.TypeIds) typeOpt.AddItem(t);
        methodOpt = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        methodOpt.AddItem("Otak"); methodOpt.AddItem("FSM");
        spawnRow.AddChild(new Label { Text = "Tipe:" });
        spawnRow.AddChild(typeOpt);
        spawnRow.AddChild(methodOpt);
        AddBtn(spawnRow, "+ Spawn", SpawnAtScreenCenter);
        spawnBox.AddChild(spawnRow);

        var spawnActionRow = new HBoxContainer();
        var clickChk = new CheckButton { Text = "Klik lantai spawn", FocusMode = Control.FocusModeEnum.None };
        clickChk.Toggled += on => clickSpawn = on;
        spawnActionRow.AddChild(clickChk);
        AddBtn(spawnActionRow, "🗑️ Hapus Semua", () => { Select(null); arena.ClearNpcs(); });
        spawnBox.AddChild(spawnActionRow);
        leftBento.AddChild(cardSpawn);

        // --- Card 3: 🎯 Skenario Uji Khusus ---
        cardScenarios = CreateBentoCard("🎯 SKENARIO UJI KHUSUS", new Color(0.98f, 0.57f, 0.24f), out var scenBox);
        var scenRow = new HBoxContainer();
        if (arena.Stats.HasFlag(MapStats.Escape)) AddBtn(scenRow, "🦅 Looming", TriggerLoomingTest);
        if (arena.Stats.HasFlag(MapStats.Food) || arena.Stats.HasFlag(MapStats.Poison)) AddBtn(scenRow, "🍎 Makan vs Racun", TriggerFeedingTest);
        if (arena.Stats.HasFlag(MapStats.Danger)) AddBtn(scenRow, "⚡ Belajar Bahaya", TriggerDangerLearningTest);
        if (scenRow.GetChildCount() > 0)
        {
            scenBox.AddChild(scenRow);
            leftBento.AddChild(cardScenarios);
        }

        // --- Card 4: 📊 Skor Komparatif (Otak vs FSM) ---
        var cardStats = CreateBentoCard("📊 SKOR KOMPARATIF REAL-TIME", new Color(0.51f, 0.55f, 0.97f), out var statsBox);
        statsLabel = new Label();
        statsLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.95f, 1f));
        statsBox.AddChild(statsLabel);
        leftBento.AddChild(cardStats);

        // --- Baris Bantuan Bawah Singkat ---
        var helpPill = new PanelContainer();
        var helpStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.09f, 0.80f),
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        helpPill.AddThemeStyleboxOverride("panel", helpStyle);
        var helpLabel = new Label
        {
            Text = "💡 [H] Sembunyikan HUD | [Tab] Nama | [Esc] Batal Pilih",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        helpLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 0.9f, 0.85f));
        helpPill.AddChild(helpLabel);
        leftBento.AddChild(helpPill);

        // =========================================================================
        // KOLOM BENTO KANAN: Inspeksi Entitas & Sirkuit Saraf Terpilih (Lebar 380px)
        // =========================================================================
        rightBento = new VBoxContainer
        {
            Name = "RightBento",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -394f,
            OffsetTop = 14f,
            OffsetRight = -14f,
            CustomMinimumSize = new Vector2(380, 0),
            Visible = false,
        };
        rightBento.AddThemeConstantOverride("separation", 10);
        hudRoot.AddChild(rightBento);

        // --- Card 5: 🔬 Status & Motorik Terurai ---
        cardInspect = CreateBentoCard("🔬 INSPEKSI ENTITAS", new Color(0.18f, 0.83f, 0.75f), out var inspectBox);
        detail = new Label { AutowrapMode = TextServer.AutowrapMode.Arbitrary, CustomMinimumSize = new Vector2(350, 0) };
        detail.AddThemeColorOverride("font_color", new Color(0.92f, 0.96f, 1f));
        inspectBox.AddChild(detail);
        rightBento.AddChild(cardInspect);

        // --- Card 6: 🧬 6 Knob Sifat Biologis ---
        cardTraits = CreateBentoCard("🧬 6 KNOB SIFAT BIOLOGIS", new Color(0.96f, 0.62f, 0.04f), out var traitBox);
        for (int i = 0; i < TraitNames.Length; i++)
        {
            int k = i;
            var headerRow = new HBoxContainer();
            var nameLbl = new Label { Text = TraitNames[i], SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var valLbl = new Label { Text = "1.00" };
            valLbl.AddThemeColorOverride("font_color", new Color(0.96f, 0.85f, 0.4f));
            headerRow.AddChild(nameLbl);
            headerRow.AddChild(valLbl);
            sliderValLabels.Add(valLbl);
            traitBox.AddChild(headerRow);

            var s = new HSlider { MinValue = 0, MaxValue = 2, Step = 0.05, CustomMinimumSize = new Vector2(350, 16) };
            s.ValueChanged += v =>
            {
                SetTrait(k, (float)v);
                valLbl.Text = v.ToString("F2");
            };
            sliders.Add(s);
            traitBox.AddChild(s);
        }
        rightBento.AddChild(cardTraits);

        // --- Card 7: ⚡ Raster Spike Neuron ---
        cardRaster = CreateBentoCard("⚡ SPIKE RASTER (2.635 LIF)", new Color(0.96f, 0.25f, 0.37f), out var rasterBox);
        var legendLbl = new Label
        {
            Text = "Cyan: Sensorik | Kuning: KC/MBON | Merah: Motorik",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        legendLbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.85f, 0.9f));
        legendLbl.AddThemeFontSizeOverride("font_size", 12);
        rasterBox.AddChild(legendLbl);
        raster = new SpikeRaster { CustomMinimumSize = new Vector2(350, 180) };
        rasterBox.AddChild(raster);
        rightBento.AddChild(cardRaster);
    }

    PanelContainer CreateBentoCard(string title, Color accentColor, out VBoxContainer contentBox)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.08f, 0.12f, 0.88f),
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            BorderColor = accentColor,
            ContentMarginLeft = 12,
            ContentMarginTop = 8,
            ContentMarginRight = 12,
            ContentMarginBottom = 10,
            ShadowColor = new Color(0, 0, 0, 0.4f),
            ShadowSize = 5,
        };
        panel.AddThemeStyleboxOverride("panel", style);

        var mainBox = new VBoxContainer();
        mainBox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(mainBox);

        // Header bar dengan Title & Tombol Minimalkan
        var header = new HBoxContainer();
        var titleLbl = new Label
        {
            Text = title,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        titleLbl.AddThemeColorOverride("font_color", accentColor);
        header.AddChild(titleLbl);

        contentBox = new VBoxContainer();
        contentBox.AddThemeConstantOverride("separation", 6);

        var collapseBtn = new Button
        {
            Text = "−",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(24, 20),
        };
        var capturedContentBox = contentBox;
        collapseBtn.Pressed += () =>
        {
            capturedContentBox.Visible = !capturedContentBox.Visible;
            collapseBtn.Text = capturedContentBox.Visible ? "−" : "+";
        };
        header.AddChild(collapseBtn);

        mainBox.AddChild(header);
        mainBox.AddChild(contentBox);

        return panel;
    }

    static void AddBtn(Container parent, string text, System.Action onClick, bool disabled = false)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, Disabled = disabled };
        b.Pressed += onClick;
        parent.AddChild(b);
    }

    void GoToMap(string path) => GetTree().ChangeSceneToFile(path);

    bool GroundPoint(Vector2 screenPos, out Vector3 point)
    {
        point = default;
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return false;
        var hit = new Plane(Vector3.Up, 0f).IntersectsRay(cam.ProjectRayOrigin(screenPos), cam.ProjectRayNormal(screenPos));
        if (hit == null) return false;
        point = hit.Value;
        return true;
    }

    void SpawnNpcAt(Vector3 p)
    {
        var n = arena.SpawnNpc(typeOpt.GetItemText(typeOpt.Selected), methodOpt.Selected == 0, p);
        Select(n);
    }

    void SpawnAtScreenCenter()
    {
        var center = GetViewport().GetVisibleRect().Size / 2f;
        SpawnNpcAt(GroundPoint(center, out var p) ? p : arena.RandomSpot());
    }

    NpcBase EnsureTargetNpc(string preferredType = null)
    {
        if (selected != null && IsInstanceValid(selected)) return selected;
        NpcBase target = null;
        if (preferredType != null)
            target = NpcBase.All.OfType<BrainNpc>().FirstOrDefault(n => n.TypeName.ToLowerInvariant() == preferredType);
        target ??= NpcBase.All.OfType<BrainNpc>().FirstOrDefault() ?? NpcBase.All.FirstOrDefault();
        if (target != null) Select(target);
        return target;
    }

    void TriggerLoomingTest()
    {
        var t = EnsureTargetNpc("rusa");
        if (t == null) return;
        arena.Predator?.DiveAt(t);
    }

    void TriggerFeedingTest()
    {
        var t = EnsureTargetNpc("babi");
        if (t == null) return;
        t.Hunger = 0.9f;
        var fwd = -t.GlobalBasis.Z;
        var side = t.GlobalBasis.X;
        arena.SpawnEdible(new Vector3(t.GlobalPosition.X, 0, t.GlobalPosition.Z) + fwd * 2.2f - side * 1.1f, false);
        arena.SpawnEdible(new Vector3(t.GlobalPosition.X, 0, t.GlobalPosition.Z) + fwd * 2.2f + side * 1.1f, true);
    }

    void TriggerDangerLearningTest()
    {
        var t = EnsureTargetNpc("rusa");
        var dz = WorldRegistry.Dangers.FirstOrDefault();
        if (t == null || dz == null) return;
        var startPos = dz.GlobalPosition + new Vector3(0, 0.1f, dz.Radius + 1.2f);
        t.GlobalPosition = startPos;
        t.LookAt(new Vector3(dz.GlobalPosition.X, startPos.Y, dz.GlobalPosition.Z), Vector3.Up);
    }

    void SetTrait(int k, float v)
    {
        if (selected is not BrainNpc b) return;
        var t = b.Traits;
        switch (k)
        {
            case 0: t.Fearfulness = v; break;
            case 1: t.Appetite = v; break;
            case 2: t.Pickiness = v; break;
            case 3: t.Curiosity = v; break;
            case 4: t.LearningRate = v; break;
            default: t.NeuralNoise = v; break;
        }
        b.Traits = t;
    }

    void Select(NpcBase n)
    {
        if (selected is BrainNpc old) old.Brain.CaptureCounts = null;
        selected = n;
        if (arena.PlayerNode != null && n != null) arena.PlayerNode.FollowTarget = n;

        if (n == null)
        {
            rightBento.Visible = false;
            return;
        }

        rightBento.Visible = true;
        bool isBrain = n is BrainNpc;
        cardTraits.Visible = isBrain;
        cardRaster.Visible = isBrain;

        if (n is BrainNpc b)
        {
            b.Brain.CaptureCounts = new int[b.Brain.State.C.N];
            raster.Bind(b.Brain);
            var t = b.Traits;
            float[] vals = { t.Fearfulness, t.Appetite, t.Pickiness, t.Curiosity, t.LearningRate, t.NeuralNoise };
            for (int i = 0; i < vals.Length; i++)
            {
                sliders[i].SetValueNoSignal(vals[i]);
                sliderValLabels[i].Text = vals[i].ToString("F2");
            }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (clickSpawn)
            {
                if (GroundPoint(mb.Position, out var gp)) SpawnNpcAt(gp);
                return;
            }
            var cam = GetViewport().GetCamera3D();
            if (cam == null) return;
            var from = cam.ProjectRayOrigin(mb.Position);
            var q = PhysicsRayQueryParameters3D.Create(from, from + cam.ProjectRayNormal(mb.Position) * 200f, collisionMask: 2);
            var hit = cam.GetWorld3D().DirectSpaceState.IntersectRay(q);
            Select(hit.Count > 0 ? hit["collider"].As<Node>() as NpcBase : null);
        }
        else if (e is InputEventKey { Pressed: true } k)
        {
            int idx = (int)(k.Keycode - Key.Key1);
            if (k.CtrlPressed && idx >= 0 && idx < MapRegistry.Maps.Length) GoToMap(MapRegistry.Maps[idx].Path);
            else if (k.Keycode == Key.H) hudRoot.Visible = !hudRoot.Visible;
            else if (k.Keycode == Key.L) arena.Ai.LodEnabled = !arena.Ai.LodEnabled;
            else if (k.Keycode == Key.Tab)
            {
                labels = !labels;
                foreach (var n in NpcBase.All) n.ShowLabel = labels;
                foreach (var z in arena.ZoneLabels) z.Visible = labels;
            }
            else if (k.Keycode == Key.F11 || (k.AltPressed && k.Keycode == Key.Enter)) ToggleFullscreen();
            else if (k.Keycode == Key.Escape) Select(null);
        }
    }

    void ToggleFullscreen()
    {
        var m = DisplayServer.WindowGetMode();
        bool isFull = m == DisplayServer.WindowMode.Fullscreen || m == DisplayServer.WindowMode.ExclusiveFullscreen;
        DisplayServer.WindowSetMode(isFull ? DisplayServer.WindowMode.Maximized : DisplayServer.WindowMode.Fullscreen);
    }

    public override void _Process(double delta)
    {
        if (!hudRoot.Visible) return;

        var brainList = NpcBase.All.OfType<BrainNpc>().ToList();
        var fsmList = NpcBase.All.OfType<BaselineNpc>().ToList();
        string camName = arena.PlayerNode?.Mode switch
        {
            Player.CamMode.FirstPerson => "FPS (F1)",
            Player.CamMode.FollowNpc => "Follow (F3)",
            _ => "Top-Down (F2)",
        };
        info.Text = $"FPS: {Engine.GetFramesPerSecond()}  |  Kamera: {camName}  |  LOD: {(arena.Ai.LodEnabled ? "ON" : "OFF")}\n" +
                    $"AI Tick: {arena.Ai.LastDecideWallMs:F2} ms (CPU {arena.Ai.LastDecideCpuMs:F2} ms)  |  Map: {arena.MapTitle}";

        // Ringkasan Statistik Komparatif Real-Time (Otak Connectome vs FSM)
        float bFood = brainList.Count > 0 ? brainList.Average(x => x.Metrics.FoodSeconds) : 0f;
        float fFood = fsmList.Count > 0 ? fsmList.Average(x => x.Metrics.FoodSeconds) : 0f;
        float bEsc = brainList.Count > 0 ? (float)brainList.Average(x => x.Metrics.EscapeEvents) : 0f;
        float fEsc = fsmList.Count > 0 ? (float)fsmList.Average(x => x.Metrics.EscapeEvents) : 0f;
        float bPois = brainList.Count > 0 ? (float)brainList.Average(x => x.Metrics.PoisonBites) : 0f;
        float fPois = fsmList.Count > 0 ? (float)fsmList.Average(x => x.Metrics.PoisonBites) : 0f;
        float bDang = brainList.Count > 0 ? brainList.Average(x => x.Metrics.TimeInDanger) : 0f;
        float fDang = fsmList.Count > 0 ? fsmList.Average(x => x.Metrics.TimeInDanger) : 0f;
        float bDist = brainList.Count > 0 ? brainList.Average(x => x.Metrics.Distance) : 0f;
        float fDist = fsmList.Count > 0 ? fsmList.Average(x => x.Metrics.Distance) : 0f;

        var st = arena.Stats;
        var lines = new List<string> { $"Populasi: 🧠 Otak ({brainList.Count}) vs ⚙️ FSM ({fsmList.Count})" };
        if (st.HasFlag(MapStats.Food)) lines.Add($"🍎 Makan Manis : 🧠 {bFood:F1}s  vs  ⚙️ {fFood:F1}s");
        if (st.HasFlag(MapStats.Poison)) lines.Add($"🤢 Gigit Racun : 🧠 {bPois:F0}x  vs  ⚙️ {fPois:F0}x");
        if (st.HasFlag(MapStats.Escape)) lines.Add($"🦅 Kabur Looming: 🧠 {bEsc:F0}x  vs  ⚙️ {fEsc:F0}x");
        if (st.HasFlag(MapStats.Danger)) lines.Add($"⚡ Zona Bahaya : 🧠 {bDang:F1}s  vs  ⚙️ {fDang:F1}s");
        if (st.HasFlag(MapStats.Explore)) lines.Add($"🧭 Jarak Tempuh : 🧠 {bDist:F0}m  vs  ⚙️ {fDist:F0}m");
        statsLabel.Text = string.Join("\n", lines);

        if (selected != null && !IsInstanceValid(selected)) Select(null);
        if (selected == null) return;

        var c = selected.Command;
        string txt = $"Nama: {selected.TypeName.ToUpperInvariant()}  [{selected.AiMethod}]  {selected.StatusText}\n" +
                     $"Metrik: Makan {selected.Metrics.FoodSeconds:F1}s | Racun {selected.Metrics.PoisonBites}x | Bahaya {selected.Metrics.Punishments}x | Kabur {selected.Metrics.EscapeEvents}x\n" +
                     $"Motorik: Maju {c.Forward:F1} | Belok {c.Turn:+0.00;-0.00} | Valensi {c.Valence:+0.00;-0.00}";

        if (selected is BrainNpc b)
        {
            var d = b.Brain.Decoder;
            txt += $"\nSyaraf: GF {d.Gf:F0}Hz | Loom {d.LoomL:F0}/{d.LoomR:F0} | MN9 {d.Mn9:F0}\n" +
                   $"DNa Turn: L {d.TurnL:F0} / R {d.TurnR:F0} | MBON: +{d.Approach:F1} / -{d.Avoid:F1}\n" +
                   $"Memori KC->MBON: {Plasticity.MeanRelativeWeight(b.Brain.State):P1} | Tick: {b.DecideMs:F2}ms (LOD x{b.BrainTimeScale})";
            raster.Push();
        }
        detail.Text = txt;
    }
}
