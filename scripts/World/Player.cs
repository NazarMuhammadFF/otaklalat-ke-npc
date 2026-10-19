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
    public Camera3D Camera { get; private set; }
    public CamMode Mode { get; private set; } = CamMode.TopDown;
    public NpcBase FollowTarget { get; set; }
    public Vector3 LoomPosition => GlobalPosition + new Vector3(0, 1f, 0);
    public float LoomRadius => 0.5f;
    public System.Action<Vector3> DropFood;

    MeshInstance3D bodyVisual;
    Vector3 topDownFocus = new(0, 0, 2f);
    float topDownHeight = 25f, topDownYaw = 0f, topDownPitch = -1.05f;
    float fpPitch = 0f;

    public override void _EnterTree() => WorldRegistry.Loomers.Add(this);
    public override void _ExitTree() => WorldRegistry.Loomers.Remove(this);

    public override void _Ready()
    {
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) });
        bodyVisual = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.85f, 0.4f) },
            Position = new Vector3(0, 0.9f, 0),
        };
        AddChild(bodyVisual);

        Camera = new Camera3D { Position = new Vector3(0, 1.7f, 0), Current = true, Far = 400f };
        AddChild(Camera);
        SetCameraMode(CamMode.TopDown);
    }

    public void SetCameraMode(CamMode newMode, NpcBase target = null)
    {
        Mode = newMode;
        if (target != null) FollowTarget = target;
        if (Mode == CamMode.FollowNpc && (FollowTarget == null || !IsInstanceValid(FollowTarget)))
        {
            if (NpcBase.All.Count > 0) FollowTarget = NpcBase.All[0];
            else Mode = CamMode.TopDown;
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
            else
            {
                topDownYaw -= m.Relative.X * MouseSensitivity;
                topDownPitch = Mathf.Clamp(topDownPitch - m.Relative.Y * MouseSensitivity, -1.48f, -0.35f);
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

        if (Mode == CamMode.FirstPerson)
        {
            float speed = Input.IsKeyPressed(Key.Shift) ? SprintSpeed : Speed;
            var dir = (GlobalBasis * new Vector3(input.X, 0, input.Y)).Normalized() * speed;
            var v = Velocity;
            v.X = dir.X; v.Z = dir.Z;
            v.Y = IsOnFloor() ? 0 : v.Y - 9.8f * dt;
            Velocity = v;
            MoveAndSlide();
        }
        else if (Mode == CamMode.TopDown)
        {
            Velocity = Vector3.Zero;
            float panSpeed = (Input.IsKeyPressed(Key.Shift) ? 24f : 14f) * dt;
            var yawBasis = Basis.FromEuler(new Vector3(0, topDownYaw, 0));
            var pan = (yawBasis * new Vector3(input.X, 0, input.Y)).Normalized() * panSpeed;
            topDownFocus.X = Mathf.Clamp(topDownFocus.X + pan.X, -Arena.HalfSize, Arena.HalfSize);
            topDownFocus.Z = Mathf.Clamp(topDownFocus.Z + pan.Z, -Arena.HalfSize, Arena.HalfSize);

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
            var targetPos = FollowTarget.GlobalPosition + new Vector3(0, 1.1f, 0);
            var back = FollowTarget.GlobalBasis.Z;
            var desiredPos = targetPos + back * 4.8f + new Vector3(0, 2.8f, 0);
            Camera.GlobalPosition = Camera.GlobalPosition.Lerp(desiredPos, Mathf.Clamp(dt * 8f, 0f, 1f));
            Camera.LookAt(targetPos, Vector3.Up);
        }
    }
}
