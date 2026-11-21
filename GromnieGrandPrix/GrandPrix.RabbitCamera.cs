using Godot;

public partial class GrandPrix
{
    private Control rabbitCameraPanel = null!;
    private SubViewport rabbitViewport = null!;
    private Camera3D rabbitCamera = null!;
    private Label rabbitCameraTitle = null!;
    private RabbitChase? filmedRabbit;
    private float rabbitCameraYaw, rabbitCameraHold;
    private const uint RabbitReplayLayer = 1u << 19, RaceOnlyLayer = 1u << 18;
    private RabbitReplay? rabbitReplay;

    private sealed class RabbitReplay(Node3D rider, MountedRacer.Ejection motion, MeshInstance3D flash, StandardMaterial3D flashPaint,
        Vector3 impact, Vector3 heading, List<(MeshInstance3D Mesh, uint Layers)> liveMeshes)
    {
        public readonly Node3D Rider = rider;
        public readonly MountedRacer.Ejection Motion = motion;
        public readonly MeshInstance3D Flash = flash;
        public readonly StandardMaterial3D FlashPaint = flashPaint;
        public readonly Vector3 Impact = impact, Heading = heading;
        public readonly List<(MeshInstance3D Mesh, uint Layers)> LiveMeshes = liveMeshes;
        public float Age;
    }

    private void BuildRabbitCamera()
    {
        rabbitCameraPanel = new Control { Position = new(28, 225), MouseFilter = Control.MouseFilterEnum.Ignore };
        raceUi.AddChild(rabbitCameraPanel);
        Plate(rabbitCameraPanel, 0, 0, 276, 182, "172821f6", "7a8f65");
        rabbitCameraTitle = Text(rabbitCameraPanel, "WHITE RABBIT CAM", 10, 6, 256, 13, Gold);
        rabbitViewport = new SubViewport
        {
            Size = new(288, 162), GuiDisableInput = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        rabbitCameraPanel.AddChild(rabbitViewport);
        rabbitCamera = new Camera3D { Fov = 58, Near = .12f, Far = 450, Current = true };
        rabbitViewport.AddChild(rabbitCamera);
        rabbitCamera.CullMask &= ~RaceOnlyLayer;
        camera.CullMask &= ~RabbitReplayLayer;
        rabbitCameraPanel.AddChild(new TextureRect
        {
            Position = new(8, 28), Size = new(260, 146), Texture = rabbitViewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore
        });
        rabbitCameraPanel.Hide();
    }

    private void StartRabbitCamera(RabbitChase chase)
    {
        EndRabbitReplay();
        filmedRabbit = chase;
        rabbitCameraHold = 0;
        Vector3 forward = chase.Owner.Heading(track);
        if (chase.Target.Distance + chase.TargetStationOffset < chase.Owner.Distance) forward = -forward;
        rabbitCameraYaw = BogCircuit.Yaw(forward);
        FrameRabbit(chase, 0);
        UpdateRabbitCameraVisibility();
    }

    private void FrameRabbit(RabbitChase chase, float dt)
    {
        bool running = chase.Rabbit.Visible;
        if (running)
            rabbitCameraYaw = Mathf.LerpAngle(rabbitCameraYaw, chase.Root.Rotation.Y, 1 - Mathf.Exp(-11 * dt));
        Vector3 forward = Vector3.Forward.Rotated(Vector3.Up, rabbitCameraYaw);
        Vector3 right = forward.Cross(Vector3.Up);
        Vector3 focus = chase.Root.Position + (running ? Vector3.Up * .48f : chase.Orb.Position);
        float widen = running ? Mathf.Clamp(chase.Bite / RabbitBiteTime, 0, 1) : 1;
        Vector3 desired = focus - forward * Mathf.Lerp(2.45f, 3.9f, widen)
            + right * Mathf.Lerp(1.3f, 1.75f, widen) + Vector3.Up * Mathf.Lerp(1.05f, 1.5f, widen);
        rabbitCamera.Position = ClearCameraSight(desired, focus);
        rabbitCamera.LookAt(focus + forward * Mathf.Lerp(.45f, .85f, widen));
    }

    private void UpdateRabbitCameraVisibility()
    {
        bool visible = (filmedRabbit is not null || rabbitReplay is not null || rabbitCameraHold > 0) && racing && !paused && !finished;
        rabbitCameraPanel.Visible = visible;
        rabbitViewport.RenderTargetUpdateMode = visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
    }

    private void ClearRabbitCamera()
    {
        EndRabbitReplay();
        filmedRabbit = null;
        rabbitCameraHold = 0;
        UpdateRabbitCameraVisibility();
    }

    private void StartRabbitReplay(RabbitChase chase)
    {
        var rider = new Node3D { Name = "Bunny Smite replay" };
        itemFxRoot.AddChild(rider);
        var motion = chase.Target.EjectMotion;
        rider.GlobalTransform = motion.Launch;
        var liveMeshes = new List<(MeshInstance3D, uint)>();
        var inverse = motion.Launch.AffineInverse();
        foreach (var node in chase.Target.Rider.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D { Mesh: { } mesh } live || !live.IsVisibleInTree()) continue;
            var copy = new MeshInstance3D
            {
                Mesh = mesh, MaterialOverride = live.MaterialOverride, Layers = RabbitReplayLayer,
                Transform = inverse * live.GlobalTransform, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            for (int i = 0; i < live.GetSurfaceOverrideMaterialCount(); i++)
                copy.SetSurfaceOverrideMaterial(i, live.GetSurfaceOverrideMaterial(i));
            rider.AddChild(copy);
            liveMeshes.Add((live, live.Layers));
            live.Layers = RaceOnlyLayer;
        }

        Vector3 impact = chase.Target.CoursePosition(track) + Vector3.Up * .6f;
        var paint = Glow(BunnyPink);
        paint.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        var flash = BogCircuit.MeshAt(itemFxRoot, ringMesh, impact, Vector3.One, paint);
        flash.Layers = RabbitReplayLayer;
        flash.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        flash.Rotation = new(.35f, .1f, -.36f);
        rabbitReplay = new(rider, motion, flash, paint, impact,
            Vector3.Forward.Rotated(Vector3.Up, rabbitCameraYaw), liveMeshes);
        rabbitCameraTitle.Text = "BUNNY SMITE  /  SLOW MOTION";
        TickRabbitReplay(0);
    }

    private void TickRabbitReplay(float dt)
    {
        if (rabbitReplay is not { } replay) return;
        replay.Age += dt * .30f;
        if (replay.Age >= .66f)
        {
            EndRabbitReplay();
            UpdateRabbitCameraVisibility();
            return;
        }
        replay.Rider.GlobalTransform = replay.Motion.PoseAt(track, replay.Age);
        replay.Rider.GlobalPosition += Vector3.Up * MountedRacer.RoadMeshLift(track, replay.Rider.GetChildren().OfType<MeshInstance3D>());
        replay.Flash.Scale = Vector3.One * (.45f + replay.Age * 7);
        replay.FlashPaint.AlbedoColor = new Color(BunnyPink, 1 - replay.Age / .66f);
        Vector3 focus = replay.Impact.Lerp(replay.Rider.GlobalPosition + Vector3.Up * .6f, .72f + replay.Age * .4f);
        Vector3 side = replay.Heading.Cross(Vector3.Up);
        Vector3 desired = focus - replay.Heading * 4 + side * (2.4f + replay.Age) + Vector3.Up * 2.1f;
        rabbitCamera.Position = ClearCameraSight(desired, focus);
        rabbitCamera.LookAt(focus);
    }

    private void EndRabbitReplay()
    {
        if (rabbitReplay is not { } replay) return;
        foreach (var (mesh, layers) in replay.LiveMeshes)
            if (GodotObject.IsInstanceValid(mesh)) mesh.Layers = layers;
        replay.Rider.Hide();
        replay.Flash.Hide();
        replay.Rider.QueueFree();
        replay.Flash.QueueFree();
        rabbitReplay = null;
        rabbitCameraTitle.Text = "WHITE RABBIT CAM";
    }
}
