using System.Collections.Generic;
using Godot;
using OtakLalat.Brain;
using OtakLalat.Brain.Core;
using OtakLalat.World;

namespace OtakLalat.Npc;

/// <summary>Statistik perilaku per NPC (untuk benchmark).</summary>
public sealed class NpcMetrics
{
    public float FoodSeconds, TimeInDanger, Distance;
    public int PoisonBites, PredatorHits, DangerEntries, EscapeEvents, Punishments, HumanTouches;
    public readonly List<float> HumanFleeDist = new();   // jarak ke pemain saat refleks kabur dimulai (ukuran terukur Fearfulness)
    public readonly List<(float t, float x, float z, int action, float valence)> Path = new();
    public readonly Dictionary<int, float> ActionTime = new();   // aksi -> detik (untuk entropi)
}

/// <summary>
/// Tubuh NPC yang SAMA untuk kedua metode: indera, gerak, makan, lapar, rasa sakit.
/// Yang berbeda hanya <see cref="Decide"/> (otak lalat vs FSM).
/// </summary>
public abstract partial class NpcBase : CharacterBody3D
{
    public static readonly List<NpcBase> All = new();

    public const float WalkSpeed = 2.2f, MaxTurnRate = 3f, Gravity = 9.8f;
    const float AvoidHoldSec = 0.5f;
    const float LoomRefRadPerS = 0.3f, TouchRange = 1.0f, AntennaOffset = 0.45f, OdorThreshold = 0.05f;

    public float Hunger = 0.4f;
    public SensorFrame Frame;
    /// <summary>Jalur penglihatan looming ke otak (variabel eksperimen). Hanya NPC Otak yang memakainya; FSM tetap membaca LoomL/R.</summary>
    public VisionMode Vision = VisionMode.Scalar;
    /// <summary>FOV total gabungan kedua mata (derajat); 360 = tanpa titik buta. Hanya dipakai jalur retina.</summary>
    public float FovDeg = 360f;
    float[] loomBinsL, loomBinsR;
    public MotorCommand Command;
    public readonly NpcMetrics Metrics = new();
    public abstract string TypeName { get; }
    public abstract string AiMethod { get; }
    public abstract Color BodyColor { get; }
    public float DecideMs;           // waktu CPU Decide terakhir (profiling)
    public float BrainTimeScale = 1f;

    float punishTimer, yawRate, pathTimer, simTime, avoidTimer, avoidDir;
    bool wasEscaping, wasInDanger;
    Edible touching;
    readonly Dictionary<ILooming, float> lastTheta = new();
    Label3D label;
    RayCast3D rayL, rayR, rayC;
    AnimationPlayer animPlayer;
    string currentAnim = "";

    static readonly Dictionary<string, PackedScene> ModelCache = new();

    static (string path, float scale) GetModelSpec(string typeName) => typeName.ToLowerInvariant() switch
    {
        "rusa" => ("res://assets/models/animals/Deer.gltf", 0.35f),
        "sapi" => ("res://assets/models/animals/Cow.gltf", 0.26f),
        "rubah" => ("res://assets/models/animals/Fox.gltf", 0.30f),
        "keledai" => ("res://assets/models/animals/Donkey.gltf", 0.34f),
        // Fallbacks kompatibilitas lama
        "babi" => ("res://assets/models/animals/Cow.gltf", 0.26f),
        "kucing" => ("res://assets/models/animals/Fox.gltf", 0.30f),
        "penduduk" => ("res://assets/models/animals/Donkey.gltf", 0.34f),
        _ => ("res://assets/models/animals/Deer.gltf", 0.34f),
    };

    /// <summary>Keputusan AI. Dipanggil paralel (thread-safe: hanya membaca Frame & state sendiri).</summary>
    public abstract MotorCommand Decide(float dt);
    protected abstract float HungerRate { get; }
    public virtual string StatusText => "";

    public override void _EnterTree() => All.Add(this);
    public override void _ExitTree() => All.Remove(this);

    public override void _Ready()
    {
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.2f }, Position = new Vector3(0, 0.6f, 0) });

        // 1. Muat model 3D hewan sesuai tabel pemetaan AGENTS.md (fallback ke kapsul jika gagal/headless tanpa aset)
        if (!TryBuildAnimalModel())
        {
            var mat = new StandardMaterial3D { AlbedoColor = BodyColor };
            AddChild(new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.2f }, MaterialOverride = mat,
                Position = new Vector3(0, 0.6f, 0), RotationDegrees = new Vector3(90, 0, 0),
            });
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.3f, 0.3f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = BodyColor.Darkened(0.4f) },
                Position = new Vector3(0, 0.75f, -0.65f),
            });
        }

        // 2. Cincin aura kaki pembeda metode AI: Biru bercahaya = Otak Connectome, Kuning/Oranye = FSM
        bool isBrain = AiMethod == "otak";
        var ringColor = isBrain ? new Color(0.15f, 0.65f, 1.0f, 0.85f) : new Color(0.95f, 0.70f, 0.15f, 0.85f);
        var ringMat = new StandardMaterial3D
        {
            AlbedoColor = ringColor,
            EmissionEnabled = true,
            Emission = ringColor,
            EmissionEnergyMultiplier = 1.2f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.46f, OuterRadius = 0.60f, Rings = 24, RingSegments = 12 },
            MaterialOverride = ringMat,
            Position = new Vector3(0, 0.03f, 0),
            Scale = new Vector3(1f, 0.2f, 1f),
        });

        // 3. Label3D di atas kepala untuk nama, metode AI, dan emotikon status reaktif
        label = new Label3D
        {
            Position = new Vector3(0, 1.85f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 34,
            OutlineSize = 10,
            PixelSize = 0.0055f,
        };
        AddChild(label);
        rayL = MakeRay(0.5f); rayR = MakeRay(-0.5f); rayC = MakeRay(0f);
        FloorSnapLength = 0.3f;
        // NPC tidak saling bertabrakan (hindari deadlock berhadapan); tetap bertabrakan dengan dunia (layer 1)
        CollisionLayer = 2;
        CollisionMask = 1;
    }

    bool TryBuildAnimalModel()
    {
        var (path, scale) = GetModelSpec(TypeName);
        if (!ModelCache.TryGetValue(path, out var scene))
        {
            scene = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
            ModelCache[path] = scene;
        }
        if (scene == null) return false;
        var model = scene.Instantiate<Node3D>();
        // Model Quaternius glTF menghadap +Z; putar 180 derajat agar menghadap Forward (-Z)
        model.RotationDegrees = new Vector3(0, 180f, 0);
        model.Scale = new Vector3(scale, scale, scale);
        AddChild(model);

        animPlayer = model.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
        if (animPlayer != null)
        {
            foreach (var animName in animPlayer.GetAnimationList())
            {
                var anim = animPlayer.GetAnimation(animName);
                if (anim != null && (animName == "Idle" || animName == "Walk" || animName == "Gallop" || animName == "Eating"))
                    anim.LoopMode = Animation.LoopModeEnum.Linear;
            }
            PlayAnim("Idle", 1f);
        }
        return true;
    }

    void PlayAnim(string name, float speed)
    {
        if (animPlayer == null || !animPlayer.HasAnimation(name)) return;
        animPlayer.SpeedScale = speed;
        if (currentAnim == name && animPlayer.IsPlaying()) return;
        currentAnim = name;
        animPlayer.Play(name, customBlend: 0.2);
    }

    RayCast3D MakeRay(float angle)
    {
        var r = new RayCast3D { Position = new Vector3(0, 0.5f, 0), TargetPosition = new Vector3(Mathf.Sin(angle), 0, -Mathf.Cos(angle)) * 1.8f, ExcludeParent = true, CollisionMask = 1 };
        AddChild(r);
        return r;
    }

    Vector3 Forward => -GlobalBasis.Z;
    Vector3 Left => -GlobalBasis.X;

    float Bearing(Vector3 p)
    {
        var d = p - GlobalPosition;
        return Mathf.Atan2(d.Dot(Left), d.Dot(Forward));
    }

    // ---------------- indera (main thread) ----------------
    public void Sense(float dt)
    {
        var f = new SensorFrame { Hunger = Hunger, YawRate = yawRate };
        var head = GlobalPosition + new Vector3(0, 0.8f, 0);
        bool retina = Vision == VisionMode.Retinotopic && BrainAssets.Connectome.RetinaSectors > 0;
        if (retina)
        {
            int k = BrainAssets.Connectome.RetinaSectors;
            loomBinsL ??= new float[k]; loomBinsR ??= new float[k];
            System.Array.Clear(loomBinsL); System.Array.Clear(loomBinsR);
        }

        foreach (var l in WorldRegistry.Loomers)
        {
            float d = Mathf.Max(0.1f, head.DistanceTo(l.LoomPosition));
            float theta = 2f * Mathf.Atan(l.LoomRadius / d);
            float prev = lastTheta.TryGetValue(l, out var p) ? p : theta;
            lastTheta[l] = theta;
            float s = Mathf.Clamp((theta - prev) / dt / LoomRefRadPerS, 0f, 1f);
            if (s <= 0) continue;
            float b = Bearing(l.LoomPosition);
            if (b > -Retina.FrontOverlap) f.LoomL = Mathf.Max(f.LoomL, s);
            if (b < Retina.FrontOverlap) f.LoomR = Mathf.Max(f.LoomR, s);
            if (retina) Retina.Add(b, theta * 0.5f, s, loomBinsL, loomBinsR, Mathf.DegToRad(FovDeg));
        }
        if (retina) { f.LoomBinsL = loomBinsL; f.LoomBinsR = loomBinsR; }

        touching = null;
        float best = float.MaxValue;
        foreach (var e in WorldRegistry.Edibles)
        {
            if (!e.Available) continue;
            var dv = e.GlobalPosition - GlobalPosition; dv.Y = 0;
            float d = dv.Length();
            if (d > e.Radius + TouchRange) continue;
            float b = Bearing(e.GlobalPosition);
            float l = b > -0.6f ? 1f : 0f, r = b < 0.6f ? 1f : 0f;
            if (e.IsPoison) { f.BitterL = Mathf.Max(f.BitterL, l); f.BitterR = Mathf.Max(f.BitterR, r); }
            else { f.SugarL = Mathf.Max(f.SugarL, l); f.SugarR = Mathf.Max(f.SugarR, r); }
            if (d < best) { best = d; touching = e; }
        }

        var antL = head + Forward * 0.4f + Left * AntennaOffset;
        var antR = head + Forward * 0.4f - Left * AntennaOffset;
        foreach (var o in WorldRegistry.Odors)
        {
            float cl = o.ConcentrationAt(antL), cr = o.ConcentrationAt(antR);
            switch (o.Kind)
            {
                case OdorKind.Food: f.OdorFoodL += cl; f.OdorFoodR += cr; break;
                case OdorKind.Poison: f.OdorPoisonL += cl; f.OdorPoisonR += cr; break;
                default: f.OdorNeutralL += cl; f.OdorNeutralR += cr; break;
            }
        }
        f.OdorFoodL = Detect(f.OdorFoodL); f.OdorFoodR = Detect(f.OdorFoodR);
        f.OdorPoisonL = Detect(f.OdorPoisonL); f.OdorPoisonR = Detect(f.OdorPoisonR);
        f.OdorNeutralL = Detect(f.OdorNeutralL); f.OdorNeutralR = Detect(f.OdorNeutralR);

        f.SunBearing = Bearing(GlobalPosition + WorldRegistry.SunDirection);
        f.Punish = punishTimer > 0 ? 1f : 0f;
        f.Reward = Frame.Reward;   // diisi Act() saat makan
        Frame = f;
    }

    /// <summary>Ambang deteksi reseptor bau (sama untuk kedua metode).</summary>
    public static float Detect(float c) => Mathf.Clamp((c - OdorThreshold) / (1f - OdorThreshold), 0f, 1f);

    // ---------------- tubuh (main thread) ----------------
    public void Act(float dt)
    {
        simTime += dt;
        var cmd = Command;
        // refleks mekanosensorik tubuh (bukan otak): hindari dinding/rintangan
        bool hitL = rayL.IsColliding(), hitR = rayR.IsColliding(), hitC = rayC.IsColliding();
        bool front = hitC || (hitL && hitR);
        if (front)
        {
            // terhalang di depan: putar MENJAUH dari dinding (sisi tempat normal permukaan berada) dan kunci arahnya
            // sampai ray bebas, agar perintah belok otak yang berubah-ubah tidak membuat NPC meluncur di dinding
            if (avoidTimer <= 0f)
            {
                var rc = hitC ? rayC : hitL ? rayL : rayR;
                var nrm = rc.GetCollisionNormal(); nrm.Y = 0f;
                float lean = nrm.Dot(Left);
                avoidDir = Mathf.Abs(lean) > 0.05f ? Mathf.Sign(lean) : (cmd.Turn >= 0f ? 1f : -1f);
            }
            avoidTimer = AvoidHoldSec;
        }
        else avoidTimer -= dt;

        float turn, forward = cmd.Forward;
        if (avoidTimer > 0f)
        {
            turn = avoidDir;
            forward *= front ? 0.2f : 0.7f;
        }
        else
        {
            float reflex = 0f;   // ray samping: menjauh dari sisi yang tersentuh
            if (hitL) reflex -= 1f;
            if (hitR) reflex += 1f;
            turn = Mathf.Clamp(cmd.Turn + reflex, -1f, 1f);
        }

        yawRate = turn * MaxTurnRate * (cmd.Escape ? 1.5f : 1f);
        RotateY(yawRate * dt);
        var v = Forward * forward * WalkSpeed;
        v.Y = IsOnFloor() ? 0f : Velocity.Y - Gravity * dt;
        Velocity = v;
        var before = GlobalPosition;
        MoveAndSlide();
        Metrics.Distance += (GlobalPosition - before).Length();

        Hunger = Mathf.Clamp(Hunger + HungerRate * dt, 0f, 1f);
        Frame.Reward = 0f;
        int action = cmd.Escape ? 3 : cmd.Eat ? 2 : Mathf.Abs(cmd.Turn) > 0.5f ? 1 : 0;
        if (cmd.Eat && touching != null && touching.Consume(dt))
        {
            action = touching.IsPoison ? 4 : 2;
            if (touching.IsPoison) { Metrics.PoisonBites++; Hurt(1.5f); }
            else { Hunger = Mathf.Max(0f, Hunger - 0.3f * dt); Frame.Reward = 1f; Metrics.FoodSeconds += dt; }
        }

        bool inDanger = false;
        foreach (var z in WorldRegistry.Dangers) inDanger |= z.Contains(GlobalPosition);
        if (inDanger) { Metrics.TimeInDanger += dt; if (punishTimer <= 0) Hurt(0.5f); }
        if (inDanger && !wasInDanger) Metrics.DangerEntries++;
        wasInDanger = inDanger;
        if (cmd.Escape && !wasEscaping)
        {
            Metrics.EscapeEvents++;
            RecordHumanFlee();
        }
        wasEscaping = cmd.Escape;
        punishTimer -= dt;

        Metrics.ActionTime[action] = Metrics.ActionTime.GetValueOrDefault(action) + dt;
        pathTimer -= dt;
        if (pathTimer <= 0f)
        {
            pathTimer = 0.1f;
            Metrics.Path.Add((simTime, GlobalPosition.X, GlobalPosition.Z, action, cmd.Valence));
        }

        // Sinkronisasi animasi model 3D & indikator emotikon status untuk penguji manusia
        if (cmd.Escape) PlayAnim("Gallop", 1.35f);
        else if (cmd.Eat && touching != null) PlayAnim("Eating", 1.1f);
        else if (cmd.Forward > 0.1f) PlayAnim("Walk", Mathf.Clamp(cmd.Forward, 0.6f, 1.5f));
        else PlayAnim("Idle", 1f);

        bool touchingBitter = Frame.BitterL + Frame.BitterR > 0f;
        (string emote, Color col) = cmd.Escape ? ("🏃💨", new Color(1f, 0.45f, 0.35f))
            : punishTimer > 0f ? ("⚡", new Color(1f, 0.3f, 0.3f))
            : touchingBitter && !cmd.Eat ? ("🤢", new Color(0.8f, 0.55f, 1f))
            : cmd.Eat && touching != null ? (touching.IsPoison ? "🤢" : "🍎", new Color(0.45f, 1f, 0.5f))
            : ("🧭", AiMethod == "otak" ? new Color(0.75f, 0.92f, 1f) : new Color(1f, 0.92f, 0.7f));
        string badge = AiMethod == "otak" ? (Vision == VisionMode.Retinotopic ? "🧠Otak·retina" : "🧠Otak") : "⚙️FSM";
        label.Modulate = col;
        label.Text = $"{emote} {TypeName} [{badge}]\n{StatusText}";
    }

    void Hurt(float seconds)
    {
        punishTimer = Mathf.Max(punishTimer, seconds);
        Metrics.Punishments++;
        OnHurt();
    }

    protected virtual void OnHurt() { }

    /// <summary>Catat jarak ke pemain bila pemain adalah pemicu looming terdekat saat kabur dimulai.</summary>
    void RecordHumanFlee()
    {
        ILooming nearest = null; float best = float.MaxValue;
        foreach (var l in WorldRegistry.Loomers)
        {
            float d = GlobalPosition.DistanceTo(l.LoomPosition);
            if (d < best) { best = d; nearest = l; }
        }
        if (nearest is Player) Metrics.HumanFleeDist.Add(best);
    }

    public void OnHumanTouch()
    {
        Metrics.HumanTouches++;
        Hurt(1f);
    }

    public void OnPredatorHit()
    {
        Metrics.PredatorHits++;
        Hurt(1f);
    }

    public bool ShowLabel { set => label.Visible = value; }
}
