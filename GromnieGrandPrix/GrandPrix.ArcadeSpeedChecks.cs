using Godot;

public partial class GrandPrix
{
    private const float SpeedCheckStep = 1f / 60;

    private void StartArcadeSpeedTrial()
    {
        StartVoidCheck();
        var route = VoidFlyover(track);
        const float runway = 416;
        float start = Enumerable.Range(0, (int)((track.Length - runway) / 16))
            .Select(index => index * 16f)
            .Where(at => at > 36 && (at + runway < route.Start - 10 || at > route.End + 10))
            .MinBy(at => Enumerable.Range(0, 53).Max(step => Math.Abs(track.Curvature(at + step * 8))));
        PutOnSpeedTrial(start, -4, RoadPace - 5);
    }

    private void PutOnSpeedTrial(float station, float lane, float speed)
    {
        countdown = 0;
        foreach (var rival in racers.Skip(1)) rival.Finish = 1;
        Player.Distance = station;
        Player.Lane = lane;
        Player.Speed = speed;
        Player.GatesCleared = Enumerable.Range(0, track.GateCount).Count(index => track.Gate(index) <= station);
        Player.Checkpoint = track.Gate(Math.Max(0, Player.GatesCleared - 1));
        Player.Draw(track, 0, true);
        HighlightCheckpoint();
    }

    private void TickSpeedTrial(int frame)
    {
        bool drift = frame is >= 24 and < 100;
        float steer = frame is >= 24 and < 42 ? 1 : frame is >= 64 and < 76 ? -1 : 0;
        TickRace(SpeedCheckStep, true, false, steer, drift, false);
    }

    private void CheckArcadeSpeed()
    {
        StartArcadeSpeedTrial();
        for (int tick = 0; tick < 24; tick++) TickSpeedTrial(tick);
        Require(Mathf.IsEqualApprox(Player.Speed, RoadPace) && !Player.FreeDriving,
            $"Rynthid reaches 60 mph at cruise ({Player.Speed / MetresPerSecondPerMph:F1} mph)");

        for (int tick = 24; tick < 100; tick++) TickSpeedTrial(tick);
        Require(drifting && charge > 1.18f && !HasSuperDrift(Player) && dust.UsesNativeDriftSprites &&
                dust.DriftSmokeCount > 0 && dust.SuperDriftFireCount == 0 && Mathf.IsEqualApprox(Player.Speed, RoadPace),
            "the drift charges at cruise speed and leaves smoke");
        float peak = Player.Speed;
        for (int tick = 100; tick < 165; tick++)
        {
            TickSpeedTrial(tick);
            peak = Math.Max(peak, Player.Speed);
            Require(Player.Speed <= DriftSpeedCap + .001f, "ground speed stays within the 100 mph cap");
        }
        Require(Mathf.IsEqualApprox(peak, DriftSpeedCap) && HasSuperDrift(Player) && dust.SuperDriftFireCount > 0 &&
                !Player.FreeDriving && !Player.Falling, $"a real super drift reaches 100 mph and leaves fire (peak {peak / MetresPerSecondPerMph:F1} mph)");
        ChangeRaceSettings(raceSettings with { MilesPerHour = true });
        UpdateRaceSpeedUi();
        Require(speedText.Text == "100 mph", "the speedometer shows the physical drift speed cap");

        for (int tick = 0; tick < 90; tick++) TickRace(SpeedCheckStep, false, true, 0, false, false);
        Require(Player.Speed <= .01f && !Player.Falling && !HasSuperDrift(Player), "braking to a stop is controlled and the fire boost expires");

        ResetRace();
        float gate = track.Gate(4);
        PutOnSpeedTrial(gate - 10, 0, RoadPace);
        GrantBoost(Player, 1);
        for (int tick = 0; tick < 28; tick++) TickRace(SpeedCheckStep, true, false, 0, false, false);
        Require(Mathf.IsEqualApprox(Player.Speed, BoostSpeedCap) && dust.SuperDriftFireCount == 0,
            "an ordinary boost reaches 80 mph without a fire trail");

        ResetRace();
        PutOnSpeedTrial(gate - .20f, 0, DriftSpeedCap);
        ReleaseDriftBoost(Player, 1.30f);
        TickRace(SpeedCheckStep, true, false, 0, false, false);
        Require(Player.Distance > gate + .3f && Player.GatesCleared == 5,
            "a 100 mph pass registers its checkpoint");
        CheckHighSpeedPickups();
        GD.Print("ARCADE SPEED PASS | 60 mph cruise, 80 mph regular boost, 100 mph super drift, steering, braking, checkpoints and drift effects.");
    }

    private async Task CaptureArcadeSpeed(string folder, bool video)
    {
        StartArcadeSpeedTrial();
        SetPhysicsProcess(false);
        ChangeRaceSettings(raceSettings with { MilesPerHour = true });
        SetRaceCamera(1);
        float peak = 0;
        for (int frame = 0; frame < (video ? 114 : 72); frame++)
        {
            for (int substep = 0; substep < 2; substep++)
            {
                worldTime += SpeedCheckStep;
                TickSpeedTrial(frame * 2 + substep);
                SetRaceCamera(1 - Mathf.Exp(-8.2f * SpeedCheckStep));
                UpdateSpeedBlur(SpeedCheckStep);
            }
            peak = Math.Max(peak, Player.Speed);
            UpdateRaceUi();
            if (video) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            else if (frame is 34 or 71) await SaveFrame(Path.Combine(folder, frame == 34 ? "Drift-Smoke.png" : "100-MPH-Super-Drift.png"));
        }
        Require(Mathf.IsEqualApprox(peak, DriftSpeedCap) && !Player.Falling, "the capture reaches the actual 100 mph cap");
        GD.Print("ARCADE SPEED CAPTURE PASS");
    }
}
