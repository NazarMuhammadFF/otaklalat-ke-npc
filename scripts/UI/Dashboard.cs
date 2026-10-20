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
    CheckButton clickSpawnCheck, humanOdorCheck;
    Button[] mapButtons;
    VBoxContainer scenBox;
    readonly List<HSlider> sliders = new();
    readonly List<Label> sliderValLabels = new();
    SpikeRaster raster;
    BrainView3D brain3D;
    NpcEyeView eyeView;
    PanelContainer cardVp, cardRaster;
    Button brainFocusBtn;
    NpcBase selected;

    bool labelsVisible = true;
    bool clickSpawn = false;
    bool brainFocused = false;

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
        margin.AddThemeConstantOverride("margin_left", 6);
        margin.AddThemeConstantOverride("margin_top", 4);
        margin.AddThemeConstantOverride("margin_right", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        AddChild(margin);

        var rootBox = new VBoxContainer();
        rootBox.AddThemeConstantOverride("separation", 6);
        rootBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rootBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        margin.AddChild(rootBox);

        // 2. Baris Header / Top Bar (Map Switcher & Engine Stats)
        BuildTopBar(rootBox);

        // 3. Grid Utama (3 Kolom Bento: Kiri [245px], Tengah [Flex/Expand], Kanan [265px])
        var mainGrid = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        mainGrid.AddThemeConstantOverride("separation", 6);
        rootBox.AddChild(mainGrid);

        // Kolom Kiri: Spawner, Skenario Uji, & Skor Komparatif
        BuildLeftColumn(mainGrid);

        // Kolom Tengah: Frame Viewport Simulasi 3D & Spike Raster
        BuildCenterColumn(mainGrid);

        // Kolom Kanan: Inspeksi NPC Terpilih & 6 Knob Sifat Biologis
        BuildRightColumn(mainGrid);

        // 4. Muat Map Default (atau dari argumen --map=0..3)
        int startMap = 0;
        bool autoShot = false;
        bool startFocusBrain = false;
        string shotPath = "reports/screenshots/bento_dashboard.png";
        foreach (var raw in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
        {
            var a = raw.TrimStart('-');
            if (a.StartsWith("map=") && int.TryParse(a.Substring(4), out int mIdx))
                startMap = Mathf.Clamp(mIdx, 0, MapRegistry.Maps.Length - 1);
            else if (a.StartsWith("screenshot-file="))
                shotPath = a.Substring("screenshot-file=".Length);
            else if (a == "auto-screenshot")
                autoShot = true;
            else if (a == "focus-brain")
                startFocusBrain = true;
        }
        SwitchMap(startMap);
        if (startFocusBrain) ToggleBrainFocus();

        if (autoShot)
        {
            GetTree().CreateTimer(1.5).Timeout += () =>
            {
                var img = GetViewport().GetTexture().GetImage();
                DirAccess.MakeDirAbsolute("reports/screenshots");
                img.SavePng(shotPath);
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
        var titleLbl = new Label { Text = "🧠 OTAK LALAT NPC" };
        titleLbl.AddThemeColorOverride("font_color", new Color(0.38f, 0.82f, 0.98f));
        titleLbl.AddThemeFontSizeOverride("font_size", 13);
        var subTitle = new Label { Text = "Connectome LIF vs FSM" };
        subTitle.AddThemeColorOverride("font_color", new Color(0.55f, 0.65f, 0.75f));
        subTitle.AddThemeFontSizeOverride("font_size", 9);
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
            string shortTitle = i switch
            {
                0 => "Makan",
                1 => "Bahaya",
                2 => "Predator",
                3 => "Hutan",
                _ => MapRegistry.Maps[i].Title
            };
            var btn = new Button
            {
                Text = $"{i + 1}: {shortTitle}",
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

        // Status Performa (dengan lebar minimum & clip agar tidak menggeser topRow saat digit berubah)
        fpsLabel = new Label { Text = "FPS: 60", CustomMinimumSize = new Vector2(58, 0), ClipText = true };
        fpsLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 0.6f));
        topRow.AddChild(fpsLabel);

        tickLabel = new Label { Text = "Tick: 0.00ms", CustomMinimumSize = new Vector2(92, 0), ClipText = true };
        tickLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.4f));
        topRow.AddChild(tickLabel);

        lodLabel = new Label { Text = "LOD: ON", CustomMinimumSize = new Vector2(58, 0), ClipText = true };
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
        var wrap = new Control
        {
            CustomMinimumSize = new Vector2(270, 0),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            ClipContents = true,
        };
        var col = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        col.SetAnchorsPreset(LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 6);
        wrap.AddChild(col);

        // Card 1: 🐾 Spawner Populasi
        var cardSpawn = CreateBentoCard("🐾 SPAWNER POPULASI", new Color(0.20f, 0.83f, 0.60f), out var spawnBox);
        
        var row1 = new HBoxContainer();
        row1.AddThemeConstantOverride("separation", 4);
        typeOpt = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var t in MapBase.TypeIds) typeOpt.AddItem(t);
        methodOpt = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = true };
        methodOpt.AddItem("Otak");
        methodOpt.AddItem("FSM");
        methodOpt.AddItem("Otak·retina");
        row1.AddChild(typeOpt);
        row1.AddChild(methodOpt);
        spawnBox.AddChild(row1);

        var row2 = new HBoxContainer();
        row2.AddThemeConstantOverride("separation", 4);
        var spawnBtn = new Button { Text = "+ Spawn", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        spawnBtn.Pressed += SpawnAtCenter;
        row2.AddChild(spawnBtn);
        var clearBtn = new Button { Text = "🗑️ Hapus", FocusMode = FocusModeEnum.None };
        clearBtn.Pressed += () => { Select(null); currentMap?.ClearNpcs(); };
        row2.AddChild(clearBtn);
        spawnBox.AddChild(row2);

        var row3 = new HBoxContainer();
        clickSpawnCheck = new CheckButton { Text = "Klik lantai spawn", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        clickSpawnCheck.Toggled += on => clickSpawn = on;
        row3.AddChild(clickSpawnCheck);
        spawnBox.AddChild(row3);
        col.AddChild(cardSpawn);

        // Card 2: 🎯 Skenario Uji Khusus
        var cardScen = CreateBentoCard("🎯 SKENARIO UJI KHUSUS", new Color(0.98f, 0.57f, 0.24f), out scenBox);
        col.AddChild(cardScen);

        // Card 2b: 🧍 Pemain (Manusia)
        var cardHuman = CreateBentoCard("🧍 PEMAIN (MANUSIA)", new Color(0.55f, 0.9f, 0.45f), out var humanBox);
        humanOdorCheck = new CheckButton { Text = "Bau manusia (kanal netral)", ButtonPressed = true, FocusMode = FocusModeEnum.None };
        humanOdorCheck.AddThemeFontSizeOverride("font_size", 11);
        humanOdorCheck.Toggled += on => { if (currentMap?.PlayerNode != null) currentMap.PlayerNode.EmitsOdor = on; };
        humanBox.AddChild(humanOdorCheck);
        var humanHint = new Label
        {
            Text = "F1: sudut pandang manusia (WASD: jalan, Panah: kamera). Shift = lari; menyentuh NPC saat lari = nyeri (NPC belajar hindari bau Anda).",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        humanHint.AddThemeFontSizeOverride("font_size", 9);
        humanHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
        humanBox.AddChild(humanHint);
        col.AddChild(cardHuman);

        // Card 3: 📊 Skor Komparatif Real-Time
        var cardStats = CreateBentoCard("📊 SKOR KOMPARATIF REAL-TIME", new Color(0.51f, 0.55f, 0.97f), out var statsBox);
        cardStats.SizeFlagsVertical = SizeFlags.ExpandFill;
        statsLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        statsLabel.AddThemeFontSizeOverride("font_size", 11);
        statsLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.95f, 1f));
        statsBox.AddChild(statsLabel);
        col.AddChild(cardStats);

        // Quick Help Pill
        var helpPill = new PanelContainer();
        helpPill.AddThemeStyleboxOverride("panel", CreateCardStyle(new Color(0.2f, 0.3f, 0.4f, 0.4f), new Color(0.04f, 0.06f, 0.09f, 0.8f), 6));
        var helpLbl = new Label
        {
            Text = "💡 WASD: Gerak | ◄▲▼►: Kamera | Scroll: Zoom\n[F] Makanan | [Ctrl+1..4] Map | [Tab] Label",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        helpLbl.AddThemeFontSizeOverride("font_size", 9);
        helpLbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
        helpPill.AddChild(helpLbl);
        col.AddChild(helpPill);

        parent.AddChild(wrap);
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
        };
        col.AddThemeConstantOverride("separation", 6);

        // Header Viewport 3D
        var camBar = new HBoxContainer();
        mapTitleLabel = new Label { Text = "Map: Memuat...", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mapTitleLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 1f));
        mapTitleLabel.AddThemeFontSizeOverride("font_size", 11);
        camBar.AddChild(mapTitleLabel);

        var btnFps = new Button { Text = "F1: FPS", FocusMode = FocusModeEnum.None };
        btnFps.Pressed += () => currentMap?.PlayerNode?.SetCameraMode(Player.CamMode.FirstPerson);
        var btnTop = new Button { Text = "F2: Atas", FocusMode = FocusModeEnum.None };
        btnTop.Pressed += () => currentMap?.PlayerNode?.SetCameraMode(Player.CamMode.TopDown);
        var btnFol = new Button { Text = "F3: Ikuti", FocusMode = FocusModeEnum.None };
        btnFol.Pressed += () =>
        {
            if (selected == null) Select(NpcBase.All.OfType<BrainNpc>().FirstOrDefault() ?? NpcBase.All.FirstOrDefault());
            currentMap?.PlayerNode?.SetCameraMode(Player.CamMode.FollowNpc, selected);
        };
        camBar.AddChild(btnFps);
        camBar.AddChild(btnTop);
        camBar.AddChild(btnFol);

        // Card 4: 🎮 Viewport Simulasi 3D (SubViewportContainer Frame)
        cardVp = CreateBentoCard("🎮 SIMULASI DUNIA 3D", new Color(0.22f, 0.74f, 0.97f), out var vpBox, camBar);
        cardVp.SizeFlagsVertical = SizeFlags.ExpandFill;
        cardVp.SizeFlagsStretchRatio = 2.0f;
        cardVp.CustomMinimumSize = new Vector2(0, 290);

        viewportContainer = new SubViewportContainer
        {
            Stretch = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 260),
        };
        subViewport = new SubViewport
        {
            Size = new Vector2I(800, 460),
            OwnWorld3D = true,
            HandleInputLocally = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        viewportContainer.AddChild(subViewport);
        viewportContainer.GuiInput += OnViewportGuiInput;
        vpBox.AddChild(viewportContainer);
        col.AddChild(cardVp);

        // Baris bawah: ⚡ Spike Raster Neuron (2.635 LIF) + 🧠 Otak 3D (neuron menyala per wilayah)
        var bottomRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 210),
        };
        bottomRow.AddThemeConstantOverride("separation", 6);
        bottomRow.SizeFlagsStretchRatio = 1.45f;

        var legendLbl = new Label { Text = "Cyan: Sensorik | Kuning: KC/CX | Merah: Motorik", ClipText = true };
        legendLbl.AddThemeFontSizeOverride("font_size", 9);
        legendLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 0.9f));
        cardRaster = CreateBentoCard("⚡ SPIKE RASTER (2.635 LIF)", new Color(0.96f, 0.25f, 0.37f), out var rasterBox, legendLbl);
        cardRaster.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cardRaster.SizeFlagsStretchRatio = 0.82f;
        raster = new SpikeRaster { CustomMinimumSize = new Vector2(0, 110), SizeFlagsVertical = SizeFlags.ExpandFill };
        rasterBox.AddChild(raster);
        bottomRow.AddChild(cardRaster);

        var brainHeaderRight = new HBoxContainer();
        brainHeaderRight.AddThemeConstantOverride("separation", 4);
        var brainHint = new Label { Text = "Seret: putar | Scroll: zoom", ClipText = true };
        brainHint.AddThemeFontSizeOverride("font_size", 9);
        brainHint.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 0.9f));
        brainHeaderRight.AddChild(brainHint);

        brainFocusBtn = new Button { Text = "⛶ Fokus (B)", FocusMode = FocusModeEnum.None };
        brainFocusBtn.AddThemeFontSizeOverride("font_size", 10);
        brainFocusBtn.Pressed += ToggleBrainFocus;
        brainHeaderRight.AddChild(brainFocusBtn);

        var cardBrain = CreateBentoCard("🧠 DROSOPHILA CONNECTOME 3D", new Color(0.25f, 0.88f, 1.0f), out var brainBox, brainHeaderRight);
        cardBrain.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cardBrain.SizeFlagsStretchRatio = 1.65f;
        brain3D = new BrainView3D();
        brainBox.AddChild(brain3D);
        bottomRow.AddChild(cardBrain);
        col.AddChild(bottomRow);

        parent.AddChild(col);
    }

    // =========================================================================
    // SECTION 4: KOLOM KANAN (INSPEKSI NPC & 6 KNOB SIFAT)
    // =========================================================================
    void BuildRightColumn(HBoxContainer parent)
    {
        var wrap = new Control
        {
            CustomMinimumSize = new Vector2(270, 0),
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            ClipContents = true,
        };
        var col = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        col.SetAnchorsPreset(LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 6);
        wrap.AddChild(col);

        // Card 6: 🔬 Inspeksi Entitas Terpilih
        var deselectBtn = new Button { Text = "❌ Batal", FocusMode = FocusModeEnum.None };
        deselectBtn.Pressed += () => Select(null);
        var cardInspect = CreateBentoCard("🔬 INSPEKSI ENTITAS", new Color(0.18f, 0.83f, 0.75f), out var inspectBox, deselectBtn);
        cardInspect.CustomMinimumSize = new Vector2(0, 105);
        inspectLabel = new Label
        {
            Text = "Klik hewan di dalam frame simulasi 3D untuk menginspeksi aktivitas sirkuit saraf dan motorik.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        inspectLabel.AddThemeFontSizeOverride("font_size", 10);
        inspectLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.92f, 0.98f));
        inspectBox.AddChild(inspectLabel);
        col.AddChild(cardInspect);

        // Card 6b: 👁 Pandangan NPC (default kamera mata; opsi: input sensorik asli ke otak)
        eyeView = new NpcEyeView();
        var cardEye = CreateBentoCard("👁 PANDANGAN NPC", new Color(0.65f, 0.55f, 0.98f), out var eyeBox, eyeView.ModeSelect);
        cardEye.SizeFlagsVertical = SizeFlags.ExpandFill;
        cardEye.SizeFlagsStretchRatio = 0.75f;
        eyeBox.AddChild(eyeView);
        col.AddChild(cardEye);

        // Card 7: 🧬 6 Knob Sifat Biologis (Live Modulasi)
        var cardTraits = CreateBentoCard("🧬 6 KNOB SIFAT (LIVE)", new Color(0.96f, 0.62f, 0.04f), out var traitBox);
        cardTraits.SizeFlagsVertical = SizeFlags.ExpandFill;
        traitBox.AddThemeConstantOverride("separation", 2);

        noticeTraitsLabel = new Label
        {
            Text = "Pilih NPC Otak untuk modulasi 6 sifat biologi secara langsung.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        noticeTraitsLabel.AddThemeFontSizeOverride("font_size", 9);
        noticeTraitsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.8f));
        traitBox.AddChild(noticeTraitsLabel);

        for (int i = 0; i < TraitNames.Length; i++)
        {
            int k = i;
            var row = new HBoxContainer();
            var nameLbl = new Label { Text = TraitNames[i], SizeFlagsHorizontal = SizeFlags.ExpandFill };
            nameLbl.AddThemeFontSizeOverride("font_size", 10);
            var valLbl = new Label { Text = "1.00" };
            valLbl.AddThemeColorOverride("font_color", new Color(0.96f, 0.85f, 0.4f));
            valLbl.AddThemeFontSizeOverride("font_size", 10);
            row.AddChild(nameLbl);
            row.AddChild(valLbl);
            sliderValLabels.Add(valLbl);
            traitBox.AddChild(row);

            var s = new HSlider
            {
                MinValue = 0,
                MaxValue = 2,
                Step = 0.05,
                CustomMinimumSize = new Vector2(0, 10),
                Editable = false,
                FocusMode = FocusModeEnum.None,
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

        parent.AddChild(wrap);
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
        eyeView.ShareWorld(subViewport);

        mapTitleLabel.Text = $"Map: {currentMap.MapTitle}";
        humanOdorCheck.SetPressedNoSignal(true);

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
            else if (currentMap.PlayerNode.Mode != Player.CamMode.FirstPerson)
            {
                // di mode manusia (F1) klik kosong hanya untuk fokus/menoleh; NPC terpilih dan panelnya tetap berjalan
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
        bool isBrain = methodOpt.Selected != 1;
        var n = currentMap.SpawnNpc(type, isBrain, pos, methodOpt.Selected == 2 ? VisionMode.Retinotopic : VisionMode.Scalar);
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

    static string AvgFlee(IEnumerable<NpcBase> npcs)
    {
        var all = npcs.SelectMany(n => n.Metrics.HumanFleeDist).ToList();
        return all.Count > 0 ? $"{all.Average():F1} m (n={all.Count})" : "-";
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
        brain3D.Bind(null);
        eyeView.Bind(n);
        selected = n;
        if (currentMap?.Ai != null) currentMap.Ai.FocusNpc = n;
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
            brain3D.Bind(b.Brain);
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
            else if (k.Keycode == Key.B) ToggleBrainFocus();
            else if (k.Keycode == Key.F11 || (k.AltPressed && k.Keycode == Key.Enter)) ToggleFullscreen();
            else if (k.Keycode == Key.F12)
            {
                var img = GetViewport().GetTexture().GetImage();
                DirAccess.MakeDirAbsolute("reports/screenshots");
                img.SavePng("reports/screenshots/bento_dashboard.png");
            }
            else if (k.Keycode == Key.Escape && currentMap?.PlayerNode?.Mode != Player.CamMode.FirstPerson) Select(null);   // di mode manusia Esc tidak membatalkan pilihan (panel tetap hidup); pakai tombol Batal
        }
    }

    void ToggleBrainFocus()
    {
        brainFocused = !brainFocused;
        if (cardVp != null) cardVp.Visible = !brainFocused;
        if (cardRaster != null) cardRaster.Visible = !brainFocused;
        if (brainFocusBtn != null) brainFocusBtn.Text = brainFocused ? "🗗 Split (B)" : "⛶ Fokus (B)";
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
            if (st.HasFlag(MapStats.Escape))
            {
                lines.Add($"🦅 Kabur Looming: 🧠 {bEsc:F0}x  vs  ⚙️ {fEsc:F0}x");
                lines.Add($"📏 Jarak kabur dari manusia: 🧠 {AvgFlee(brainList)}  vs  ⚙️ {AvgFlee(fsmList)}");
                lines.Add($"🖐️ Disentuh manusia: 🧠 {brainList.Sum(x => x.Metrics.HumanTouches)}x  vs  ⚙️ {fsmList.Sum(x => x.Metrics.HumanTouches)}x");
            }
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
