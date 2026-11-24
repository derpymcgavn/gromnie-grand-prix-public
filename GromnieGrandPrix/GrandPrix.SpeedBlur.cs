using Godot;

public partial class GrandPrix
{
    private ColorRect speedBlur = null!;
    private ShaderMaterial speedBlurMaterial = null!;
    private float blurStrength, windStrength, windTravel;
    private Vector2 blurCenter = new(.5f, .48f);

    private void BuildSpeedBlur()
    {
        var layer = new CanvasLayer { Layer = 0 };
        AddChild(layer);
        speedBlurMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/RaceSpeedBlur.gdshader") };
        speedBlur = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = speedBlurMaterial };
        layer.AddChild(speedBlur);
        speedBlur.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        speedBlur.Hide();
    }

    private void UpdateSpeedBlur(float dt, bool immediate = false)
    {
        if (!raceSettings.SpeedBlur) return;
        bool airborne = Player.Flight is not null, boosting = Player.Boost > 0;
        float pace = VisualPace(Player.Speed, airborne, Player.UsesJetSki);
        float highSpeed = airborne ? 0 : RoadSpeedCue(Player.Speed, Player.UsesJetSki);
        float target = SpeedBlurStrength(Player.Speed, boosting, airborne, Player.UsesJetSki);
        float wind = (boosting ? .38f + .62f * pace : pace * pace * .23f + highSpeed * .17f) * (airborne ? .46f : 1);
        float weight = immediate ? 1 : 1 - Mathf.Exp(-(target > blurStrength ? 12 : 6) * dt);
        blurStrength = Mathf.Lerp(blurStrength, target, weight);
        windStrength = Mathf.Lerp(windStrength, wind, weight);
        speedBlur.Visible = racing && !paused && !finished && !Player.Falling && Player.EjectTime <= 0 &&
            raceView is (RaceView.Chase or RaceView.WideChase or RaceView.FirstPerson) && blurStrength > .015f;
        if (!speedBlur.Visible) return;

        float lookDistance = airborne ? 44 : 25 + highSpeed * 15;
        Vector3 lookAhead = Player.CoursePosition(track) + Player.Heading(track) * lookDistance + Vector3.Up * 1.2f;
        if (!airborne && !Player.FreeDriving && highSpeed > 0)
            lookAhead = lookAhead.Lerp(track.Surface(Player.Distance + lookDistance, Player.Lane) + Vector3.Up * 1.2f, highSpeed * .6f);
        Vector2 center = new(.5f, .48f);
        if (!camera.IsPositionBehind(lookAhead))
        {
            Vector2 screen = camera.UnprojectPosition(lookAhead) / GetViewport().GetVisibleRect().Size;
            center = new Vector2(Mathf.Clamp(screen.X, .38f, .62f), Mathf.Clamp(screen.Y, .31f, .59f));
        }
        blurCenter = blurCenter.Lerp(center, weight * (immediate ? 1 : .66f));
        windTravel += dt * (.75f + pace * 1.9f + windStrength * 1.15f + highSpeed * .8f);
        speedBlurMaterial.SetShaderParameter("strength", blurStrength);
        speedBlurMaterial.SetShaderParameter("wind", windStrength);
        speedBlurMaterial.SetShaderParameter("travel", windTravel);
        speedBlurMaterial.SetShaderParameter("vanishing_point", blurCenter);
    }

    private static float VisualPace(float speed, bool airborne, bool jetSki = false) => airborne
        ? Mathf.SmoothStep(9, 53, speed)
        : .58f * Mathf.SmoothStep(9, jetSki ? JetSkiPace : RoadPace, speed) + .42f * RoadSpeedCue(speed, jetSki);

    private static float RoadSpeedCue(float speed, bool jetSki = false) =>
        Mathf.SmoothStep(jetSki ? JetSkiPace : RoadPace, jetSki ? JetSkiDriftCap : DriftSpeedCap, speed);

    private static float SpeedBlurStrength(float speed, bool boosting, bool airborne, bool jetSki = false) =>
        Mathf.Clamp(VisualPace(speed, airborne, jetSki) * (airborne ? .77f : .60f) + (boosting ? .29f : 0), 0, 1);

    private void ClearSpeedBlur()
    {
        blurStrength = windStrength = windTravel = 0;
        blurCenter = new Vector2(.5f, .48f);
        speedBlur.Hide();
    }
}
