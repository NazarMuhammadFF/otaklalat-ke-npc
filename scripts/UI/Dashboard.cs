using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OtakLalat.Brain.Core;
using OtakLalat.Npc;
using OtakLalat.World;

namespace OtakLalat.UI;

/// <summary>
/// Dashboard Evaluasi Utama berbasis Bento Grid sejati:
/// Layar terbagi secara proporsional ke dalam section frame mandiri.
/// Viewport Simulasi 3D ditempatkan di dalam frame section khusus (SubViewportContainer),
/// bukan fullscreen HUD, terintegrasi dengan panel kontrol dan raster neural 2.635 LIF.
/// </summary>
public partial class Dashboard : Control
{
    // Komponen Viewport 3D
    SubViewportContainer viewportContainer;
    SubViewport subViewport;
    MapBase currentMap;
    int currentMapIndex = 0;

    // Komponen UI Bento
    Label fpsLabel, tickLabel, lodLabel, mapTitleLabel;
    Label statsLabel, inspectLabel, noticeTraitsLabel;
    OptionButton typeOpt, methodOpt;
    CheckButton clickSpawnCheck;
    Button[] mapButtons;
    VBoxContainer scenBox;
    readonly List<HSlider> sliders = new();
    readonly List<Label> sliderValLabels = new();
    SpikeRaster raster;
    NpcBase selected;

    bool labelsVisible = true;
    bool clickSpawn = false;

    static readonly string[] TraitNames = { "Fearfulness", "Appetite", "Pickiness", "Curiosity", "LearningRate", "NeuralNoise" };

    public override void _Ready()
    {
        // 1. Setup Canvas Dasar Dashboard
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect
        {
            Color = new Color(0.04f, 0.05f, 0.08f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        var rootBox = new VBoxContainer();
        rootBox.AddThemeConstantOverride("separation", 6);
        margin.AddChild(rootBox);

        // 2. Baris Header / Top Bar (Map Switcher & Engine Stats)
        BuildTopBar(rootBox);

        // 3. Grid Utama (3 Kolom Bento: Kiri [300px], Tengah [Flex/Expand], Kanan [340px])
        var mainGrid = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        mainGrid.AddThemeConstantOverride("separation", 8);
        rootBox.AddChild(mainGrid);

        // Kolom Kiri: Spawner, Skenario Uji, & Skor Komparatif
        BuildLeftColumn(mainGrid);

        // Kolom Tengah: Frame Viewport Simulasi 3D & Spike Raster
        BuildCenterColumn(mainGrid);

        // Kolom Kanan: Inspeksi NPC Terpilih & 6 Knob Sifat Biologis
        BuildRightColumn(mainGrid);

        // 4. Muat Map Default (Map 1: Makan vs Racun)
        SwitchMap(0);

        if (OS.GetCmdlineUserArgs().Contains("auto-screenshot") || OS.GetCmdlineArgs().Contains("--auto-screenshot"))
        {
            GetTree().CreateTimer(1.5).Timeout += () =>
            {
                var img = GetViewport().GetTexture().GetImage();
                DirAccess.MakeDirAbsolute("reports/screenshots");
                img.SavePng("reports/screenshots/bento_dashboard.png");
                GetTree().Quit();
            };
        }
    }

    // =========================================================================
    // SECTION 1: HEADER / TOP BAR
    // =========================================================================
    void BuildTopBar(VBoxContainer parent)
    {
        var topCard = new PanelContainer();
        var style = CreateCardStyle(new Color(0.25f, 0.35f, 0.5f, 0.5f), new Color(0.06f, 0.08f, 0.12f, 0.95f), 6);
        style.ContentMarginTop = 4;
        style.ContentMarginBottom = 4;
        topCard.AddThemeStyleboxOverride("panel", style);

        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 8);
        topCard.AddChild(topRow);

        // Judul Aplikasi
        var titleBox = new VBoxContainer();
        var titleLbl = new Label { Text = "🧠 DROSOPHILA CONNECTOME NPC DASHBOARD" };
        titleLbl.AddThemeColorOverride("font_color", new Color(0.38f, 0.82f, 0.98f));
        titleLbl.AddThemeFontSizeOverride("font_size", 14);
        var subTitle = new Label { Text = "Biologically Emergent NPCs (Shiu et al. 2024) vs FSM Baseline" };
        subTitle.AddThemeColorOverride("font_color", new Color(0.55f, 0.65f, 0.75f));
        subTitle.AddThemeFontSizeOverride("font_size", 10);
        titleBox.AddChild(titleLbl);
        titleBox.AddChild(subTitle);
        topRow.AddChild(titleBox);

        topRow.AddChild(new VSeparator());

        // Tombol Switch 4 Map Uji
        mapButtons = new Button[MapRegistry.Maps.Length];
        var mapBox = new HBoxContainer();
        mapBox.AddThemeConstantOverride("separation", 4);
        for (int i = 0; i < MapRegistry.Maps.Length; i++)
        {
            int idx = i;
            var (title, _) = MapRegistry.Maps[i];
            var btn = new Button
            {
                Text = $"{i + 1}. {title}",
                FocusMode = FocusModeEnum.None,
            };
            btn.Pressed += () => SwitchMap(idx);
            mapButtons[i] = btn;
            mapBox.AddChild(btn);
        }
        topRow.AddChild(mapBox);

        // Spacer
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        topRow.AddChild(spacer);

        // Status Performa
        fpsLabel = new Label { Text = "FPS: 60" };
        fpsLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 0.6f));
        topRow.AddChild(fpsLabel);

        tickLabel = new Label { Text = "Tick: 0.0ms" };
        tickLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.4f));
        topRow.AddChild(tickLabel);

        lodLabel = new Label { Text = "LOD: ON" };
        lodLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 1f));
        topRow.AddChild(lodLabel);

        var fullBtn = new Button { Text = "⛶ Fullscreen (F11)", FocusMode = FocusModeEnum.None };
        fullBtn.Pressed += ToggleFullscreen;
        topRow.AddChild(fullBtn);

        parent.AddChild(topCard);
    }

    // =========================================================================
    // SECTION 2: KOLOM KIRI (SPAWNER, SKENARIO, SKOR)
    // =========================================================================
    void BuildLeftColumn(HBoxContainer parent)
    {
        var col = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(290, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        col.AddThemeConstantOverride("separation", 8);

        // Card 1: 🐾 Spawner Populasi
        var cardSpawn = CreateBentoCard("🐾 SPAWNER POPULASI", new Color(0.20f, 0.83f, 0.60f), out var spawnBox);
        var row1 = new HBoxContainer();
        typeOpt = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var t in MapBase.TypeIds) typeOpt.AddItem(t);
        methodOpt = new OptionButton { FocusMode = FocusModeEnum.None };
        methodOpt.AddItem("Otak");
        methodOpt.AddItem("FSM");
        row1.AddChild(typeOpt);
        row1.AddChild(methodOpt);
        var spawnBtn = new Button { Text = "+ Spawn", FocusMode = FocusModeEnum.None };
        spawnBtn.Pressed += SpawnAtCenter;
        row1.AddChild(spawnBtn);
        spawnBox.AddChild(row1);

        var row2 = new HBoxContainer();
        clickSpawnCheck = new CheckButton { Text = "Klik lantai spawn", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        clickSpawnCheck.Toggled += on => clickSpawn = on;
        row2.AddChild(clickSpawnCheck);
        var clearBtn = new Button { Text = "🗑️ Hapus Semua", FocusMode = FocusModeEnum.None };
        clearBtn.Pressed += () => { Select(null); currentMap?.ClearNpcs(); };
        row2.AddChild(clearBtn);
        spawnBox.AddChild(row2);
        col.AddChild(cardSpawn);

        // Card 2: 🎯 Skenario Uji Khusus
        var cardScen = CreateBentoCard("🎯 SKENARIO UJI KHUSUS", new Color(0.98f, 0.57f, 0.24f), out scenBox);
        col.AddChild(cardScen);

        // Card 3: 📊 Skor Komparatif Real-Time
        var cardStats = CreateBentoCard("📊 SKOR KOMPARATIF REAL-TIME", new Color(0.51f, 0.55f, 0.97f), out var statsBox);
        cardStats.SizeFlagsVertical = SizeFlags.ExpandFill;
        statsLabel = new Label();
        statsLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.95f, 1f));
        statsBox.AddChild(statsLabel);
        col.AddChild(cardStats);

        // Quick Help Pill
        var helpPill = new PanelContainer();
        helpPill.AddThemeStyleboxOverride("panel", CreateCardStyle(new Color(0.2f, 0.3f, 0.4f, 0.4f), new Color(0.04f, 0.06f, 0.09f, 0.8f), 6));
        var helpLbl = new Label
        {
            Text = "💡 WASD: Gerak | Scroll: Zoom | F: Makanan\n[Ctrl+1..4] Ganti Map | [Tab] Label Nama",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        helpLbl.AddThemeFontSizeOverride("font_size", 10);
        helpLbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
        helpPill.AddChild(helpLbl);
        col.AddChild(helpPill);

        parent.AddChild(col);
    }

    // =========================================================================
    // SECTION 3: KOLOM TENGAH (VIEWPORT 3D & SPIKE RASTER)
    // =========================================================================
    void BuildCenterColumn(HBoxContainer parent)
    {
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2.4f,
        };
        col.AddThemeConstantOverride("separation", 8);

        // Header Viewport 3D
        var camBar = new HBoxContainer();
        mapTitleLabel = new Label { Text = "Map: Memuat...", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mapTitleLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 1f));
        camBar.AddChild(mapTitleLabel);

        var btnFps = new Button { Text = "F1: FPS", FocusMode = FocusModeEnum.None };
        btnFps.Pressed += () => currentMap?.PlayerNode?.SetCameraMode(Player.CamMode.FirstPerson);
        var btnTop = new Button { Text = "F2: Kamera Atas", FocusMode = FocusModeEnum.None };
        btnTop.Pressed += () => currentMap?.PlayerNode?.SetCameraMode(Player.CamMode.TopDown);
        var btnFol = new Button { Text = "F3: Ikuti NPC", FocusMode = FocusModeEnum.None };
        btnFol.Pressed += () =>
        {
            if (selected == null) Select(NpcBase.All.OfType<BrainNpc>().FirstOrDefault() ?? NpcBase.All.FirstOrDefault());
            currentMap?.PlayerNode?.SetCameraMode(Player.CamMode.FollowNpc, selected);
        };
        camBar.AddChild(btnFps);
        camBar.AddChild(btnTop);
        camBar.AddChild(btnFol);

        // Card 4: 🎮 Viewport Simulasi 3D (SubViewportContainer Frame)
        var cardVp = CreateBentoCard("🎮 SIMULASI DUNIA 3D", new Color(0.22f, 0.74f, 0.97f), out var vpBox, camBar);
        cardVp.SizeFlagsVertical = SizeFlags.ExpandFill;
        cardVp.SizeFlagsStretchRatio = 3.0f;
        cardVp.CustomMinimumSize = new Vector2(0, 380);

        viewportContainer = new SubViewportContainer
        {
            Stretch = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 360),
        };
        subViewport = new SubViewport
        {
            Size = new Vector2I(800, 500),
            OwnWorld3D = true,
            HandleInputLocally = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        viewportContainer.AddChild(subViewport);
        viewportContainer.GuiInput += OnViewportGuiInput;
        vpBox.AddChild(viewportContainer);
        col.AddChild(cardVp);

        // Card 5: ⚡ Spike Raster Neuron (2.635 LIF)
        var legendLbl = new Label { Text = "Cyan: Sensorik | Kuning: KC/MBON/CX | Merah: Motorik" };
        legendLbl.AddThemeFontSizeOverride("font_size", 10);
        legendLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 0.9f));
        var cardRaster = CreateBentoCard("⚡ SPIKE RASTER NEURON (2.635 LIF CONNECTOME)", new Color(0.96f, 0.25f, 0.37f), out var rasterBox, legendLbl);
        cardRaster.CustomMinimumSize = new Vector2(0, 150);
        raster = new SpikeRaster { CustomMinimumSize = new Vector2(0, 120), SizeFlagsVertical = SizeFlags.ExpandFill };
        rasterBox.AddChild(raster);
        col.AddChild(cardRaster);

        parent.AddChild(col);
    }

    // =========================================================================
    // SECTION 4: KOLOM KANAN (INSPEKSI NPC & 6 KNOB SIFAT)
    // =========================================================================
    void BuildRightColumn(HBoxContainer parent)
    {
        var col = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(330, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        col.AddThemeConstantOverride("separation", 8);

        // Card 6: 🔬 Inspeksi Entitas Terpilih
        var deselectBtn = new Button { Text = "❌ Batal", FocusMode = FocusModeEnum.None };
        deselectBtn.Pressed += () => Select(null);
        var cardInspect = CreateBentoCard("🔬 INSPEKSI ENTITAS", new Color(0.18f, 0.83f, 0.75f), out var inspectBox, deselectBtn);
        inspectLabel = new Label
        {
            Text = "Klik hewan di dalam frame simulasi 3D untuk menginspeksi aktivitas sirkuit saraf dan motorik.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        inspectLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.92f, 0.98f));
        inspectBox.AddChild(inspectLabel);
        col.AddChild(cardInspect);

        // Card 7: 🧬 6 Knob Sifat Biologis (Live Modulasi)
        var cardTraits = CreateBentoCard("🧬 6 KNOB SIFAT BIOLOGIS (LIVE TUNING)", new Color(0.96f, 0.62f, 0.04f), out var traitBox);
        cardTraits.SizeFlagsVertical = SizeFlags.ExpandFill;

        noticeTraitsLabel = new Label
        {
            Text = "Pilih NPC Otak untuk modulasi 6 sifat biologi secara langsung.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        noticeTraitsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.8f));
        traitBox.AddChild(noticeTraitsLabel);

        for (int i = 0; i < TraitNames.Length; i++)
        {
            int k = i;
            var row = new HBoxContainer();
            var nameLbl = new Label { Text = TraitNames[i], SizeFlagsHorizontal = SizeFlags.ExpandFill };
            nameLbl.AddThemeFontSizeOverride("font_size", 12);
            var valLbl = new Label { Text = "1.00" };
            valLbl.AddThemeColorOverride("font_color", new Color(0.96f, 0.85f, 0.4f));
            valLbl.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(nameLbl);
            row.AddChild(valLbl);
            sliderValLabels.Add(valLbl);
            traitBox.AddChild(row);

            var s = new HSlider
            {
                MinValue = 0,
                MaxValue = 2,
                Step = 0.05,
                CustomMinimumSize = new Vector2(0, 16),
                Editable = false,
            };
            s.ValueChanged += v =>
            {
                SetTrait(k, (float)v);
                valLbl.Text = v.ToString("F2");
            };
            sliders.Add(s);
            traitBox.AddChild(s);
        }
        col.AddChild(cardTraits);

        parent.AddChild(col);
    }

    // =========================================================================
    // MANAJEMEN MAP & SCENE VIEWPORT
    // =========================================================================
    public void SwitchMap(int index)
    {
        if (index < 0 || index >= MapRegistry.Maps.Length) return;
        currentMapIndex = index;

        // Highlight tombol map aktif
        for (int i = 0; i < mapButtons.Length; i++)
        {
            mapButtons[i].Disabled = i == index;
        }

        var (title, path) = MapRegistry.Maps[index];

        // Hapus map lama dari subviewport jika ada
        if (currentMap != null)
        {
            Select(null);
            subViewport.RemoveChild(currentMap);
            currentMap.QueueFree();
            currentMap = null;
        }

        // Muat scene map baru ke dalam SubViewport frame
        var scene = GD.Load<PackedScene>(path);
        if (scene == null) return;

        currentMap = scene.Instantiate<MapBase>();
        currentMap.SuppressOverlay = true; // Jangan buat HUD lama
        subViewport.AddChild(currentMap);

        mapTitleLabel.Text = $"Map: {currentMap.MapTitle}";

        // Perbarui tombol skenario uji sesuai map
        RebuildScenarioButtons();
    }

    void RebuildScenarioButtons()
    {
        foreach (Node child in scenBox.GetChildren()) child.QueueFree();
        if (currentMap == null) return;

        var st = currentMap.Stats;
        bool hasAny = false;
        if (st.HasFlag(MapStats.Escape))
        {
            var btn = new Button { Text = "🦅 Uji Looming Predator", FocusMode = FocusModeEnum.None };
            btn.Pressed += TriggerLoomingTest;
            scenBox.AddChild(btn);
            hasAny = true;
        }
        if (st.HasFlag(MapStats.Food) || st.HasFlag(MapStats.Poison))
        {
            var btn = new Button { Text = "🍎 Uji Makan Manis vs Racun", FocusMode = FocusModeEnum.None };
            btn.Pressed += TriggerFeedingTest;
            scenBox.AddChild(btn);
            hasAny = true;
        }
        if (st.HasFlag(MapStats.Danger))
        {
            var btn = new Button { Text = "⚡ Uji Belajar Bahaya Terkondisi", FocusMode = FocusModeEnum.None };
            btn.Pressed += TriggerDangerLearningTest;
            scenBox.AddChild(btn);
            hasAny = true;
        }
        if (!hasAny)
        {
            var lbl = new Label { Text = "(Eksplorasi Navigasi Bebas)" };
            lbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
            scenBox.AddChild(lbl);
        }
    }

    // =========================================================================
    // INTERAKSI MOUSE PADA FRAME SIMULASI 3D (RAYCAST & SPAWN)
    // =========================================================================
    void OnViewportGuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (currentMap?.PlayerNode?.Camera == null) return;
            var cam = currentMap.PlayerNode.Camera;
            var from = cam.ProjectRayOrigin(mb.Position);
            var dir = cam.ProjectRayNormal(mb.Position);

            if (clickSpawn)
            {
                var planeHit = new Plane(Vector3.Up, 0f).IntersectsRay(from, dir);
                if (planeHit != null)
                {
                    SpawnNpcAt(planeHit.Value);
                    return;
                }
            }

            var q = PhysicsRayQueryParameters3D.Create(from, from + dir * 200f, collisionMask: 2);
            var hit = cam.GetWorld3D().DirectSpaceState.IntersectRay(q);
            if (hit.Count > 0 && hit["collider"].As<Node>() is NpcBase npc)
            {
                Select(npc);
            }
            else
            {
                Select(null);
            }
        }
    }

    void SpawnAtCenter()
    {
        if (currentMap?.PlayerNode?.Camera == null)
        {
            if (currentMap != null) SpawnNpcAt(currentMap.RandomSpot());
            return;
        }
        var center = (Vector2)subViewport.Size / 2f;
        var cam = currentMap.PlayerNode.Camera;
        var from = cam.ProjectRayOrigin(center);
        var dir = cam.ProjectRayNormal(center);
        var hit = new Plane(Vector3.Up, 0f).IntersectsRay(from, dir);
        SpawnNpcAt(hit ?? currentMap.RandomSpot());
    }

    void SpawnNpcAt(Vector3 pos)
    {
        if (currentMap == null) return;
        string type = typeOpt.GetItemText(typeOpt.Selected);
        bool isBrain = methodOpt.Selected == 0;
        var n = currentMap.SpawnNpc(type, isBrain, pos);
        Select(n);
    }

    // =========================================================================
    // SKENARIO UJI
    // =========================================================================
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
        currentMap?.Predator?.DiveAt(t);
    }

    void TriggerFeedingTest()
    {
        var t = EnsureTargetNpc("babi");
        if (t == null) return;
        t.Hunger = 0.9f;
        var fwd = -t.GlobalBasis.Z;
        var side = t.GlobalBasis.X;
        currentMap?.SpawnEdible(new Vector3(t.GlobalPosition.X, 0, t.GlobalPosition.Z) + fwd * 2.2f - side * 1.1f, false);
        currentMap?.SpawnEdible(new Vector3(t.GlobalPosition.X, 0, t.GlobalPosition.Z) + fwd * 2.2f + side * 1.1f, true);
    }

    void TriggerDangerLearningTest()
    {
        var t = EnsureTargetNpc("rusa");
        var dz = WorldRegistry.Dangers.FirstOrDefault();
        if (t == null || dz == null || currentMap == null) return;
        var startPos = dz.GlobalPosition + new Vector3(0, 0.1f, dz.Radius + 1.2f);
        t.GlobalPosition = startPos;
        t.LookAt(new Vector3(dz.GlobalPosition.X, startPos.Y, dz.GlobalPosition.Z), Vector3.Up);
    }

    // =========================================================================
    // SELEKSI & MODULASI 6 KNOB SIFAT
    // =========================================================================
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
        if (currentMap?.PlayerNode != null && n != null) currentMap.PlayerNode.FollowTarget = n;

        if (n == null)
        {
            inspectLabel.Text = "Klik hewan di dalam frame simulasi 3D untuk menginspeksi aktivitas sirkuit saraf dan motorik.";
            noticeTraitsLabel.Text = "Pilih NPC Otak untuk modulasi 6 sifat biologi secara langsung.";
            foreach (var s in sliders) s.Editable = false;
            return;
        }

        bool isBrain = n is BrainNpc;
        noticeTraitsLabel.Text = isBrain ? $"Mengatur 6 Sifat Biologi ({n.TypeName}):" : "(Knob sifat biologi khusus untuk NPC Otak Spiking)";
        foreach (var s in sliders) s.Editable = isBrain;

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

    // =========================================================================
    // INPUT KEYBOARD & FULLSCREEN
    // =========================================================================
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } k)
        {
            int idx = (int)(k.Keycode - Key.Key1);
            if (k.CtrlPressed && idx >= 0 && idx < MapRegistry.Maps.Length) SwitchMap(idx);
            else if (k.Keycode == Key.Tab)
            {
                labelsVisible = !labelsVisible;
                foreach (var n in NpcBase.All) n.ShowLabel = labelsVisible;
                if (currentMap != null) foreach (var z in currentMap.ZoneLabels) z.Visible = labelsVisible;
            }
            else if (k.Keycode == Key.L)
            {
                if (currentMap?.Ai != null) currentMap.Ai.LodEnabled = !currentMap.Ai.LodEnabled;
            }
            else if (k.Keycode == Key.F11 || (k.AltPressed && k.Keycode == Key.Enter)) ToggleFullscreen();
            else if (k.Keycode == Key.F12)
            {
                var img = GetViewport().GetTexture().GetImage();
                DirAccess.MakeDirAbsolute("reports/screenshots");
                img.SavePng("reports/screenshots/bento_dashboard.png");
            }
            else if (k.Keycode == Key.Escape) Select(null);
        }
    }

    void ToggleFullscreen()
    {
        var m = DisplayServer.WindowGetMode();
        bool isFull = m == DisplayServer.WindowMode.Fullscreen || m == DisplayServer.WindowMode.ExclusiveFullscreen;
        DisplayServer.WindowSetMode(isFull ? DisplayServer.WindowMode.Maximized : DisplayServer.WindowMode.Fullscreen);
    }

    // =========================================================================
    // UPDATE REAL-TIME (STATS, MOTOR COMMANDS, NEURAL FIRING)
    // =========================================================================
    public override void _Process(double delta)
    {
        fpsLabel.Text = $"FPS: {Engine.GetFramesPerSecond()}";
        if (currentMap?.Ai != null)
        {
            tickLabel.Text = $"Tick: {currentMap.Ai.LastDecideWallMs:F2}ms";
            lodLabel.Text = $"LOD: {(currentMap.Ai.LodEnabled ? "ON" : "OFF")}";
        }

        // Statistik Agregat Populasi
        var brainList = NpcBase.All.OfType<BrainNpc>().ToList();
        var fsmList = NpcBase.All.OfType<BaselineNpc>().ToList();
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

        var lines = new List<string> { $"Populasi: 🧠 Otak ({brainList.Count}) vs ⚙️ FSM ({fsmList.Count})\n" };
        if (currentMap != null)
        {
            var st = currentMap.Stats;
            if (st.HasFlag(MapStats.Food)) lines.Add($"🍎 Makan Manis : 🧠 {bFood:F1}s  vs  ⚙️ {fFood:F1}s");
            if (st.HasFlag(MapStats.Poison)) lines.Add($"🤢 Gigit Racun : 🧠 {bPois:F0}x  vs  ⚙️ {fPois:F0}x");
            if (st.HasFlag(MapStats.Escape)) lines.Add($"🦅 Kabur Looming: 🧠 {bEsc:F0}x  vs  ⚙️ {fEsc:F0}x");
            if (st.HasFlag(MapStats.Danger)) lines.Add($"⚡ Zona Bahaya : 🧠 {bDang:F1}s  vs  ⚙️ {fDang:F1}s");
            if (st.HasFlag(MapStats.Explore)) lines.Add($"🧭 Jarak Tempuh : 🧠 {bDist:F0}m  vs  ⚙️ {fDist:F0}m");
        }
        statsLabel.Text = string.Join("\n", lines);

        // Update Inspeksi NPC Terpilih
        if (selected != null && !IsInstanceValid(selected)) Select(null);
        if (selected == null) return;

        var c = selected.Command;
        string txt = $"Nama: {selected.TypeName.ToUpperInvariant()}  [{selected.AiMethod}]  {selected.StatusText}\n" +
                     $"Metrik: Makan {selected.Metrics.FoodSeconds:F1}s | Racun {selected.Metrics.PoisonBites}x | Bahaya {selected.Metrics.Punishments}x | Kabur {selected.Metrics.EscapeEvents}x\n" +
                     $"Motorik: Maju {c.Forward:F1} | Belok {c.Turn:+0.00;-0.00} | Valensi {c.Valence:+0.00;-0.00}";

        if (selected is BrainNpc b)
        {
            var d = b.Brain.Decoder;
            txt += $"\nSyaraf Motorik: GF {d.Gf:F0}Hz | Loom {d.LoomL:F0}/{d.LoomR:F0} | MN9 {d.Mn9:F0}\n" +
                   $"DNa Kemudi: L {d.TurnL:F0} / R {d.TurnR:F0} | MBON: +{d.Approach:F1} / -{d.Avoid:F1}\n" +
                   $"Memori Plastis KC->MBON: {Plasticity.MeanRelativeWeight(b.Brain.State):P1}\n" +
                   $"Tick AI: {b.DecideMs:F2}ms (LOD x{b.BrainTimeScale})";
            raster.Push();
        }
        inspectLabel.Text = txt;
    }

    // =========================================================================
    // HELPER STYLING BENTO
    // =========================================================================
    PanelContainer CreateBentoCard(string title, Color accentColor, out VBoxContainer contentBox, Control headerExtra = null)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", CreateCardStyle(accentColor, new Color(0.07f, 0.09f, 0.13f, 0.95f), 8));

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
        card.AddChild(vbox);

        var header = new HBoxContainer();
        var titleLbl = new Label
        {
            Text = title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        titleLbl.AddThemeColorOverride("font_color", accentColor);
        titleLbl.AddThemeFontSizeOverride("font_size", 12);
        header.AddChild(titleLbl);

        if (headerExtra != null) header.AddChild(headerExtra);
        vbox.AddChild(header);

        contentBox = new VBoxContainer();
        contentBox.AddThemeConstantOverride("separation", 6);
        contentBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(contentBox);

        return card;
    }

    StyleBoxFlat CreateCardStyle(Color accentColor, Color bgColor, int radius)
    {
        return new StyleBoxFlat
        {
            BgColor = bgColor,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.45f),
            ContentMarginLeft = 8,
            ContentMarginTop = 6,
            ContentMarginRight = 8,
            ContentMarginBottom = 6,
            ShadowColor = new Color(0, 0, 0, 0.45f),
            ShadowSize = 4,
        };
    }
}
