using System.Collections.Generic;
using System.Linq;
using Godot;
using OtakLalat.Brain.Core;
using OtakLalat.Npc;
using OtakLalat.World;

namespace OtakLalat.UI;

/// <summary>
/// Overlay evaluasi interaktif: kontrol kamera (F1/F2/F3), skenario uji satu-klik, statistik komparatif Otak vs FSM,
/// serta inspeksi NPC terpilih (slider 6 knob sifat langsung & raster spike neuron).
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    MapBase arena;
    Label info, statsLabel, detail;
    VBoxContainer panel;
    readonly List<HSlider> sliders = new();
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
        // Panel Kiri-Atas: Kontrol Kamera, Skenario Uji Satu-Klik & Statistik Komparatif Real-Time
        var leftBg = new PanelContainer { Position = new Vector2(10, 10), CustomMinimumSize = new Vector2(460, 0) };
        var leftBox = new VBoxContainer();
        leftBg.AddChild(leftBox);
        AddChild(leftBg);

        info = new Label();
        info.AddThemeColorOverride("font_shadow_color", Colors.Black);
        leftBox.AddChild(info);

        // Baris Tombol Kamera (F1 / F2 / F3) & Layar Penuh
        var camRow = new HBoxContainer();
        AddBtn(camRow, "F1: Pemain (FPS)", () => arena.PlayerNode?.SetCameraMode(Player.CamMode.FirstPerson));
        AddBtn(camRow, "F2: Kamera Atas", () => arena.PlayerNode?.SetCameraMode(Player.CamMode.TopDown));
        AddBtn(camRow, "F3: Ikuti NPC", () =>
        {
            if (selected == null) Select(NpcBase.All.OfType<BrainNpc>().FirstOrDefault() ?? NpcBase.All.FirstOrDefault());
            arena.PlayerNode?.SetCameraMode(Player.CamMode.FollowNpc, selected);
        });
        AddBtn(camRow, "⛶ Layar Penuh (F11)", ToggleFullscreen);
        leftBox.AddChild(camRow);

        // Baris Navigasi Map (Ctrl+1..4)
        string current = arena.SceneFilePath;
        var mapRow = new HBoxContainer();
        mapRow.AddChild(new Label { Text = "Map:" });
        for (int i = 0; i < MapRegistry.Maps.Length; i++)
        {
            var (title, path) = MapRegistry.Maps[i];
            AddBtn(mapRow, $"{i + 1} {title}", () => GoToMap(path), disabled: path == current);
        }
        leftBox.AddChild(mapRow);
        leftBox.AddChild(new Label { Text = $"Map aktif: {arena.MapTitle}" });

        // Baris Spawn Manual
        var spawnRow = new HBoxContainer();
        typeOpt = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (var t in MapBase.TypeIds) typeOpt.AddItem(t);
        methodOpt = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        methodOpt.AddItem("Otak"); methodOpt.AddItem("FSM");
        spawnRow.AddChild(new Label { Text = "Spawn:" });
        spawnRow.AddChild(typeOpt);
        spawnRow.AddChild(methodOpt);
        AddBtn(spawnRow, "Spawn", SpawnAtScreenCenter);
        AddBtn(spawnRow, "Hapus semua", () => { Select(null); arena.ClearNpcs(); });
        leftBox.AddChild(spawnRow);
        var clickChk = new CheckButton { Text = "Klik lantai untuk spawn", FocusMode = Control.FocusModeEnum.None };
        clickChk.Toggled += on => clickSpawn = on;
        leftBox.AddChild(clickChk);

        // Baris Tombol Skenario Uji (hanya yang relevan untuk map ini)
        var scenRow = new HBoxContainer();
        if (arena.Stats.HasFlag(MapStats.Escape)) AddBtn(scenRow, "🦅 Uji Looming", TriggerLoomingTest);
        if (arena.Stats.HasFlag(MapStats.Food) || arena.Stats.HasFlag(MapStats.Poison)) AddBtn(scenRow, "🍎 Uji Makan vs Racun", TriggerFeedingTest);
        if (arena.Stats.HasFlag(MapStats.Danger)) AddBtn(scenRow, "⚡ Uji Belajar Bahaya", TriggerDangerLearningTest);
        if (scenRow.GetChildCount() > 0) leftBox.AddChild(scenRow);

        statsLabel = new Label();
        statsLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.95f, 1f));
        leftBox.AddChild(statsLabel);

        // Panel Kanan-Atas: Inspeksi NPC Terpilih (Slider 6 Knob Sifat & Raster Spike)
        panel = new VBoxContainer { Visible = false, CustomMinimumSize = new Vector2(360, 0) };
        var bg = new PanelContainer { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -380, OffsetTop = 10, OffsetRight = -10 };
        bg.AddChild(panel);
        AddChild(bg);
        detail = new Label();
        panel.AddChild(detail);
        for (int i = 0; i < TraitNames.Length; i++)
        {
            int k = i;
            panel.AddChild(new Label { Text = TraitNames[i] });
            var s = new HSlider { MinValue = 0, MaxValue = 2, Step = 0.05, CustomMinimumSize = new Vector2(340, 16) };
            s.ValueChanged += v => SetTrait(k, (float)v);
            sliders.Add(s);
            panel.AddChild(s);
        }
        raster = new SpikeRaster { CustomMinimumSize = new Vector2(340, 200) };
        panel.AddChild(new Label { Text = "Raster spike (atas: input, tengah: KC/MBON/CX, bawah: output)" });
        panel.AddChild(raster);
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
        // Tempatkan NPC di tepi zona bahaya menghadap ke pusat zona agar penguji mengamati sengatan & penghindaran
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
        panel.Visible = n != null;
        foreach (var s in sliders) s.Visible = n is BrainNpc;
        raster.Visible = n is BrainNpc;
        if (n is BrainNpc b)
        {
            b.Brain.CaptureCounts = new int[b.Brain.State.C.N];
            raster.Bind(b.Brain);
            var t = b.Traits;
            float[] vals = { t.Fearfulness, t.Appetite, t.Pickiness, t.Curiosity, t.LearningRate, t.NeuralNoise };
            for (int i = 0; i < vals.Length; i++) sliders[i].SetValueNoSignal(vals[i]);
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
        var brainList = NpcBase.All.OfType<BrainNpc>().ToList();
        var fsmList = NpcBase.All.OfType<BaselineNpc>().ToList();
        string camName = arena.PlayerNode?.Mode switch
        {
            Player.CamMode.FirstPerson => "F1: Pemain (FPS)",
            Player.CamMode.FollowNpc => "F3: Ikuti NPC",
            _ => "F2: Kamera Atas",
        };
        info.Text = $"FPS {Engine.GetFramesPerSecond()}  |  Kamera: {camName}  |  LOD {(arena.Ai.LodEnabled ? "ON" : "OFF")}\n" +
                    $"AI Tick: {arena.Ai.LastDecideWallMs:F2} ms wall ({arena.Ai.LastDecideCpuMs:F2} ms CPU)\n" +
                    "WASD gerak/geser, Scroll zoom (F2), Klik kanan+geser putar kamera, Klik kiri pilih NPC\n" +
                    "F taruh makanan, Ctrl+1-4 pindah map, L toggle LOD, Tab label";

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
        var lines = new List<string> { $"── {arena.MapTitle}: 🧠 Otak ({brainList.Count}) vs ⚙️ FSM ({fsmList.Count}) ──" };
        if (st.HasFlag(MapStats.Food)) lines.Add($"Makan Manis : 🧠 {bFood:F1}s  |  ⚙️ {fFood:F1}s");
        if (st.HasFlag(MapStats.Poison)) lines.Add($"Gigit Racun : 🧠 {bPois:F1}x  |  ⚙️ {fPois:F1}x");
        if (st.HasFlag(MapStats.Escape)) lines.Add($"Kabur Looming : 🧠 {bEsc:F1}x  |  ⚙️ {fEsc:F1}x");
        if (st.HasFlag(MapStats.Danger)) lines.Add($"Di Zona Bahaya: 🧠 {bDang:F1}s  |  ⚙️ {fDang:F1}s");
        if (st.HasFlag(MapStats.Explore)) lines.Add($"Jarak Tempuh : 🧠 {bDist:F0}m  |  ⚙️ {fDist:F0}m");
        statsLabel.Text = string.Join("\n", lines);

        if (selected != null && !IsInstanceValid(selected)) Select(null);
        if (selected == null) return;
        var c = selected.Command;
        string txt = $"{selected.TypeName} [{selected.AiMethod}]  {selected.StatusText}\n" +
                     $"makan {selected.Metrics.FoodSeconds:F1}s  racun {selected.Metrics.PoisonBites}  disakiti {selected.Metrics.Punishments}  kabur {selected.Metrics.EscapeEvents}\n" +
                     $"maju {c.Forward:F1} belok {c.Turn:+0.00;-0.00} valensi {c.Valence:+0.00;-0.00}";
        if (selected is BrainNpc b)
        {
            var d = b.Brain.Decoder;
            txt += $"\nGF {d.Gf:F0}Hz  loomL {d.LoomL:F0} loomR {d.LoomR:F0}  MN9 {d.Mn9:F0}\n" +
                   $"DNa L {d.TurnL:F0} R {d.TurnR:F0}  MBON+ {d.Approach:F1} MBON- {d.Avoid:F1}\n" +
                   $"memori KC->MBON {Plasticity.MeanRelativeWeight(b.Brain.State):P1}  {b.DecideMs:F2} ms/tick  LOD x{b.BrainTimeScale}";
            raster.Push();
        }
        detail.Text = txt;
    }
}
