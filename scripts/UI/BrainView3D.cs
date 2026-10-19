using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using OtakLalat.Brain;
using OtakLalat.Brain.Core;

namespace OtakLalat.UI;

/// <summary>
/// Visualisasi ilmiah 3D Connectome Otak Lalat (Drosophila melanogaster - FlyWire FAFB v783).
/// Menampilkan:
/// 1. Cangkang anatomi neuropil luar tunggal berbentuk sayap organik (Smooth Metaball Isosurface
///    yang menyatukan Central Brain, Wing-like Optic Lobes kiri/kanan, Dorsal Mushroom Bodies,
///    Antennal Lobes, dan Ventral SEZ tanpa garis sambungan bola internal) dalam warna deep navy & biru bercahaya.
/// 2. 2.635 neuron point-cloud bercahaya tajam dengan warna biologis akurat:
///    - Aqua / Biru Muda : Mata / Lobula (input visual di sayap lateral kiri & kanan)
///    - Magenta / Pink   : Central Complex (kompas navigasi melingkar di pusat)
///    - Kuning / Oranye  : Mushroom Body (memori & pembelajaran di bagian tengah-atas)
///    - Hijau            : Lobus Antena & Pengecap SEZ (di bagian bawah)
///    - Merah & Putih    : Motor (descending) & Interneuron aktif
/// 3. Jaringan serat sinaptik (akson & dendrit) melengkung yang menghubungkan antar-kluster dan berdenyut.
/// 4. Overlay UI ilmiah ("DROSOPHILA CONNECTOME 3D VIEW", "DATASET: FAFB (FlyWire)", dan sidebar kluster aktif/total).
/// </summary>
public partial class BrainView3D : Control
{
    sealed class RegionInfo
    {
        public string Name;
        public Color Color;
        public string[] Groups;
        public int Total;
        public int Active;
    }

    struct FiberArc
    {
        public int Pre;
        public int Post;
        public Vector3 P0, P1, P2, P3;
        public Color BaseColor;
    }

    struct LobePrimitive
    {
        public Vector3 Center;
        public Vector3 InvRadii;
        public float TiltZ;
        public float SweepY;
    }

    const float DecaySeconds = 0.38f;
    const int FloatsPerInstance = 16; // 12 transform + 4 color

    readonly RegionInfo[] regions =
    {
        new()
        {
            Name = "Mata/Lobula (visual)",
            Color = new Color(0.10f, 0.88f, 1.00f), // Aqua / Light Blue
            Groups = new[] { "in_loom_L", "in_loom_R" },
        },
        new()
        {
            Name = "Central complex (compass)",
            Color = new Color(1.00f, 0.18f, 0.76f), // Magenta / Pink
            Groups = new[] { "in_epg", "in_pen_L", "in_pen_R", "in_er_L", "in_er_R" },
        },
        new()
        {
            Name = "Mushroom body (memori)",
            Color = new Color(1.00f, 0.74f, 0.12f), // Yellow / Orange
            Groups = new[] { "kc", "apl", "mbon", "out_mbon_avoid", "out_mbon_approach", "in_dan_reward", "in_dan_punish" },
        },
        new()
        {
            Name = "Lobus antena (bau)",
            Color = new Color(0.16f, 0.98f, 0.44f), // Vibrant Green
            Groups = new[] { "in_odor_food_L", "in_odor_food_R", "in_odor_poison_L", "in_odor_poison_R", "in_odor_neutral_L", "in_odor_neutral_R" },
        },
        new()
        {
            Name = "Pengecap (SEZ)",
            Color = new Color(0.18f, 0.88f, 0.62f), // Mint-Emerald Green (bagian bawah)
            Groups = new[] { "in_sugar_L", "in_sugar_R", "in_bitter_L", "in_bitter_R" },
        },
        new()
        {
            Name = "Motor (descending)",
            Color = new Color(1.00f, 0.20f, 0.24f), // Vibrant Crimson Red
            Groups = new[] { "out_gf", "out_loom_L", "out_loom_R", "out_mn9", "out_turn_L", "out_turn_R" },
        },
        new()
        {
            Name = "Interneuron (jalur)",
            Color = new Color(0.88f, 0.94f, 1.00f), // Pure White / Ice Blue
            Groups = Array.Empty<string>(),
        },
    };

    // Komponen pembentuk permukaan anatomi organik otak lalat (Smooth Metaball Field)
    static readonly LobePrimitive[] BrainLobes =
    {
        // Central Brain (Supraesophageal Cerebrum Kiri & Kanan)
        new() { Center = new Vector3(-0.42f, 0.12f, -0.02f), InvRadii = new Vector3(1f / 0.76f, 1f / 0.62f, 1f / 0.50f), TiltZ = -0.12f },
        new() { Center = new Vector3( 0.42f, 0.12f, -0.02f), InvRadii = new Vector3(1f / 0.76f, 1f / 0.62f, 1f / 0.50f), TiltZ =  0.12f },
        // Dorsal Mushroom Body Calyx Domes (Atas-Tengah)
        new() { Center = new Vector3(-0.58f, 0.54f, -0.08f), InvRadii = new Vector3(1f / 0.44f, 1f / 0.36f, 1f / 0.36f), TiltZ = -0.15f },
        new() { Center = new Vector3( 0.58f, 0.54f, -0.08f), InvRadii = new Vector3(1f / 0.44f, 1f / 0.36f, 1f / 0.36f), TiltZ =  0.15f },
        // Lobula / Pangkal Sayap Optik Kiri & Kanan
        new() { Center = new Vector3(-1.18f, 0.04f, -0.06f), InvRadii = new Vector3(1f / 0.56f, 1f / 0.58f, 1f / 0.44f), TiltZ = -0.18f, SweepY =  0.15f },
        new() { Center = new Vector3( 1.18f, 0.04f, -0.06f), InvRadii = new Vector3(1f / 0.56f, 1f / 0.58f, 1f / 0.44f), TiltZ =  0.18f, SweepY = -0.15f },
        // Medulla / Bentangan Sayap Optik Lateral Kiri & Kanan
        new() { Center = new Vector3(-1.82f, 0.06f, -0.16f), InvRadii = new Vector3(1f / 0.68f, 1f / 0.88f, 1f / 0.46f), TiltZ = -0.26f, SweepY =  0.24f },
        new() { Center = new Vector3( 1.82f, 0.06f, -0.16f), InvRadii = new Vector3(1f / 0.68f, 1f / 0.88f, 1f / 0.46f), TiltZ =  0.26f, SweepY = -0.24f },
        // Lamina / Ujung Luar Sayap Optik Kiri & Kanan
        new() { Center = new Vector3(-2.22f, 0.12f, -0.26f), InvRadii = new Vector3(1f / 0.36f, 1f / 0.92f, 1f / 0.40f), TiltZ = -0.32f, SweepY =  0.30f },
        new() { Center = new Vector3( 2.22f, 0.12f, -0.26f), InvRadii = new Vector3(1f / 0.36f, 1f / 0.92f, 1f / 0.40f), TiltZ =  0.32f, SweepY = -0.30f },
        // Lobus Antena & Ventral SEZ (Bagian Bawah)
        new() { Center = new Vector3(-0.32f, -0.44f, 0.24f), InvRadii = new Vector3(1f / 0.34f, 1f / 0.32f, 1f / 0.32f) },
        new() { Center = new Vector3( 0.32f, -0.44f, 0.24f), InvRadii = new Vector3(1f / 0.34f, 1f / 0.32f, 1f / 0.32f) },
        new() { Center = new Vector3( 0.00f, -0.68f, 0.02f), InvRadii = new Vector3(1f / 0.48f, 1f / 0.36f, 1f / 0.34f) },
    };

    SubViewport vp;
    Node3D brainRoot;
    Node3D pivot;
    Camera3D cam;
    MultiMesh mmNeurons;
    ImmediateMesh pulseLinesMesh;
    ShaderMaterial outerShellMat;
    ShaderMaterial cxRingMat;

    RichTextLabel sidebarList;
    Label modeBadgeLabel;
    Label spikeRateLabel;
    Control viewArea;

    FlyBrain boundBrain;
    Connectome connectome;
    int builtN = -1;
    int[] regionOf;
    bool[] isDescendingInterneuron;
    Vector3[] pos;
    float[] act;
    float[] buf;
    float[] baseScale;
    FiberArc[] dynamicFibers = Array.Empty<FiberArc>();

    long lastSteps;
    float yaw = 0.0f;
    float pitch = -0.12f;
    float dist = 5.15f;
    float uiUpdateTimer;
    float simTime;
    bool dragging;
    bool autoRotate = true;

    public BrainView3D()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        ClipContents = true;

        var screenBg = new PanelContainer();
        screenBg.SetAnchorsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.015f, 0.028f, 0.058f, 1f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            BorderColor = new Color(0.14f, 0.46f, 0.85f, 0.48f),
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        };
        screenBg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(screenBg);

        var mainHBox = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        mainHBox.AddThemeConstantOverride("separation", 0);
        screenBg.AddChild(mainHBox);

        // =====================================================================
        // AREA KIRI/TENGAH: 3D VIEWPORT + SCIENTIFIC HUD OVERLAYS
        // =====================================================================
        viewArea = new Control
        {
            CustomMinimumSize = new Vector2(180, 130),
            ClipContents = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.85f,
        };
        mainHBox.AddChild(viewArea);

        var vpContainer = new SubViewportContainer
        {
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        vpContainer.SetAnchorsPreset(LayoutPreset.FullRect);
        viewArea.AddChild(vpContainer);

        vp = new SubViewport
        {
            Size = new Vector2I(580, 340),
            OwnWorld3D = true,
            TransparentBg = false,
            Msaa3D = Viewport.Msaa.Msaa2X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        vpContainer.AddChild(vp);
        viewArea.GuiInput += OnViewInput;

        BuildScientificOverlays(viewArea);

        // =====================================================================
        // AREA KANAN: SIDEBAR KLUSTER NEURAL & ACTIVE/TOTAL COUNTS
        // =====================================================================
        var sidePanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(178, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        var sideStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.022f, 0.040f, 0.082f, 0.94f),
            BorderWidthLeft = 1,
            BorderColor = new Color(0.14f, 0.45f, 0.82f, 0.38f),
            ContentMarginLeft = 7,
            ContentMarginRight = 7,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        sidePanel.AddThemeStyleboxOverride("panel", sideStyle);
        mainHBox.AddChild(sidePanel);

        var sideVBox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        sideVBox.AddThemeConstantOverride("separation", 4);
        sidePanel.AddChild(sideVBox);

        var sideHeader = new Label
        {
            Text = "NEURAL CLUSTERS (ACTIVE/TOTAL)",
            ClipText = true,
        };
        sideHeader.AddThemeFontSizeOverride("font_size", 9);
        sideHeader.AddThemeColorOverride("font_color", new Color(0.38f, 0.86f, 1.0f));
        sideVBox.AddChild(sideHeader);

        var sep = new ColorRect
        {
            CustomMinimumSize = new Vector2(0, 1),
            Color = new Color(0.18f, 0.55f, 0.92f, 0.4f),
        };
        sideVBox.AddChild(sep);

        sidebarList = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Off,
        };
        sidebarList.AddThemeFontSizeOverride("normal_font_size", 9);
        sidebarList.AddThemeFontSizeOverride("bold_font_size", 9);
        sideVBox.AddChild(sidebarList);

        modeBadgeLabel = new Label
        {
            Text = "● MODE: SPONTANEOUS CONNECTOME",
            ClipText = true,
        };
        modeBadgeLabel.AddThemeFontSizeOverride("font_size", 8);
        modeBadgeLabel.AddThemeColorOverride("font_color", new Color(0.35f, 0.95f, 0.55f));
        sideVBox.AddChild(modeBadgeLabel);

        Build3DScene();
    }

    public override void _Ready()
    {
        try
        {
            connectome = BrainAssets.Connectome;
            if (connectome != null && builtN != connectome.N)
            {
                BuildConnectomeGeometry(connectome);
                SeedInitialActivity();
                UpdateVisualBuffers(0.016f, false, null);
                UpdateSidebarText();
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[BrainView3D] Gagal memuat connectome awal: {ex.Message}");
        }
    }

    void BuildScientificOverlays(Control parent)
    {
        var topOverlay = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        topOverlay.SetAnchorsPreset(LayoutPreset.TopWide);
        topOverlay.AddThemeConstantOverride("margin_left", 8);
        topOverlay.AddThemeConstantOverride("margin_top", 5);
        topOverlay.AddThemeConstantOverride("margin_right", 8);
        parent.AddChild(topOverlay);

        var topCol = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        topCol.AddThemeConstantOverride("separation", 1);
        topOverlay.AddChild(topCol);

        var topRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        topCol.AddChild(topRow);

        var titleLbl = new Label
        {
            Text = "DROSOPHILA CONNECTOME 3D VIEW",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
        };
        titleLbl.AddThemeFontSizeOverride("font_size", 9);
        titleLbl.AddThemeColorOverride("font_color", new Color(0.45f, 0.92f, 1.0f));
        titleLbl.AddThemeColorOverride("font_outline_color", new Color(0.01f, 0.05f, 0.14f, 0.95f));
        titleLbl.AddThemeConstantOverride("outline_size", 3);
        topRow.AddChild(titleLbl);

        var datasetLbl = new Label
        {
            Text = "DATASET: FAFB (FlyWire)",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        datasetLbl.AddThemeFontSizeOverride("font_size", 9);
        datasetLbl.AddThemeColorOverride("font_color", new Color(0.32f, 1.0f, 0.58f));
        datasetLbl.AddThemeColorOverride("font_outline_color", new Color(0.01f, 0.05f, 0.14f, 0.95f));
        datasetLbl.AddThemeConstantOverride("outline_size", 3);
        topRow.AddChild(datasetLbl);

        var botOverlay = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        botOverlay.SetAnchorsPreset(LayoutPreset.BottomWide);
        botOverlay.AddThemeConstantOverride("margin_left", 8);
        botOverlay.AddThemeConstantOverride("margin_bottom", 4);
        botOverlay.AddThemeConstantOverride("margin_right", 8);
        parent.AddChild(botOverlay);

        var botRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        botOverlay.AddChild(botRow);

        spikeRateLabel = new Label
        {
            Text = "2,635 LIF NEURONS | 136,357 SYNAPSES",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
        };
        spikeRateLabel.AddThemeFontSizeOverride("font_size", 8);
        spikeRateLabel.AddThemeColorOverride("font_color", new Color(0.58f, 0.78f, 0.96f, 0.85f));
        botRow.AddChild(spikeRateLabel);

        var navHintLbl = new Label
        {
            Text = "[Drag: Orbit | Scroll: Zoom]",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        navHintLbl.AddThemeFontSizeOverride("font_size", 8);
        navHintLbl.AddThemeColorOverride("font_color", new Color(0.45f, 0.65f, 0.85f, 0.75f));
        botRow.AddChild(navHintLbl);
    }

    // =========================================================================
    // CANGKANG NEUROPIL TUNGGAL BERBENTUK SAYAP ORGANIK (METABALL ISOSURFACE)
    // =========================================================================
    void Build3DScene()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.012f, 0.022f, 0.048f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.12f, 0.25f, 0.45f),
            AmbientLightEnergy = 1.0f,
            GlowEnabled = false,
        };
        vp.AddChild(new WorldEnvironment { Environment = env });

        brainRoot = new Node3D();
        vp.AddChild(brainRoot);

        pivot = new Node3D { Rotation = new Vector3(pitch, yaw, 0f) };
        vp.AddChild(pivot);

        // KeepAspect = Width memastikan bentangan sayap kiri-kanan otak selalu muat utuh di layar
        cam = new Camera3D
        {
            Position = new Vector3(0, -0.02f, dist),
            KeepAspect = Camera3D.KeepAspectEnum.Width,
            Fov = 54f,
            Near = 0.1f,
            Far = 30f,
        };
        pivot.AddChild(cam);

        var shellShader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back;
uniform vec4 inner_color : source_color = vec4(0.03, 0.12, 0.38, 0.09);
uniform vec4 rim_color : source_color = vec4(0.14, 0.58, 1.00, 0.46);
uniform float level : hint_range(0.0, 1.0) = 0.2;

void fragment() {
    float ndotv = abs(dot(NORMAL, VIEW));
    float rim = pow(clamp(1.0 - ndotv, 0.0, 1.0), 2.2);
    float boost = 1.0 + level * 0.55;
    ALBEDO = mix(inner_color.rgb, rim_color.rgb, rim) * boost;
    ALPHA = clamp((inner_color.a + rim_color.a * rim) * boost, 0.0, 0.72);
}
"
        };

        outerShellMat = new ShaderMaterial { Shader = shellShader };
        outerShellMat.SetShaderParameter("inner_color", new Color(0.03f, 0.12f, 0.38f, 0.085f));
        outerShellMat.SetShaderParameter("rim_color", new Color(0.14f, 0.56f, 1.00f, 0.44f));

        // 1. Bangun Mesh Tunggal Cangkang Neuropil Otak Lalat (Tanpa Garis Bola Bertumpuk)
        BuildUnifiedDrosophilaShell(out var shellMesh, out var wireMesh);

        brainRoot.AddChild(new MeshInstance3D
        {
            Mesh = shellMesh,
            MaterialOverride = outerShellMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        var wireMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        brainRoot.AddChild(new MeshInstance3D
        {
            Mesh = wireMesh,
            MaterialOverride = wireMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // 2. Cincin Torus Central Complex (Ellipsoid Body) di Pusat Otak
        cxRingMat = new ShaderMaterial { Shader = shellShader };
        cxRingMat.SetShaderParameter("inner_color", new Color(0.36f, 0.04f, 0.28f, 0.06f));
        cxRingMat.SetShaderParameter("rim_color", new Color(1.0f, 0.22f, 0.78f, 0.38f));

        var ebTorus = new TorusMesh { InnerRadius = 0.16f, OuterRadius = 0.32f, Rings = 32, RingSegments = 16 };
        brainRoot.AddChild(new MeshInstance3D
        {
            Mesh = ebTorus,
            MaterialOverride = cxRingMat,
            Position = new Vector3(0f, 0.04f, 0.08f),
            RotationDegrees = new Vector3(90f, 0f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // 3. Mesh Garis Serat Sinaptik Berdenyut (Pulsing Fibers)
        pulseLinesMesh = new ImmediateMesh();
        var pulseMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        brainRoot.AddChild(new MeshInstance3D
        {
            Mesh = pulseLinesMesh,
            MaterialOverride = pulseMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    static float EvalMetaballField(Vector3 p)
    {
        float sum = 0f;
        for (int i = 0; i < BrainLobes.Length; i++)
        {
            ref readonly var b = ref BrainLobes[i];
            Vector3 d = p - b.Center;

            if (b.TiltZ != 0f)
            {
                float cz = Mathf.Cos(b.TiltZ), sz = Mathf.Sin(b.TiltZ);
                float nx = d.X * cz - d.Y * sz;
                float ny = d.X * sz + d.Y * cz;
                d.X = nx; d.Y = ny;
            }
            if (b.SweepY != 0f)
            {
                float cy = Mathf.Cos(b.SweepY), sy = Mathf.Sin(b.SweepY);
                float nx = d.X * cy - d.Z * sy;
                float nz = d.X * sy + d.Z * cy;
                d.X = nx; d.Z = nz;
            }

            Vector3 q = d * b.InvRadii;
            float r2 = q.LengthSquared();
            sum += Mathf.Exp(-r2 * 1.65f);
        }
        return sum;
    }

    static Vector3 EvalMetaballNormal(Vector3 p)
    {
        const float eps = 0.02f;
        float dx = EvalMetaballField(p + new Vector3(eps, 0, 0)) - EvalMetaballField(p - new Vector3(eps, 0, 0));
        float dy = EvalMetaballField(p + new Vector3(0, eps, 0)) - EvalMetaballField(p - new Vector3(0, eps, 0));
        float dz = EvalMetaballField(p + new Vector3(0, 0, eps)) - EvalMetaballField(p - new Vector3(0, 0, eps));
        var g = new Vector3(-dx, -dy, -dz);
        return g.LengthSquared() > 1e-8f ? g.Normalized() : Vector3.Up;
    }

    static void BuildUnifiedDrosophilaShell(out ArrayMesh surfaceMesh, out ArrayMesh wireMesh)
    {
        const int U = 72;
        const int V = 38;
        const float iso = 0.22f;

        var verts = new Vector3[(U + 1) * (V + 1)];
        var norms = new Vector3[(U + 1) * (V + 1)];

        for (int iv = 0; iv <= V; iv++)
        {
            float v = (float)iv / V;
            float phi = (v - 0.5f) * Mathf.Pi;
            float cosPhi = Mathf.Cos(phi);
            float sinPhi = Mathf.Sin(phi);

            for (int iu = 0; iu <= U; iu++)
            {
                float u = (float)iu / U;
                float theta = u * Mathf.Tau;
                var dir = new Vector3(
                    Mathf.Cos(theta) * cosPhi * 2.5f,
                    sinPhi * 1.15f,
                    Mathf.Sin(theta) * cosPhi * 0.85f).Normalized();

                float lo = 0.15f, hi = 3.25f;
                for (int iter = 0; iter < 10; iter++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (EvalMetaballField(dir * mid) > iso) lo = mid;
                    else hi = mid;
                }
                Vector3 pos = dir * (0.5f * (lo + hi));
                int idx = iv * (U + 1) + iu;
                verts[idx] = pos;
                norms[idx] = EvalMetaballNormal(pos);
            }
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int iv = 0; iv < V; iv++)
        {
            for (int iu = 0; iu < U; iu++)
            {
                int i00 = iv * (U + 1) + iu;
                int i10 = i00 + 1;
                int i01 = (iv + 1) * (U + 1) + iu;
                int i11 = i01 + 1;

                st.SetNormal(norms[i00]); st.AddVertex(verts[i00]);
                st.SetNormal(norms[i10]); st.AddVertex(verts[i10]);
                st.SetNormal(norms[i11]); st.AddVertex(verts[i11]);

                st.SetNormal(norms[i00]); st.AddVertex(verts[i00]);
                st.SetNormal(norms[i11]); st.AddVertex(verts[i11]);
                st.SetNormal(norms[i01]); st.AddVertex(verts[i01]);
            }
        }
        surfaceMesh = st.Commit();

        var wst = new SurfaceTool();
        wst.Begin(Mesh.PrimitiveType.Lines);
        var wireCol = new Color(0.14f, 0.50f, 0.96f, 0.085f);
        for (int iv = 2; iv < V; iv += 3)
        {
            for (int iu = 0; iu < U; iu++)
            {
                int i0 = iv * (U + 1) + iu;
                int i1 = i0 + 1;
                wst.SetColor(wireCol); wst.AddVertex(verts[i0]);
                wst.SetColor(wireCol); wst.AddVertex(verts[i1]);
            }
        }
        for (int iu = 0; iu < U; iu += 4)
        {
            for (int iv = 0; iv < V; iv++)
            {
                int i0 = iv * (U + 1) + iu;
                int i1 = (iv + 1) * (U + 1) + iu;
                wst.SetColor(wireCol); wst.AddVertex(verts[i0]);
                wst.SetColor(wireCol); wst.AddVertex(verts[i1]);
            }
        }
        wireMesh = wst.Commit();
    }

    // =========================================================================
    // PENEMPATAN ANATOMIS 2.635 NEURON & JARINGAN SERAT SINAPTIK
    // =========================================================================
    void BuildConnectomeGeometry(Connectome c)
    {
        int n = c.N;
        builtN = n;
        regionOf = new int[n];
        isDescendingInterneuron = new bool[n];
        Array.Fill(regionOf, -1);

        int[] priorityOrder = { 5, 0, 3, 4, 1, 2 };
        foreach (int r in priorityOrder)
        {
            foreach (var g in regions[r].Groups)
            {
                foreach (int idx in c.Group(g))
                {
                    if (regionOf[idx] < 0) regionOf[idx] = r;
                }
            }
        }

        foreach (var r in regions) r.Total = 0;
        for (int i = 0; i < n; i++)
        {
            if (regionOf[i] < 0) regionOf[i] = regions.Length - 1;
            regions[regionOf[i]].Total++;

            string t = c.Types[i] ?? "";
            if (regionOf[i] == 6 && (t.StartsWith("DN") || t.StartsWith("LAL") || t.StartsWith("PS") || t.StartsWith("AN")))
                isDescendingInterneuron[i] = true;
        }

        pos = new Vector3[n];
        act = new float[n];
        baseScale = new float[n];
        buf = new float[n * FloatsPerInstance];

        var rng = new RandomNumberGenerator();
        var kcSet = new HashSet<int>(c.Group("kc"));
        var epgSet = new HashSet<int>(c.Group("in_epg"));
        var erL = new HashSet<int>(c.Group("in_er_L"));
        var erR = new HashSet<int>(c.Group("in_er_R"));

        for (int i = 0; i < n; i++)
        {
            rng.Seed = (ulong)(i * 2654435761L + 137);
            int rIdx = regionOf[i];
            string t = c.Types[i] ?? "";
            float side = c.Sides[i] == 'L' ? -1f : c.Sides[i] == 'R' ? 1f : (rng.Randf() < 0.5f ? -1f : 1f);

            pos[i] = ComputeAnatomicalNeuronPosition(
                i, rIdx, t, side, kcSet.Contains(i), epgSet.Contains(i) || erL.Contains(i) || erR.Contains(i), rng);

            baseScale[i] = rIdx switch
            {
                5 => 0.052f, // Motor descending (14 neuron)
                3 => 0.044f, // Lobus antena (16 ALPN)
                4 => 0.030f, // Pengecap SEZ (99 GRN)
                0 => 0.032f, // Mata / Lobula (314 neuron di sayap optik yang luas)
                1 => 0.022f, // Central complex (cincin kompas)
                2 => 0.022f, // Mushroom body
                _ => isDescendingInterneuron[i] ? 0.024f : 0.018f,
            };

            int o = i * FloatsPerInstance;
            buf[o + 3] = pos[i].X;
            buf[o + 7] = pos[i].Y;
            buf[o + 11] = pos[i].Z;
            buf[o] = buf[o + 5] = buf[o + 10] = baseScale[i];
        }

        var glowShader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;

void vertex() {
    float sx = length(MODEL_MATRIX[0].xyz);
    float sy = length(MODEL_MATRIX[1].xyz);
    float sz = length(MODEL_MATRIX[2].xyz);
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
        INV_VIEW_MATRIX[0] * sx,
        INV_VIEW_MATRIX[1] * sy,
        INV_VIEW_MATRIX[2] * sz,
        MODEL_MATRIX[3]
    );
}

void fragment() {
    vec2 uv = UV * 2.0 - 1.0;
    float r2 = dot(uv, uv);
    if (r2 > 1.0) discard;
    float core = exp(-r2 * 12.0);
    float halo = exp(-r2 * 3.0) * 0.48;
    float glow = core + halo;
    ALBEDO = mix(COLOR.rgb, vec3(1.0), core * 0.42);
    ALPHA = clamp(glow * COLOR.a, 0.0, 1.0);
}
"
        };

        mmNeurons = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            InstanceCount = n,
            Mesh = new QuadMesh { Size = new Vector2(2f, 2f) },
        };

        brainRoot.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mmNeurons,
            MaterialOverride = new ShaderMaterial { Shader = glowShader },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        BuildSynapticFiberNetwork(c, rng);
    }

    static Vector3 RandomInUnitBall(RandomNumberGenerator rng)
    {
        Vector3 p;
        do
        {
            p = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f));
        } while (p.LengthSquared() > 1f);
        return p;
    }

    Vector3 ComputeAnatomicalNeuronPosition(
        int idx, int rIdx, string type, float side, bool isKc, bool isRingCx, RandomNumberGenerator rng)
    {
        var ball = RandomInUnitBall(rng);

        switch (rIdx)
        {
            case 0: // Mata / Lobula (visual): Sayap optik lateral kiri & kanan (Aqua / Light Blue)
            {
                float u = rng.RandfRange(0f, 1f);
                float wingX = side * Mathf.Lerp(1.08f, 2.25f, Mathf.Pow(u, 0.68f));
                float wingSpanY = Mathf.Lerp(0.45f, 0.86f, u);
                float y = ball.Y * wingSpanY + 0.06f + 0.08f * u;
                float zCurve = -0.18f * u * u + ball.Z * 0.30f;
                return new Vector3(wingX + ball.X * 0.14f, y, zCurve);
            }

            case 1: // Central complex (compass): Cincin Torus Ellipsoid Body di pusat (Magenta/Pink)
            {
                if (isRingCx)
                {
                    float angle = (idx * 0.6180339f + rng.RandfRange(-0.06f, 0.06f)) * Mathf.Tau;
                    float ringR = 0.25f + rng.RandfRange(-0.045f, 0.045f);
                    return new Vector3(
                        Mathf.Cos(angle) * ringR,
                        0.04f + Mathf.Sin(angle) * ringR,
                        0.08f + rng.RandfRange(-0.05f, 0.05f));
                }
                else
                {
                    float archAngle = rng.RandfRange(-1.2f, 1.2f);
                    float archR = 0.33f + rng.RandfRange(-0.05f, 0.05f);
                    return new Vector3(
                        Mathf.Sin(archAngle) * archR * 1.15f,
                        0.14f + Mathf.Cos(archAngle) * 0.18f + ball.Y * 0.05f,
                        -0.05f + ball.Z * 0.08f);
                }
            }

            case 2: // Mushroom body (memori): L-shaped lobes + dorsal Calyx di tengah-atas (Kuning/Oranye)
            {
                float branch = rng.Randf();
                if (isKc && branch < 0.46f)
                {
                    return new Vector3(
                        side * (0.64f + ball.X * 0.24f),
                        0.56f + ball.Y * 0.18f,
                        -0.12f + ball.Z * 0.18f);
                }
                else if (branch < 0.75f)
                {
                    float h = rng.RandfRange(0.24f, 0.70f);
                    return new Vector3(
                        side * (0.48f + ball.X * 0.12f),
                        h,
                        0.06f + ball.Z * 0.10f);
                }
                else
                {
                    float m = rng.RandfRange(0.20f, 0.54f);
                    return new Vector3(
                        side * m,
                        0.28f + ball.Y * 0.09f,
                        0.12f + ball.Z * 0.10f);
                }
            }

            case 3: // Lobus antena (bau): Glomeruli di bagian bawah-depan (Hijau)
            {
                return new Vector3(
                    side * (0.30f + Math.Abs(ball.X) * 0.18f),
                    -0.44f + ball.Y * 0.16f,
                    0.26f + ball.Z * 0.16f);
            }

            case 4: // Pengecap (SEZ): Gnathal Ganglion di bagian bawah-tengah (Hijau)
            {
                return new Vector3(
                    side * (0.14f + Math.Abs(ball.X) * 0.28f),
                    -0.66f + ball.Y * 0.16f,
                    0.12f + ball.Z * 0.16f);
            }

            case 5: // Motor (descending): Jalur turun ke leher ventral (Merah menyala)
            {
                float t = rng.RandfRange(0f, 1f);
                return new Vector3(
                    side * Mathf.Lerp(0.26f, 0.08f, t) + ball.X * 0.06f,
                    Mathf.Lerp(-0.36f, -0.82f, t) + ball.Y * 0.05f,
                    -0.10f + ball.Z * 0.10f);
            }

            default:
            {
                if (type.StartsWith("LC") || type.StartsWith("LPLC") || type.StartsWith("LT") || type.StartsWith("Li"))
                {
                    float u = rng.RandfRange(0f, 1f);
                    return new Vector3(
                        side * Mathf.Lerp(0.95f, 2.05f, u) + ball.X * 0.14f,
                        ball.Y * Mathf.Lerp(0.42f, 0.76f, u) + 0.05f,
                        -0.08f + ball.Z * 0.24f);
                }
                if (type.StartsWith("PVLP") || type.StartsWith("AVLP") || type.StartsWith("PLP") || type.StartsWith("AOTU"))
                {
                    return new Vector3(
                        side * rng.RandfRange(0.56f, 1.38f) + ball.X * 0.12f,
                        ball.Y * 0.44f + 0.02f,
                        ball.Z * 0.28f);
                }
                if (type.StartsWith("FB") || type.StartsWith("hDelta") || type.StartsWith("vDelta") ||
                    type.StartsWith("Delta7") || type.StartsWith("PFL") || type.StartsWith("PFN") ||
                    type.StartsWith("ExR") || type.StartsWith("EL") || type.StartsWith("GLNO") || type.StartsWith("LPsP"))
                {
                    return new Vector3(
                        ball.X * 0.44f,
                        0.16f + ball.Y * 0.22f,
                        -0.06f + ball.Z * 0.18f);
                }
                if (isDescendingInterneuron[idx])
                {
                    float d = rng.RandfRange(0f, 1f);
                    return new Vector3(
                        side * Mathf.Lerp(0.48f, 0.14f, d) + ball.X * 0.10f,
                        Mathf.Lerp(-0.10f, -0.76f, d) + ball.Y * 0.08f,
                        -0.06f + ball.Z * 0.18f);
                }

                // Sebarkan sebagian interneuron hingga ke pangkal sayap optik agar transisi rapat & organik
                return new Vector3(
                    side * rng.RandfRange(0.24f, 1.45f) + ball.X * 0.14f,
                    ball.Y * 0.50f + 0.04f,
                    ball.Z * 0.30f);
            }
        }
    }

    void BuildSynapticFiberNetwork(Connectome c, RandomNumberGenerator rng)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);

        var dynList = new List<FiberArc>(380);
        int n = c.N;

        // 1. Jaringan dendritik retinotopik intra-lobus di dalam Sayap Optik & Mushroom Body
        var opticNeurons = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (regionOf[i] == 0) opticNeurons.Add(i);
        }
        for (int k = 0; k < opticNeurons.Count; k++)
        {
            int u = opticNeurons[k];
            // Hubungkan ke tetangga terdekat di sayap optik yang sama untuk membentuk jaring dendritik aqua
            int bestV = -1;
            float bestD2 = 0.32f;
            for (int m = 1; m <= 25; m++)
            {
                int v = opticNeurons[(k + m) % opticNeurons.Count];
                if (Mathf.Sign(pos[u].X) != Mathf.Sign(pos[v].X)) continue;
                float d2 = pos[u].DistanceSquaredTo(pos[v]);
                if (d2 > 0.01f && d2 < bestD2)
                {
                    bestD2 = d2;
                    bestV = v;
                }
            }
            if (bestV >= 0)
                AddSynapticFiber(st, dynList, u, bestV, rng);
        }

        // 2. Traktus aksonal nyata dari matriks CSR Connectome
        for (int pre = 0; pre < n; pre++)
        {
            int start = c.IndPtr[pre];
            int end = c.IndPtr[pre + 1];
            if (end <= start) continue;

            int bestPost = -1;
            float bestW = 0f;
            int secondPost = -1;
            float secondW = 0f;

            for (int e = start; e < end; e++)
            {
                int post = c.Indices[e];
                if (post == pre) continue;
                float w = Mathf.Abs(c.Weights[e]);
                if (w > bestW)
                {
                    secondW = bestW; secondPost = bestPost;
                    bestW = w; bestPost = post;
                }
                else if (w > secondW && regionOf[post] != regionOf[pre])
                {
                    secondW = w; secondPost = post;
                }
            }

            if (bestPost >= 0 && (pre % 2 == 0 || regionOf[pre] != 6))
                AddSynapticFiber(st, dynList, pre, bestPost, rng);

            if (secondPost >= 0 && regionOf[pre] != regionOf[secondPost] && pre % 3 == 0)
                AddSynapticFiber(st, dynList, pre, secondPost, rng);
        }

        dynamicFibers = dynList.ToArray();

        var staticFiberMesh = st.Commit();
        var staticFiberMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        brainRoot.AddChild(new MeshInstance3D
        {
            Mesh = staticFiberMesh,
            MaterialOverride = staticFiberMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    void AddSynapticFiber(SurfaceTool st, List<FiberArc> dynList, int pre, int post, RandomNumberGenerator rng)
    {
        Vector3 p0 = pos[pre];
        Vector3 p3 = pos[post];
        float d = p0.DistanceTo(p3);
        if (d < 0.08f || d > 3.5f) return;

        int rPre = regionOf[pre];
        int rPost = regionOf[post];

        Vector3 mid = (p0 + p3) * 0.5f;
        Vector3 bend = new Vector3(
            mid.X * 0.76f,
            mid.Y + Mathf.Clamp(d * 0.12f, 0.03f, 0.22f) * (rPre == 3 ? 1.15f : 0.55f),
            mid.Z * 0.78f + rng.RandfRange(-0.05f, 0.05f));

        Vector3 p1 = p0.Lerp(bend, 0.55f);
        Vector3 p2 = p3.Lerp(bend, 0.55f);

        Color cPre = GetDisplayNeuronColor(pre, 0.2f);
        Color cPost = GetDisplayNeuronColor(post, 0.2f);
        float alpha = rPre != rPost ? 0.065f : 0.038f;

        var col0 = new Color(cPre.R, cPre.G, cPre.B, alpha);
        var col1 = new Color((cPre.R + cPost.R) * 0.5f, (cPre.G + cPost.G) * 0.5f, (cPre.B + cPost.B) * 0.5f, alpha * 1.15f);
        var col2 = new Color(cPost.R, cPost.G, cPost.B, alpha);

        st.SetColor(col0); st.AddVertex(p0);
        st.SetColor(col1); st.AddVertex(p1);
        st.SetColor(col1); st.AddVertex(p1);
        st.SetColor(col1); st.AddVertex(p2);
        st.SetColor(col1); st.AddVertex(p2);
        st.SetColor(col2); st.AddVertex(p3);

        if (dynList.Count < 320 && (rPre != rPost || rPre == 1 || rPre == 5 || rng.Randf() < 0.16f))
        {
            dynList.Add(new FiberArc
            {
                Pre = pre,
                Post = post,
                P0 = p0, P1 = p1, P2 = p2, P3 = p3,
                BaseColor = cPre.Lerp(cPost, 0.5f),
            });
        }
    }

    Color GetDisplayNeuronColor(int idx, float activity)
    {
        int rIdx = regionOf[idx];
        if (rIdx == 6 && isDescendingInterneuron[idx])
        {
            var baseDesc = new Color(0.96f, 0.26f, 0.32f);
            return baseDesc.Lerp(new Color(1f, 0.78f, 0.78f), 0.35f * activity);
        }
        var c = regions[rIdx].Color;
        return c.Lerp(Colors.White, 0.28f * activity);
    }

    public void Bind(FlyBrain b)
    {
        boundBrain = b;
        if (b != null)
        {
            connectome = b.State.C;
            if (builtN != connectome.N) BuildConnectomeGeometry(connectome);
            lastSteps = b.State.TotalSteps;
            modeBadgeLabel.Text = "● MODE: LIVE NPC CONNECTOME";
            modeBadgeLabel.AddThemeColorOverride("font_color", new Color(0.28f, 1.0f, 0.58f));
        }
        else
        {
            modeBadgeLabel.Text = "● MODE: SPONTANEOUS CONNECTOME";
            modeBadgeLabel.AddThemeColorOverride("font_color", new Color(0.42f, 0.86f, 1.0f));
        }
    }

    void SeedInitialActivity()
    {
        if (act == null) return;
        var rng = new RandomNumberGenerator { Seed = 20261019UL };
        for (int i = 0; i < act.Length; i++)
        {
            int r = regionOf[i];
            float p = r switch
            {
                1 => 0.68f, // Central complex (compass): ~249/367
                0 => 0.16f, // Mata/Lobula (visual): ~51/314
                2 => 0.22f, // Mushroom body (memori)
                3 => 0.42f, // Lobus antena (bau)
                4 => 0.26f, // Pengecap (SEZ)
                5 => 0.08f, // Motor (descending): ~1/14
                _ => 0.15f,
            };
            act[i] = rng.Randf() < p ? rng.RandfRange(0.35f, 0.98f) : rng.RandfRange(0.0f, 0.16f);
        }
    }

    void StepSpontaneousActivity(float dt)
    {
        simTime += dt;
        float compassPhase = simTime * 1.6f;
        float visualWave = Mathf.Sin(simTime * 2.1f);
        uint frameHash = (uint)(simTime * 60f) * 747796405u + 2891336453u;

        for (int i = 0; i < act.Length; i++)
        {
            int r = regionOf[i];
            uint h = (uint)i * 2654435761u ^ frameHash;
            float rand01 = (h & 0xFFFFu) / 65535f;

            if (r == 1)
            {
                float ang = Mathf.Atan2(pos[i].Y - 0.04f, pos[i].X);
                float diff = Mathf.Cos(ang - compassPhase);
                if (diff > -0.35f && rand01 < 0.25f)
                    act[i] = Mathf.Max(act[i], 0.48f + 0.50f * Mathf.Max(0f, diff));
            }
            else if (r == 0)
            {
                bool activeSide = (pos[i].X > 0 && visualWave >= -0.25f) || (pos[i].X < 0 && visualWave <= 0.25f);
                if (activeSide && rand01 < 0.036f)
                    act[i] = 0.95f;
            }
            else if (r == 5)
            {
                if (rand01 < 0.012f) act[i] = 0.95f;
            }
            else if (r == 2)
            {
                if (rand01 < 0.035f) act[i] = 0.5f + 0.5f * ((h & 0xFFu) / 255f);
            }
            else if (r == 3 || r == 4)
            {
                if (rand01 < 0.045f) act[i] = 0.9f;
            }
            else
            {
                if (rand01 < 0.022f) act[i] = 0.85f;
            }
        }
    }

    void OnViewInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                dragging = mb.Pressed;
                if (mb.Pressed && mb.DoubleClick) autoRotate = !autoRotate;
                else if (mb.Pressed) autoRotate = false;
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                dist = Mathf.Clamp(dist - 0.30f, 2.4f, 8.5f);
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                dist = Mathf.Clamp(dist + 0.30f, 2.4f, 8.5f);
            }
        }
        else if (e is InputEventMouseMotion mmMotion && dragging)
        {
            yaw -= mmMotion.Relative.X * 0.01f;
            pitch = Mathf.Clamp(pitch - mmMotion.Relative.Y * 0.01f, -1.25f, 1.25f);
        }
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || mmNeurons == null) return;
        float dt = Mathf.Min((float)delta, 0.05f);

        if (autoRotate)
        {
            yaw = Mathf.Sin(simTime * 0.45f) * 0.22f;
            pitch = -0.10f + Mathf.Cos(simTime * 0.32f) * 0.05f;
        }

        pivot.Rotation = new Vector3(pitch, yaw, 0f);
        cam.Position = new Vector3(0f, -0.02f, dist);

        bool fresh = false;
        int[] counts = null;
        if (boundBrain != null)
        {
            long steps = boundBrain.State.TotalSteps;
            counts = boundBrain.CaptureCounts;
            fresh = steps != lastSteps && counts != null;
            lastSteps = steps;
        }
        else
        {
            StepSpontaneousActivity(dt);
        }

        UpdateVisualBuffers(dt, fresh, counts);

        uiUpdateTimer -= dt;
        if (uiUpdateTimer <= 0f)
        {
            uiUpdateTimer = 0.15f;
            UpdateSidebarText();
        }
    }

    void UpdateVisualBuffers(float dt, bool fresh, int[] counts)
    {
        float decay = dt / DecaySeconds;
        foreach (var r in regions) r.Active = 0;

        int totalActive = 0;
        for (int i = 0; i < act.Length; i++)
        {
            float a = act[i] - decay;
            if (fresh && counts[i] > 0) a = 1f;
            if (a < 0f) a = 0f;
            act[i] = a;

            int rIdx = regionOf[i];
            if (a > 0.24f)
            {
                regions[rIdx].Active++;
                totalActive++;
            }

            int o = i * FloatsPerInstance;
            float s = baseScale[i] * (0.86f + 1.15f * a);
            buf[o] = buf[o + 5] = buf[o + 10] = s;

            var col = GetDisplayNeuronColor(i, a);
            buf[o + 12] = col.R;
            buf[o + 13] = col.G;
            buf[o + 14] = col.B;
            buf[o + 15] = 0.30f + 0.70f * a;
        }
        mmNeurons.Buffer = buf;

        float globalLevel = Mathf.Clamp((float)totalActive / Math.Max(1, builtN) * 1.8f, 0.1f, 0.9f);
        outerShellMat?.SetShaderParameter("level", globalLevel);
        float cxLevel = regions[1].Total > 0 ? Mathf.Clamp((float)regions[1].Active / regions[1].Total, 0.1f, 1f) : 0.2f;
        cxRingMat?.SetShaderParameter("level", cxLevel);

        pulseLinesMesh.ClearSurfaces();
        if (dynamicFibers.Length > 0)
        {
            pulseLinesMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            bool anyLine = false;
            float pulseWave = 0.65f + 0.35f * Mathf.Sin(simTime * 6.5f);

            for (int f = 0; f < dynamicFibers.Length; f++)
            {
                ref readonly var arc = ref dynamicFibers[f];
                float aPre = act[arc.Pre];
                float aPost = act[arc.Post];
                float energy = Mathf.Max(aPre, aPost * 0.85f);
                if (energy < 0.28f) continue;

                anyLine = true;
                float alpha = Mathf.Clamp(energy * 0.26f * pulseWave, 0.05f, 0.36f);
                var cHot = arc.BaseColor.Lerp(Colors.White, 0.32f * energy);
                var colEnd = new Color(cHot.R, cHot.G, cHot.B, alpha * 0.6f);
                var colMid = new Color(cHot.R, cHot.G, cHot.B, alpha);

                pulseLinesMesh.SurfaceSetColor(colEnd); pulseLinesMesh.SurfaceAddVertex(arc.P0);
                pulseLinesMesh.SurfaceSetColor(colMid); pulseLinesMesh.SurfaceAddVertex(arc.P1);
                pulseLinesMesh.SurfaceSetColor(colMid); pulseLinesMesh.SurfaceAddVertex(arc.P1);
                pulseLinesMesh.SurfaceSetColor(colMid); pulseLinesMesh.SurfaceAddVertex(arc.P2);
                pulseLinesMesh.SurfaceSetColor(colMid); pulseLinesMesh.SurfaceAddVertex(arc.P2);
                pulseLinesMesh.SurfaceSetColor(colEnd); pulseLinesMesh.SurfaceAddVertex(arc.P3);
            }

            if (!anyLine)
            {
                pulseLinesMesh.SurfaceSetColor(new Color(0, 0, 0, 0));
                pulseLinesMesh.SurfaceAddVertex(Vector3.Zero);
                pulseLinesMesh.SurfaceAddVertex(Vector3.Zero);
            }
            pulseLinesMesh.SurfaceEnd();
        }

        spikeRateLabel.Text = $"ACTIVE: {totalActive}/{builtN} LIF | SYNAPSES: 136,357";
    }

    void UpdateSidebarText()
    {
        var sb = new StringBuilder();
        foreach (var r in regions)
        {
            string hex = r.Color.ToHtml(false);
            sb.Append($"[color=#{hex}]●[/color] [color=#d8ecff]{r.Name}:[/color] [b][color=#{hex}]{r.Active}/{r.Total}[/color][/b]\n");
        }
        sidebarList.Text = sb.ToString().TrimEnd();
    }
}
