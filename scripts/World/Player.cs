using Godot;
using OtakLalat.Npc;

namespace OtakLalat.World;

/// <summary>
/// Kamera & karakter penguji. Mendukung 3 mode kamera:
///   F1: First-Person Pemain (WASD jalan, Shift lari looming, F taruh makanan)
///   F2: Pandangan Atas / Bird's-Eye Arena (WASD geser, Scroll zoom, Klik kanan putar)
///   F3: Follow NPC terpilih (mengikuti hewan dari dekat)
/// </summary>
public partial class Player : CharacterBody3D, ILooming
{
    public enum CamMode { FirstPerson, TopDown, FollowNpc }

    [Export] public float Speed = 5f, SprintSpeed = 11f, MouseSensitivity = 0.003f;
    [Export] public float KeyLookSpeed = 2.0f; // Kecepatan rotasi kamera keyboard arah (rad/detik)
    public Camera3D Camera { get; private set; }
    public CamMode Mode { get; private set; } = CamMode.TopDown;
    public NpcBase FollowTarget { get; set; }
    public Vector3 LoomPosition => GlobalPosition + new Vector3(0, 1f, 0);
    public float LoomRadius => 0.5f;
    public System.Action<Vector3> DropFood;

    Node3D bodyVisual;
    OdorSource humanOdor;
    readonly System.Collections.Generic.Dictionary<NpcBase, float> touchCooldown = new();
    /// <summary>Bau manusia (kanal netral, TIDAK dilabeli): NPC hanya belajar menghindarinya lewat pengalaman disentuh.</summary>
    public bool EmitsOdor { get => humanOdor?.Active ?? false; set { if (humanOdor != null) humanOdor.Active = value; } }
    Vector3 topDownFocus = new(0, 0, 2f);
    float topDownHeight = 25f, topDownYaw = 0f, topDownPitch = -1.05f;
    float fpPitch = 0f;
    float followYaw = 0f, followPitch = 0f;

    public override void _EnterTree() => WorldRegistry.Loomers.Add(this);
    public override void _ExitTree() => WorldRegistry.Loomers.Remove(this);

    public override void _Ready()
    {
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) });
        // sosok manusia sederhana: badan, kepala, hidung penunjuk arah hadap (-Z), dan label
        bodyVisual = new Node3D { Name = "HumanBody" };
        var skin = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.85f, 0.4f) };
        bodyVisual.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.5f }, MaterialOverride = skin, Position = new Vector3(0, 0.75f, 0) });
        bodyVisual.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.2f, Height = 0.4f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.8f, 0.65f) }, Position = new Vector3(0, 1.65f, 0) });
        bodyVisual.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.1f, 0.25f) }, MaterialOverride = skin, Position = new Vector3(0, 1.65f, -0.22f) });
        bodyVisual.AddChild(new Label3D { Text = "KAMU (manusia)", Position = new Vector3(0, 2.2f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 30, OutlineSize = 8, PixelSize = 0.005f, Modulate = new Color(0.6f, 1f, 0.7f) });
        AddChild(bodyVisual);
        humanOdor = new OdorSource { Name = "HumanOdor", Kind = OdorKind.Neutral, Strength = 1f, Spread = 2.5f, Position = new Vector3(0, 1f, 0) };
        AddChild(humanOdor);

        Camera = new Camera3D { Position = new Vector3(0, 1.7f, 0), Current = true, Far = 400f };
        AddChild(Camera);
        SetCameraMode(CamMode.TopDown);
    }

    public void SetCameraMode(CamMode newMode, NpcBase target = null)
    {
        Mode = newMode;
        if (target != null) FollowTarget = target;
        if (Mode == CamMode.FollowNpc)
        {
            followYaw = 0f;
            followPitch = 0f;
            if (FollowTarget == null || !IsInstanceValid(FollowTarget))
            {
                if (NpcBase.All.Count > 0) FollowTarget = NpcBase.All[0];
                else Mode = CamMode.TopDown;
            }
        }
        if (bodyVisual != null) bodyVisual.Visible = Mode != CamMode.FirstPerson;
        if (Mode == CamMode.FirstPerson)
        {
            Camera.TopLevel = false;
            Camera.Position = new Vector3(0, 1.7f, 0);
            Camera.Rotation = new Vector3(fpPitch, 0, 0);
        }
        else
        {
            Camera.TopLevel = true;
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } k)
        {
            if (k.Keycode == Key.F1) { SetCameraMode(CamMode.FirstPerson); return; }
            if (k.Keycode == Key.F2) { SetCameraMode(CamMode.TopDown); return; }
            if (k.Keycode == Key.F3) { SetCameraMode(CamMode.FollowNpc); return; }
            if (k.Keycode == Key.F)
            {
                var dropPos = Mode == CamMode.FirstPerson
                    ? GlobalPosition - GlobalBasis.Z * 2.5f
                    : FollowTarget != null && IsInstanceValid(FollowTarget)
                        ? FollowTarget.GlobalPosition - FollowTarget.GlobalBasis.Z * 2.2f
                        : topDownFocus;
                DropFood?.Invoke(dropPos);
                return;
            }
        }

        if (e is InputEventMouseButton { Pressed: true } mb && Mode == CamMode.TopDown)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) topDownHeight = Mathf.Clamp(topDownHeight - 2.2f, 7f, 45f);
            else if (mb.ButtonIndex == MouseButton.WheelDown) topDownHeight = Mathf.Clamp(topDownHeight + 2.2f, 7f, 45f);
        }
        else if (e is InputEventMouseMotion m && Input.IsMouseButtonPressed(MouseButton.Right))
        {
            if (Mode == CamMode.FirstPerson)
            {
                RotateY(-m.Relative.X * MouseSensitivity);
                fpPitch = Mathf.Clamp(fpPitch - m.Relative.Y * MouseSensitivity, -1.4f, 1.4f);
                Camera.Rotation = new Vector3(fpPitch, 0, 0);
            }
            else if (Mode == CamMode.TopDown)
            {
                topDownYaw -= m.Relative.X * MouseSensitivity;
                topDownPitch = Mathf.Clamp(topDownPitch - m.Relative.Y * MouseSensitivity, -1.48f, -0.35f);
            }
            else if (Mode == CamMode.FollowNpc)
            {
                followYaw -= m.Relative.X * MouseSensitivity;
                followPitch = Mathf.Clamp(followPitch + m.Relative.Y * MouseSensitivity, -0.6f, 1.2f);
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var input = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W)) input.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) input.Y += 1;
        if (Input.IsKeyPressed(Key.A)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D)) input.X += 1;

        // Kontrol rotasi kamera via Keyboard Arah (Arrow Keys)
        var camInput = Vector2.Zero;
        if (Input.IsKeyPressed(Key.Left)) camInput.X -= 1;
        if (Input.IsKeyPressed(Key.Right)) camInput.X += 1;
        if (Input.IsKeyPressed(Key.Up)) camInput.Y -= 1;
        if (Input.IsKeyPressed(Key.Down)) camInput.Y += 1;

        float rotSpeed = (Input.IsKeyPressed(Key.Shift) ? KeyLookSpeed * 1.6f : KeyLookSpeed) * dt;

        if (Mode == CamMode.FirstPerson)
        {
            // Rotasi kamera FPS: Panah Kiri/Kanan = Putar Badan (Yaw), Panah Atas/Bawah = Angguk Kepala (Pitch)
            if (camInput.X != 0) RotateY(-camInput.X * rotSpeed);
            if (camInput.Y != 0)
            {
                fpPitch = Mathf.Clamp(fpPitch - camInput.Y * rotSpeed, -1.4f, 1.4f);
                Camera.Rotation = new Vector3(fpPitch, 0, 0);
            }

            float speed = Input.IsKeyPressed(Key.Shift) ? SprintSpeed : Speed;
            var dir = (GlobalBasis * new Vector3(input.X, 0, input.Y)).Normalized() * speed;
            var v = Velocity;
            v.X = dir.X; v.Z = dir.Z;
            v.Y = IsOnFloor() ? 0 : v.Y - 9.8f * dt;
            Velocity = v;
            MoveAndSlide();
            TouchNpcs(dt);
        }
        else if (Mode == CamMode.TopDown)
        {
            Velocity = Vector3.Zero;
            float panSpeed = (Input.IsKeyPressed(Key.Shift) ? 24f : 14f) * dt;
            var yawBasis = Basis.FromEuler(new Vector3(0, topDownYaw, 0));
            var pan = (yawBasis * new Vector3(input.X, 0, input.Y)).Normalized() * panSpeed;
            topDownFocus.X = Mathf.Clamp(topDownFocus.X + pan.X, -Arena.HalfSize, Arena.HalfSize);
            topDownFocus.Z = Mathf.Clamp(topDownFocus.Z + pan.Z, -Arena.HalfSize, Arena.HalfSize);

            // Rotasi sudut pandang Bird's-Eye: Panah Kiri/Kanan = Orbit Yaw, Panah Atas/Bawah = Sudut Pitch
            if (camInput.X != 0) topDownYaw -= camInput.X * rotSpeed;
            if (camInput.Y != 0)
                topDownPitch = Mathf.Clamp(topDownPitch - camInput.Y * rotSpeed, -1.48f, -0.35f);

            // Zoom alternatif via Keyboard (+/- atau PageUp/PageDown)
            if (Input.IsKeyPressed(Key.Pageup) || Input.IsKeyPressed(Key.Equal) || Input.IsKeyPressed(Key.KpAdd))
                topDownHeight = Mathf.Clamp(topDownHeight - 16f * dt, 7f, 45f);
            else if (Input.IsKeyPressed(Key.Pagedown) || Input.IsKeyPressed(Key.Minus) || Input.IsKeyPressed(Key.KpSubtract))
                topDownHeight = Mathf.Clamp(topDownHeight + 16f * dt, 7f, 45f);

            float horizDist = topDownHeight / Mathf.Tan(-topDownPitch);
            var offset = yawBasis * new Vector3(0, topDownHeight, horizDist);
            Camera.GlobalPosition = topDownFocus + offset;
            Camera.LookAt(topDownFocus, Vector3.Up);
        }
        else if (Mode == CamMode.FollowNpc)
        {
            Velocity = Vector3.Zero;
            if (FollowTarget == null || !IsInstanceValid(FollowTarget))
            {
                SetCameraMode(CamMode.TopDown);
                return;
            }

            // Orbit mengelilingi hewan yang diikuti: Panah Kiri/Kanan = Orbit, Panah Atas/Bawah = Elevasi
            if (camInput.X != 0) followYaw -= camInput.X * rotSpeed;
            if (camInput.Y != 0) followPitch = Mathf.Clamp(followPitch - camInput.Y * rotSpeed, -0.6f, 1.2f);

            var targetPos = FollowTarget.GlobalPosition + new Vector3(0, 1.1f, 0);
            var orbitRot = Basis.FromEuler(new Vector3(0, followYaw, 0));
            var back = orbitRot * FollowTarget.GlobalBasis.Z;
            float desiredHeight = 2.8f + followPitch * 2.2f;
            var desiredPos = targetPos + back * 4.8f + new Vector3(0, desiredHeight, 0);
            Camera.GlobalPosition = Camera.GlobalPosition.Lerp(desiredPos, Mathf.Clamp(dt * 8f, 0f, 1f));
            Camera.LookAt(targetPos, Vector3.Up);
        }
    }

    /// <summary>Menyentuh NPC saat berlari = stimulus nyeri (seperti serangan drone); jeda 1,5 s per NPC.</summary>
    void TouchNpcs(float dt)
    {
        foreach (var k in new System.Collections.Generic.List<NpcBase>(touchCooldown.Keys))
        {
            if (!IsInstanceValid(k) || (touchCooldown[k] -= dt) <= 0f) touchCooldown.Remove(k);
        }
        if (!Input.IsKeyPressed(Key.Shift)) return;
        foreach (var n in NpcBase.All)
        {
            if (touchCooldown.ContainsKey(n)) continue;
            var d = n.GlobalPosition - GlobalPosition; d.Y = 0;
            if (d.Length() > 1.0f) continue;
            n.OnHumanTouch();
            touchCooldown[n] = 1.5f;
        }
    }
}
