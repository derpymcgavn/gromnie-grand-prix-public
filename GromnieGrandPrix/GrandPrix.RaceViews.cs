using Godot;

public partial class GrandPrix
{
    private enum RaceView { Chase, FirstPerson, Isometric, TiltShift, WideChase }
    private RaceView raceView;
    private float chaseYaw;
    private Label raceViewLabel = null!;
    private ColorRect tiltShiftOverlay = null!;
    private ShaderMaterial tiltShiftMaterial = null!;

    private void BuildRaceViews()
    {
        var layer = new CanvasLayer { Layer = 0 };
        AddChild(layer);
        tiltShiftMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/TiltShift.gdshader") };
        tiltShiftOverlay = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = tiltShiftMaterial };
        layer.AddChild(tiltShiftOverlay);
        tiltShiftOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        tiltShiftOverlay.Hide();
    }

    private void CycleRaceView()
    {
        raceView = raceView == RaceView.WideChase ? RaceView.Chase : (RaceView)((int)raceView + 1);
        SetRaceCamera(1);
        UpdateSpeedBlur(0, immediate: true);
    }

    private void UpdateRaceViewUi()
    {
        tiltShiftOverlay.Visible = raceView == RaceView.TiltShift && racing && !finished && !paused;
        raceViewLabel.Text = raceView switch
        {
            RaceView.FirstPerson => "FIRST PERSON",
            RaceView.Isometric => "ISOMETRIC",
            RaceView.TiltShift => "TILT-SHIFT",
            RaceView.WideChase => "WIDE CHASE",
            _ => "CHASE CAM"
        };
        if (tiltShiftOverlay.Visible)
        {
            float focus = camera.UnprojectPosition(Player.Root.Position + Vector3.Up).Y / GetViewport().GetVisibleRect().Size.Y;
            tiltShiftMaterial.SetShaderParameter("focus_y", Mathf.Clamp(focus, .40f, .70f));
        }
    }

    private Vector3 FitChaseCamera(Vector3 desired, Vector3 focus)
    {
        Vector3 offset = desired - focus;
        Vector3 fitted = desired;
        for (int i = 0; i < 5; i++)
        {
            float reach = 1 - i * .17f;
            Vector3 candidate = focus + new Vector3(offset.X * reach, offset.Y, offset.Z * reach);
            fitted = ClearCameraSight(KeepCameraAboveRoad(candidate, 1.35f), focus);
            if (fitted.Y - desired.Y < 3.4f) break;
        }
        if (!Player.Falling) fitted = track.KeepCameraOutsideScenery(focus, fitted);
        return KeepCameraAboveRoad(fitted, 1.35f);
    }

    private Vector3 KeepCameraAboveRoad(Vector3 eye, float clearance) =>
        eye with { Y = Math.Max(eye.Y, track.GroundOrRoadHeight(eye) + clearance) };

    private void UpdateCheckpointArchVisibility()
    {
        bool chase = raceView is RaceView.Chase or RaceView.WideChase;
        foreach (var (arch, halfWidth) in checkpointArches)
        {
            Vector3 eye = arch.ToLocal(camera.Position);
            bool obstructs = chase && Mathf.Abs(eye.Z) < 4.2f &&
                Mathf.Abs(eye.X) < halfWidth + 1.25f && eye.Y is > -2.2f and < 4.4f;
            arch.Visible = !obstructs;
        }
    }

    private void SetAlternateRaceCamera(float weight)
    {
        Vector3 heading = Player.Heading(track);
        Vector3 rider = Player.CoursePosition(track);
        if (raceView == RaceView.FirstPerson)
        {
            var (ahead, height) = Player.Rig.Kind switch
            {
                RaceMountKind.Gromnie => (1.60f, 1.66f),
                RaceMountKind.Cow => (1.29f, 1.78f),
                RaceMountKind.Auroch => (1.40f, 1.96f),
                RaceMountKind.Chicken => (.59f, 1.94f),
                RaceMountKind.Rabbit => (.68f, 1.48f),
                RaceMountKind.Grievver => (.92f, 1.94f),
                RaceMountKind.Mukkir => (.91f, 1.86f),
                RaceMountKind.Shreth => (1.37f, 1.91f),
                RaceMountKind.Mattekar => (1.28f, 1.97f),
                RaceMountKind.Reedshark => (.98f, 2.04f),
                RaceMountKind.BaelZharon => (1.45f, 2.80f),
                RaceMountKind.FestivusCarpet => (.88f, 1.95f),
                RaceMountKind.IsinDuleDisc => (.72f, 2.75f),
                RaceMountKind.CarenziSkates => (.77f, 2.62f),
                RaceMountKind.Siraluun => (1.06f, 2.63f),
                _ => (.38f, 2.68f)
            };
            if (Player.Flight is not null) (ahead, height) = (1.1f, -.42f);
            Vector3 eye = rider + heading * ahead + Vector3.Up * (height + Player.LeapHeight);
            if (Player.Flight?.Flyer is { SeatsRider: true } flyer)
                eye = flyer.RiderAnchor.GlobalPosition + heading * .95f + Vector3.Up * 1.15f;
            if (Player.Flight is null)
            {
                foreach (var mesh in Player.Mount.BodyMeshes)
                {
                    if (mesh.Mesh is not { } geometry || !mesh.Visible) continue;
                    Aabb bounds = mesh.GlobalTransform * geometry.GetAabb();
                    eye.Y = Math.Max(eye.Y, bounds.End.Y + .23f);
                }
            }
            eye = KeepCameraAboveRoad(eye, .92f);
            float grade = Player.Flight?.Pitch ?? Player.Root.Rotation.X;
            camera.Position = eye;
            bool airborne = Player.Flight is not null;
            float highSpeed = airborne ? 0 : RoadSpeedCue(Player.Speed);
            camera.Fov = Mathf.Lerp(camera.Fov, 70 + VisualPace(Player.Speed, airborne) * 6.5f + highSpeed * 3.2f +
                (Player.Boost > 0 ? 4 : 0), weight);
            AimRaceCamera(eye + heading * 16 + Vector3.Up * Mathf.Clamp(grade * 6.4f - .40f, -3f, 3f), weight);
            return;
        }

        bool isometric = raceView == RaceView.Isometric;
        Vector3 focus = rider + heading * (isometric ? 3.8f : 6.1f);
        Vector3 side = heading.Cross(Vector3.Up);
        Vector3 offset = isometric ? new Vector3(25, 44, 25) : -heading * 24 + side * 10 + Vector3.Up * 24;
        Vector3 desired = focus + offset;
        desired = KeepCameraAboveRoad(desired, 12);
        if (isometric) camera.Size = track.IsXL ? 54 : track.IsMedium ? 50 : 39;
        else camera.Fov = Mathf.Lerp(camera.Fov, 41, weight);
        camera.Position = KeepCameraAboveRoad(camera.Position.Lerp(desired, weight), 1.35f);
        AimRaceCamera(focus, weight);
    }
}
