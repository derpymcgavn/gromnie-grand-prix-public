using Godot;

public partial class GrandPrix
{
    private void StartPresentationRace()
    {
        ChooseCourse(xlRoutes.Keys.Single(id => CourseName(id) == "Yanshi XL"));
        StartRace();
        Require(racing && track.Name == "Yanshi XL", "the presentation course loads");
        countdown = 0;
        ApplyWeather(WeatherPresets[0]);
    }

    private void CheckRacePresentation()
    {
        StartPresentationRace();
        SetRaceCamera(1);
        float idleFov = camera.Fov;
        UpdateSpeedBlur(0, immediate: true);
        Require(Mathf.IsEqualApprox(idleFov, 58) && sceneGrade.Visible && !speedBlur.Visible,
            "the chase camera and standalone grade retain their idle look");

        Player.Speed = RoadPace;
        Player.Boost = 2;
        SetRaceCamera(1);
        UpdateSpeedBlur(0, immediate: true);
        Require(camera.Fov > idleFov + 7 && speedBlur.Visible && !sceneGrade.Visible,
            "boost opens the field of view and grades the speed effect in one pass");

        float lowSpeedFov = camera.Fov;
        float lowSpeedBlur = blurStrength;
        Player.Speed = DriftSpeedCap;
        SetRaceCamera(1);
        UpdateSpeedBlur(0, immediate: true);
        Require(camera.Fov > lowSpeedFov + 5 && blurStrength > lowSpeedBlur && blurStrength < 1 &&
                SpeedBlurStrength(27, true, false) is > .6f and < 1,
            "road effects build from the cruise pace to the super drift cap");
        Require(VisualPace(38, true) is > 0 and < 1 && Mathf.IsEqualApprox(VisualPace(53, true), 1),
            "flight uses its own speed range");
        Player.Speed = RoadPace;

        CheckRaceViews();
        raceView = RaceView.TiltShift;
        SetRaceCamera(1);
        UpdateSpeedBlur(0, immediate: true);
        Require(tiltShiftOverlay.Visible && !speedBlur.Visible && !sceneGrade.Visible,
            "tilt shift supplies its own grade");
        SetPaused(true);
        Require(sceneGrade.Visible && !speedBlur.Visible && !tiltShiftOverlay.Visible,
            "the grade stays active behind the pause menu");
        GD.Print("PRESENTATION PASS | boost FOV, five camera views, grade handoff and pause.");
    }

    private async Task CaptureRacePresentation(string folder, bool video)
    {
        StartPresentationRace();
        SetPhysicsProcess(false);
        float[] lanes = [-3.3f, -7f, 1f, 4.5f, 7.8f];
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            racer.Distance = track.Gate(0) + (i == 0 ? 77 : 79 + i * 6);
            racer.Lane = lanes[i];
            racer.Speed = i == 0 ? 16 : 22 + i * .65f;
            racer.GatesCleared = 1;
            racer.Checkpoint = track.Gate(0);
            racer.Draw(track, 0, true);
        }
        HighlightCheckpoint();
        SetRaceCamera(1);
        UpdateRaceUi();
        if (!video)
        {
            foreach (int index in new[] { 0, 3, 4, 5 })
            {
                var weather = WeatherPresets[index];
                ApplyWeather(weather);
                if (weather.Rain > 0) TickTrackWeather(12);
                UpdateRaceUi();
                await SaveFrame(Path.Combine(folder, $"{weather.Name.Replace(' ', '-')}.png"));
            }
            ApplyWeather(WeatherPresets[0]);
        }

        const float step = 1f / 60;
        for (int frame = 0; frame < (video ? 240 : 92); frame++)
        {
            bool boost = frame is 63 or 160;
            if (boost) HoldItem(Player, RaceItem.Pepper);
            for (int tick = 0; tick < 2; tick++)
            {
                float steer = Mathf.Clamp((lanes[0] - Player.Lane) * .36f + track.Curvature(Player.Distance + 3) * 2.1f, -.65f, .65f);
                worldTime += step;
                TickRace(step, true, false, steer, false, boost && tick == 0);
                SetRaceCamera(1 - Mathf.Exp(-8.2f * step));
                UpdateSpeedBlur(step);
            }
            UpdateRaceUi();
            if (video) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        Require(!Player.FreeDriving && Player.Distance > track.Gate(0) + (video ? 205 : 140), "the capture drives the authored road");
        if (!video) await SaveFrame(Path.Combine(folder, "BOOST.png"));
        GD.Print("PRESENTATION CAPTURE PASS");
    }
}
