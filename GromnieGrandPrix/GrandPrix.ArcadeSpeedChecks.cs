using Godot;
using GromnieOnline;

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
        float runwayStart = Player.Distance;
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
            Require(Player.Speed <= DriftSpeedCap + .001f, "ground speed stays within the 90 mph cap");
        }
        Require(Mathf.IsEqualApprox(peak, DriftSpeedCap) && HasSuperDrift(Player) && dust.SuperDriftFireCount > 0 &&
                !Player.FreeDriving && !Player.Falling, $"a real super drift reaches 90 mph and leaves fire (peak {peak / MetresPerSecondPerMph:F1} mph)");
        ChangeRaceSettings(raceSettings with { MilesPerHour = true });
        UpdateRaceSpeedUi();
        Require(speedText.Text == "90 mph", "the speedometer shows the physical drift speed cap");

        for (int tick = 0; tick < 90; tick++) TickRace(SpeedCheckStep, false, true, 0, false, false);
        Require(Player.Speed <= .01f && !Player.Falling && !HasSuperDrift(Player), "braking to a stop is controlled and the fire boost expires");

        ResetRace();
        float gate = track.Gate(4);
        PutOnSpeedTrial(gate - 10, 0, RoadPace);
        GrantBoost(Player, 1);
        for (int tick = 0; tick < 28; tick++) TickRace(SpeedCheckStep, true, false, 0, false, false);
        Require(Mathf.IsEqualApprox(Player.Speed, 75 * MetresPerSecondPerMph) && dust.SuperDriftFireCount == 0,
            "an ordinary boost reaches 75 mph without a fire trail");

        ResetRace();
        PutOnSpeedTrial(gate - .20f, 0, DriftSpeedCap);
        ReleaseDriftBoost(Player, 1.30f);
        TickRace(SpeedCheckStep, true, false, 0, false, false);
        Require(Player.Distance > gate + .3f && Player.GatesCleared == 5,
            "a 90 mph pass registers its checkpoint");
        CheckMountExhaustion(runwayStart, gate);
        CheckHighSpeedPickups();
        GD.Print("ARCADE SPEED PASS | 60 mph cruise, 75 mph regular boost, 90 mph super drift, steering, braking, checkpoints, drift effects and mount recovery.");
    }

    private void CheckMountExhaustion(float runwayStart, float gate)
    {
        ResetRace();
        PutOnSpeedTrial(runwayStart, 0, BoostSpeedCap);
        UseRacePower(Player, RaceItem.Invulnerability);
        for (int tick = 0; tick < 288; tick++) TickRace(SpeedCheckStep, true, false, 0, false, false);
        float warmStrain = Player.BoostStrain;
        Require(!Player.MountUnavailable && warmStrain is > 4.5f and < 5f,
            $"a single 4.8-second invulnerability boost is safe from zero strain ({warmStrain:F2} s)");

        for (int tick = 0; tick < 15; tick++) TickRace(SpeedCheckStep, true, false, 0, false, false);
        Require(Player.BoostStrain > 0 && Player.BoostStrain < warmStrain,
            "ordinary running pace cools some of the accumulated strain");

        GrantBoost(Player, 1.2f);
        for (int tick = 0; tick < 72 && !Player.MountUnavailable; tick++)
            TickRace(SpeedCheckStep, true, false, 0, false, false);
        Require(Player.MountUnavailable && Mathf.IsEqualApprox(Player.MountRecovery, 20f) && Player.RemountTime == 0,
            "chained boosts exhaust the mount and start a twenty-second recovery");
        Require(Player.Rider.GetParent() == Player.Root && Player.Mount.TopLevel && Player.Mount.Visible,
            "the rider is on foot while the native mount collapse stays behind on the course");
        Require(Player.Boost == 0 && Player.SuperDriftUntil == 0 && Player.BoostStrain == 0,
            "mount exhaustion clears active boost, super drift and strain");

        var replica = racers[1];
        var detached = Wire.Packet("racer", RacerState(Player, 0)).Read<OnlineRacer>()!;
        ApplyOnlineRacer(replica, detached);
        Require(replica.MountUnavailable && Mathf.IsEqualApprox(replica.MountRecovery, Player.MountRecovery),
            "the native racer snapshot carries mount recovery to peers");

        GrantBoost(Player, 2.25f);
        ReleaseDriftBoost(Player, 1.30f);
        Require(Player.Boost == 0 && Player.SuperDriftUntil == 0 && !WantsGroundDrift(Player, true, 1, false),
            "a rider on foot cannot receive or charge a mount boost");

        float onFootPace = 45 * MetresPerSecondPerMph;
        PutOnSpeedTrial(gate - .20f, 0, onFootPace);
        TickRace(SpeedCheckStep, true, false, 0, false, false);
        Require(Mathf.IsEqualApprox(Player.Speed, onFootPace) && Player.Distance > gate && Player.GatesCleared == 5 &&
                Player.MountRecovery < 20f && !Player.FreeDriving && !Player.Falling,
            "a grounded rider runs at 45 mph and advances through a checkpoint during recovery");

        TickMountRecovery(Player, Player.MountRecovery - .10f);
        Require(Player.MountUnavailable && Player.MountRecovery > .09f && Player.RemountTime == 0,
            "the rider stays on foot through the twenty-second recovery");
        TickMountRecovery(Player, Player.MountRecovery);
        Require(Player.MountUnavailable && Player.MountRecovery == 0 && Mathf.IsEqualApprox(Player.RemountTime, .65f),
            "remounting starts automatically and takes 0.65 seconds");
        Player.Draw(track, 0, true);
        float low = Player.Rider.Position.Y;
        var remount = Wire.Packet("racer", RacerState(Player, 0)).Read<OnlineRacer>()!;
        ApplyOnlineRacer(replica, remount);
        Require(replica.MountUnavailable && replica.MountRecovery == 0 && Mathf.IsEqualApprox(replica.RemountTime, Player.RemountTime),
            "the native racer snapshot carries the remount phase to peers");

        TickMountRecovery(Player, Player.RemountTime * .5f);
        Player.Draw(track, 0, true);
        Require(Player.Rider.GetParent() == Player.Root && Player.Rider.Position.Y > low + .35f && Player.Mount.Visible,
            "the original mount reappears and the rider hops toward its saddle");
        TickMountRecovery(Player, Player.RemountTime);
        Player.Draw(track, 0, true);
        Require(Player.Rider.GetParent() != Player.Root && !Player.Mount.TopLevel && Player.Mount.Visible,
            "the rider reattaches to the original rig after the jump");
        GrantBoost(Player, 1f);
        Require(!Player.MountUnavailable && Mathf.IsEqualApprox(Player.Boost, 1f),
            "boosts are available immediately after the automatic remount");
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
            else if (frame is 34 or 71) await SaveFrame(Path.Combine(folder, frame == 34 ? "Drift-Smoke.png" : "90-MPH-Super-Drift.png"));
        }
        Require(Mathf.IsEqualApprox(peak, DriftSpeedCap) && !Player.Falling, "the capture reaches the actual 90 mph cap");
        GD.Print("ARCADE SPEED CAPTURE PASS");
    }
}
