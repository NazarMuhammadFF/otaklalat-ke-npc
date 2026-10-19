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
    Arena arena;
    Label info, statsLabel, detail;
    VBoxContainer panel;
    readonly List<HSlider> sliders = new();
    SpikeRaster raster;
    NpcBase selected;
    bool labels = true;
    static readonly string[] TraitNames = { "Fearfulness", "Appetite", "Pickiness", "Curiosity", "LearningRate", "NeuralNoise" };

    public DebugOverlay() { }
    public DebugOverlay(Arena arena) => this.arena = arena;

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

        // Baris Tombol Kamera (F1 / F2 / F3)
        var camRow = new HBoxContainer();
        AddBtn(camRow, "F1: Pemain (FPS)", () => arena.PlayerNode?.SetCameraMode(Player.CamMode.FirstPerson));
        AddBtn(camRow, "F2: Kamera Atas (4 Kuadran)", () => arena.PlayerNode?.SetCameraMode(Player.CamMode.TopDown));
        AddBtn(camRow, "F3: Ikuti NPC", () =>
        {
            if (selected == null) Select(NpcBase.All.OfType<BrainNpc>().FirstOrDefault() ?? NpcBase.All.FirstOrDefault());
            arena.PlayerNode?.SetCameraMode(Player.CamMode.FollowNpc, selected);
        });
        leftBox.AddChild(camRow);

        // Baris Tombol Skenario Uji Satu-Klik
        var scenRow = new HBoxContainer();
        AddBtn(scenRow, "🦅 Uji Looming", TriggerLoomingTest);
        AddBtn(scenRow, "🍎 Uji Makan vs Racun", TriggerFeedingTest);
        AddBtn(scenRow, "⚡ Uji Belajar Bahaya", TriggerDangerLearningTest);
        leftBox.AddChild(scenRow);

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

    static void AddBtn(Container parent, string text, System.Action onClick)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += onClick;
        parent.AddChild(b);
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
            if (idx >= 0 && idx < Arena.TypeIds.Length)
            {
                if (k.ShiftPressed) arena.SpawnFsm(Arena.TypeIds[idx], arena.RandomSpot());
                else arena.SpawnBrain(Arena.TypeIds[idx], arena.RandomSpot());
            }
            else if (k.Keycode == Key.L) arena.Ai.LodEnabled = !arena.Ai.LodEnabled;
            else if (k.Keycode == Key.Tab)
            {
                labels = !labels;
                foreach (var n in NpcBase.All) n.ShowLabel = labels;
                foreach (var z in arena.ZoneLabels) z.Visible = labels;
            }
            else if (k.Keycode == Key.Escape) Select(null);
        }
    }

    public override void _Process(double delta)
    {
        var brainList = NpcBase.All.OfType<BrainNpc>().ToList();
        var fsmList = NpcBase.All.OfType<BaselineNpc>().ToList();
        string camName = arena.PlayerNode?.Mode switch
        {
            Player.CamMode.FirstPerson => "F1: Pemain (FPS)",
            Player.CamMode.FollowNpc => "F3: Ikuti NPC",
            _ => "F2: Kamera Atas (4 Kuadran)",
        };
        info.Text = $"FPS {Engine.GetFramesPerSecond()}  |  Kamera: {camName}  |  LOD {(arena.Ai.LodEnabled ? "ON" : "OFF")}\n" +
                    $"AI Tick: {arena.Ai.LastDecideWallMs:F2} ms wall ({arena.Ai.LastDecideCpuMs:F2} ms CPU)\n" +
                    "WASD gerak/geser, Scroll zoom (F2), Klik kanan+geser putar kamera, Klik kiri pilih NPC\n" +
                    "F taruh makanan, 1-4 spawn Otak (Shift+1-4 FSM), L toggle LOD, Tab label";

        // Ringkasan Statistik Komparatif Real-Time (Otak Connectome vs FSM)
        float bFood = brainList.Count > 0 ? brainList.Average(x => x.Metrics.FoodSeconds) : 0f;
        float fFood = fsmList.Count > 0 ? fsmList.Average(x => x.Metrics.FoodSeconds) : 0f;
        float bEsc = brainList.Count > 0 ? (float)brainList.Average(x => x.Metrics.EscapeEvents) : 0f;
        float fEsc = fsmList.Count > 0 ? (float)fsmList.Average(x => x.Metrics.EscapeEvents) : 0f;
        float bPois = brainList.Count > 0 ? (float)brainList.Average(x => x.Metrics.PoisonBites) : 0f;
        float fPois = fsmList.Count > 0 ? (float)fsmList.Average(x => x.Metrics.PoisonBites) : 0f;
        float bDang = brainList.Count > 0 ? brainList.Average(x => x.Metrics.TimeInDanger) : 0f;
        float fDang = fsmList.Count > 0 ? fsmList.Average(x => x.Metrics.TimeInDanger) : 0f;
        statsLabel.Text =
            $"── Skor Rata-Rata Populasi: 🧠 Otak ({brainList.Count}) vs ⚙️ FSM ({fsmList.Count}) ──\n" +
            $"Makan Manis : 🧠 {bFood:F1}s  |  ⚙️ {fFood:F1}s     Kabur Looming : 🧠 {bEsc:F1}x  |  ⚙️ {fEsc:F1}x\n" +
            $"Gigit Racun : 🧠 {bPois:F1}x  |  ⚙️ {fPois:F1}x     Di Zona Bahaya: 🧠 {bDang:F1}s  |  ⚙️ {fDang:F1}s";

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
