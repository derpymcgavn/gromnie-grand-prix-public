using Godot;

public partial class GrandPrix
{
    private void StartIslandCheck()
    {
        PopulateCoursePicker(IslandWaterCourseName);
        Require(courseList.ItemCount == 1 && courseList.GetItemMetadata(0).AsInt32() == IslandWaterCourseId,
            "the island regatta is available in the course picker");
        ChooseCourse(IslandWaterCourseId);
        StartRace();
        Require(racing && activeTown == IslandWaterCourseId && track.IsWaterCourse,
            "the island regatta loads its native terrain: " + courseProblem);
        countdown = 0;
    }

    private void CheckIslandRegatta()
    {
        StartIslandCheck();
        Require(track.Length is > 6900 and < 7950 && track.Laps == 1 && track.HalfWidth >= 22 &&
            track.TightestInsideRadius() > 140 && track.Point(0).DistanceTo(track.Point(track.Length - .04f)) < .12f,
            "the marked water channel winds around the island without a seam or sharp turns");
        float level = track.WaterCourseLevel!.Value;
        for (float station = 0; station < track.Length; station += 8)
            foreach (float lane in new[] { -track.HalfWidth, 0, track.HalfWidth })
                Require(track.NavigableWater(track.Surface(station, lane)), "the full width of the closed loop stays afloat");
        Require(racers.All(racer => racer.UsesJetSki && !racer.Mount.Visible &&
            racer.Root.GetNode<MeshInstance3D>("Gear Knight jet ski/Gear Knight armor fairing").MaterialOverride
                is StandardMaterial3D { AlbedoTexture: not null }),
            "every racer has a real Gear Knight-textured jet ski");
        Require(!LaunchFlight(Player) && pickupItems.All(IslandWaterPickup),
            "the regatta keeps its racers on their skis");
        Require(track.TryFreeMove(track.Surface(100, track.HalfWidth), track.Surface(101, track.HalfWidth + 1), out var afloat) &&
                Math.Abs(afloat.Y - level) < .01f && !track.TryFreeMove(afloat, IslandShowAnchor, out _),
            "open water supports free movement and the native shore blocks the ski");

        float testSteering = 0;
        for (int i = 0; i < 180; i++) MoveHuman(Player, ref testSteering, 1f / 60, true, false, 0, false);
        Require(Mathf.IsEqualApprox(Player.Speed, JetSkiPace), "the jet ski reaches 100 mph on open throttle");
        GrantBoost(Player, 1.14f);
        for (int i = 0; i < 30; i++) MoveHuman(Player, ref testSteering, 1f / 60, true, false, 0, false);
        Require(Mathf.IsEqualApprox(Player.Speed, JetSkiBoostCap), "the pickup boost reaches 125 mph");
        ReleaseDriftBoost(Player, 1.38f);
        for (int i = 0; i < 30; i++) MoveHuman(Player, ref testSteering, 1f / 60, true, false, 0, false);
        Require(Mathf.IsEqualApprox(Player.Speed, JetSkiDriftCap), "a charged drift reaches 145 mph");
        CheckJetSkiWaterContact();
        CheckRegattaFluid();
        CheckJesusMode();
        ResetRace();
        countdown = 0;
        Require(islandGolems.Any(site => site.Mud) && islandGolems.Any(site => !site.Mud),
            "both native golem types spawn as course hazards");
        var water = islandGolems.First(site => !site.Mud);
        Player.Distance = water.Distance - 12;
        Player.Lane = 0;
        elapsed = water.Period + IslandGolemQuiet + .45f - water.Offset;
        TickIslandGolems(0);
        Require(water.Sign.Visible && water.Ring.Visible && !water.Dangerous,
            "the water golem warns approaching racers before entering the channel");
        elapsed += IslandGolemWarning + IslandGolemRise;
        TickIslandGolems(0);
        Require(water.Actor.Visible && water.Dangerous, "the native water golem breaches into the track");
        Vector3 across = track.Forward(water.Distance) * 2;
        Player.Ward = 0;
        CheckIslandGolemHits(Player, 0, water.Before - across, water.Position + across);
        Require(Player.EjectTime > 0, "hitting the golem ejects a racer from their ski");
        racers[1].Ward = 3;
        CheckIslandGolemHits(racers[1], 1, water.Before - across, water.Position + across);
        Require(racers[1].EjectTime <= 0 && racers[1].Ward <= 0, "a Portal Ward absorbs the golem hit");

        ResetRace();
        Require(islandShow is { } && islandShow.Targets.All(target => target.BodyMeshes.Any(mesh =>
            mesh.MaterialOverride is StandardMaterial3D { AlbedoTexture: not null })),
            "the range has three Olthoi dummies fitted with native straw texture");
        var show = islandShow!;
        Require(show.Mage.Part(9).GetChildren().OfType<MeshInstance3D>().Any(mesh =>
            mesh.GetSurfaceOverrideMaterial(0) is ShaderMaterial),
            "Asheron's native robe uses the white linen dye");
        for (int step = 0; step < 90 && !show.Dancing; step++) TickIslandShow(1f / 60);
        Require(show.Dancing, "Asheron plays a native dance at the start");
        for (int step = 0; step < 1800 && show.TargetFlinch <= 0; step++) TickIslandShow(1f / 60);
        Require(show.TargetFlinch > 0, "Asheron casts a practice spell that strikes an Olthoi dummy");

        ResetRace();
        countdown = 0;
        int gates = 0;
        for (int frame = 0; frame < Mathf.CeilToInt((track.Length / (JetSkiPace * .68f) + 55) * 60) && !finished; frame++)
        {
            bool sudsed = IsSudsed(Player);
            float steer = Mathf.Clamp(-Player.Lane * (sudsed ? -.45f : .45f), -1, 1);
            TickRace(1f / 60, !sudsed, sudsed, steer, false, false);
            Require(Player.GatesCleared >= gates && Player.GatesCleared <= gates + 1, "regatta progress records each checkpoint in order");
            gates = Player.GatesCleared;
        }
        Require(finished && Player.Finish > 0 && gates == track.GateCount + 1 && Player.LapCrossings.Count == 1,
            $"the full lap reaches the official finish ({Player.Distance:F0} m and {gates} gates)");
        GD.Print($"ISLAND REGATTA PASS | {track.Length:F0} m | {islandGolems.Count} golems | {Player.Finish:F1}s full lap | 100/125/145 mph | DAT skis, ward, dance and spell impact verified.");
        OpenStable();
    }

    private void CheckJetSkiWaterContact()
    {
        ResetRace();
        countdown = 0;
        const float step = 1f / 60;
        Vector3 station = track.Surface(56, 0);
        Vector3 heading = new Vector3(-1, 0, -.45f).Normalized();
        float idleMin = float.MaxValue, idleMax = float.MinValue;
        for (int frame = 0; frame < 300; frame++)
        {
            Player.StepJetSkiWater(station, heading, frame * step, step, 0, false);
            Require(!Player.SkiAirborne, "a stopped ski floats without bouncing off the water");
            idleMin = Math.Min(idleMin, Player.SkiHeave);
            idleMax = Math.Max(idleMax, Player.SkiHeave);
        }
        Require(idleMax - idleMin > .14f, "the idle ski rises and falls with passing swells");

        Player.ResetJetSkiWater();
        Player.Speed = JetSkiDriftCap;
        float longestSkip = 0, currentSkip = 0, largestImpact = 0, bank = 0;
        int landings = 0;
        for (int frame = 0; frame < 900; frame++)
        {
            float turn = frame < 145 ? .7f : 0;
            Vector3 point = station + heading * (frame * step * Player.Speed);
            float impact = Player.StepJetSkiWater(point, heading, frame * step, step, turn, turn != 0);
            currentSkip = Player.SkiAirborne ? currentSkip + step : 0;
            longestSkip = Math.Max(longestSkip, currentSkip);
            largestImpact = Math.Max(largestImpact, impact);
            if (impact > 0) landings++;
            bank = Math.Max(bank, -Player.SkiRoll);
            Require(float.IsFinite(Player.SkiHeave) && Math.Abs(Player.SkiHeave) < 1.5f,
                "boosted hull travel stays bounded");
            Require(!Player.SkiAirborne || Player.SkiWaterContact == 0 && Player.SkiSternContact == 0,
                "airborne skis lose thrust and stop emitting a wet wake");
        }
        Require(longestSkip is > .06f and < .75f && largestImpact >= 2.1f && landings >= 2 && bank > .12f,
            $"super-drift skis bank, skip and land ({longestSkip:F2}s skip, {largestImpact:F1} m/s impact, {landings} impacts, {bank:F2} rad bank)");
        var ski = RacerState(Player, 0).Ski;
        Require(ski is not null && ski.Heave == Player.SkiHeave && ski.Airborne == Player.SkiAirborne,
            "the host snapshot carries the authoritative water pose");
        RecallRacer(Player, "");
        Require(!Player.SkiAirborne && Player.SkiWaterSpeed == 0 && Player.ResetHold > 0,
            "recall resets the hull without a false landing or launch");
        GD.Print($"REGATTA PHYSICS PASS | {idleMax - idleMin:F2} m idle bob | {longestSkip:F2}s longest skip | {landings} impacts, peak {largestImpact:F1} m/s | {Mathf.RadToDeg(bank):F0}° peak bank");
    }

    private void CheckRegattaFluid()
    {
        PrepareJesusModeCheck();
        // Place the impact next to a tile edge, then sample both sides as the wave spreads.
        Vector2 impact = new(-12, 12.8f);
        regattaFluid.Disturb(impact, 2.6f, -.28f);
        Require(regattaFluid.HeightOffset(impact) < -.20f, "a landing first depresses the surface");
        Vector2 east = impact + Vector2.Right * 5.6f, west = impact + Vector2.Left * 5.6f;
        Require(regattaFluid.HeightOffset(east) == 0 && regattaFluid.HeightOffset(west) == 0,
            "a ripple reaches distant water through propagation");
        float eastPeak = 0, westPeak = 0;
        for (int i = 0; i < 60; i++)
        {
            regattaFluid.Step(1f / 30);
            eastPeak = Math.Max(eastPeak, Math.Abs(regattaFluid.HeightOffset(east)));
            westPeak = Math.Max(westPeak, Math.Abs(regattaFluid.HeightOffset(west)));
        }
        Require(eastPeak > .009f && westPeak > .009f && Math.Abs(eastPeak - westPeak) < .014f,
            $"impact ripples cross tile boundaries ({eastPeak:F3} m east, {westPeak:F3} m west)");
        var patch = new float[25];
        int x = -15, z = 16;
        regattaFluid.CopyHeightPatch(x - 2, z - 2, 5, 5, patch);
        Require(Math.Abs(patch[12] - regattaFluid.HeightOffset(new Vector2(x, z) * RegattaFluidField.CellSize)) < .0001f,
            "the rendered patch uses the same world-space samples as the hull");

        ResetRegattaFluid();
        Require(regattaFluid.HeightOffset(impact) == 0, "clearing the race leaves a calm background");
        const float step = 1f / 60;
        Player.Speed = 43;
        for (int i = 0; i < 140; i++)
        {
            Player.Distance = 56 + i * step * Player.Speed;
            TickRegattaFluid(step);
        }

        float wetPeak = 0;
        Vector3 crest = Vector3.Zero;
        foreach (float behind in new[] { 4f, 6, 9, 12, 16 })
        for (float lane = -4; lane <= 4; lane += .4f)
        {
            Vector3 point = track.Surface(Player.Distance - behind, lane);
            float offset = Math.Abs(regattaFluid.HeightOffset(new Vector2(point.X, point.Z)));
            if (offset <= wetPeak) continue;
            wetPeak = offset;
            crest = point;
        }
        Require(wetPeak > .025f, $"a moving ski leaves a displaced wake behind it ({wetPeak:F3} m)");
        var follower = racers[1];
        Player.ResetJetSkiWater();
        follower.ResetJetSkiWater();
        Player.Speed = follower.Speed = 23;
        Vector3 across = (track.Right(Player.Distance) * .82f + track.Forward(Player.Distance) * .58f).Normalized();
        float rollDifference = 0, pitchDifference = 0, heaveDifference = 0;
        for (int i = 0; i < 32; i++)
        {
            Vector3 point = crest + across * ((i - 9) * step * follower.Speed);
            Player.StepJetSkiWater(point, across, RegattaWaterTime + i * step, step, 0, false);
            follower.StepJetSkiWater(point, across, RegattaWaterTime + i * step, step, 0, false, regattaFluid);
            rollDifference = Math.Max(rollDifference, Math.Abs(follower.SkiRoll - Player.SkiRoll));
            pitchDifference = Math.Max(pitchDifference, Math.Abs(follower.SkiPitch - Player.SkiPitch));
            heaveDifference = Math.Max(heaveDifference, Math.Abs(follower.SkiHeave - Player.SkiHeave));
        }
        Require(Math.Max(rollDifference, pitchDifference) > .012f && heaveDifference > .010f,
            $"a crossing ski reacts to the displaced surface ({wetPeak:F3} m wake, {Mathf.RadToDeg(rollDifference):F1}° roll, {Mathf.RadToDeg(pitchDifference):F1}° pitch, {heaveDifference:F3} m heave)");
        GD.Print($"REGATTA FLUID PASS | {Math.Max(eastPeak, westPeak):F3} m ripple after crossing a tile | {wetPeak:F2} m wake | {Mathf.RadToDeg(rollDifference):F1}° crossing roll, {Mathf.RadToDeg(pitchDifference):F1}° pitch, {heaveDifference:F2} m heave");
    }

    private void PrepareJesusModeCheck()
    {
        ResetRace();
        countdown = 0;
        Player.Distance = 56;
        Player.Lane = 0;
        Player.Speed = JetSkiPace;
        Player.GatesCleared = 1;
        Player.Checkpoint = track.Gate(0);
    }

    private void DriveJetSkiDrift(int index)
    {
        float steer = index % 2 == 0 ? -.2f : .2f;
        int frames = index % 2 == 0 ? 52 : 80;
        for (int frame = 0; frame < frames; frame++)
            TickRace(1f / 60, true, false, steer, true, false);
        TickRace(1f / 60, true, false, 0, false, false);
    }

    private void CheckJesusMode()
    {
        PrepareJesusModeCheck();
        ReleaseDriftBoost(Player, .78f);
        elapsed += SkiDriftChainWindow + .01f;
        GrantBoost(Player, 1);
        ReleaseDriftBoost(Player, .3f);
        ReleaseDriftBoost(Player, .78f);
        Require(Player.SkiDriftStreak == 1, "a break clears the streak and only successful drift boosts count");
        RecallRacer(Player, "");
        Require(Player.SkiDriftStreak == 0, "a checkpoint recall clears the streak");

        PrepareJesusModeCheck();
        for (int drift = 0; drift < 3; drift++)
        {
            DriveJetSkiDrift(drift);
            Require(Player.SkiDriftStreak == drift + 1 && Player.Boost > 0 && !Player.MountUnavailable,
                "the first three consecutive drifts still boost the ski");
        }
        DriveJetSkiDrift(3);
        Require(Player.MountRecovery > 19.9f && Player.JetSkiThrowAge >= 0 && Player.Boost == 0 &&
            Player.SuperDriftUntil == 0 && message == JesusModeMessage && Player.Rider.GetParent() == Player.Root,
            "the fourth drift throws the rider and announces JESUS MODE!");
        var replica = racers[1];
        ApplyOnlineRacer(replica, RacerState(Player, 0));
        replica.Draw(track, 0, true);
        Require(replica.JetSkiThrowAge >= 0 && replica.Rider.GetParent() == replica.Root,
            "the race snapshot reproduces the throw on a guest");
        replica.Finish = elapsed;

        float before = Player.Distance;
        for (int frame = 0; frame < 105; frame++)
            TickRace(1f / 60, true, false, Mathf.Clamp(-Player.Lane * .35f, -.4f, .4f), false, false);
        Vector3 point = Player.CoursePosition(track);
        Require(Player.JetSkiThrowAge < 0 && Player.Distance > before + 25 && !Player.Falling &&
            Player.Rider.GetParent() == Player.Root && !Player.Saddle.IsVisibleInTree() &&
            Math.Abs(Player.Root.Position.Y - point.Y - WaveAt(point, RegattaWaterTime)) < .10f &&
            Mathf.IsEqualApprox(GroundPace(Player), JetSkiPace * .75f),
            "the rider races on the moving water at three quarters of ski pace");
        TickMountRecovery(Player, Player.MountRecovery);
        Player.Draw(track, 0, true);
        Require(Player.RemountTime > 0 && Player.Saddle.IsVisibleInTree(), "the ski returns for the remount hop");
        TickMountRecovery(Player, Player.RemountTime);
        Player.Draw(track, 0, true);
        Require(!Player.MountUnavailable && Player.Saddle.IsAncestorOf(Player.Rider) && Player.SkiDriftStreak == 0,
            "after recovery the rider reseats on the ski with a fresh streak");
        GD.Print("JESUS MODE PASS | fourth drift throws; replica, water run, recovery and remount verified.");
    }

    private async Task CaptureIslandRegatta(string folder)
    {
        StartIslandCheck();
        SetPhysicsProcess(false);
        ClearSpeedBlur();
        raceUi.Hide();
        float station = 56;
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            racer.Distance = station + (i - 2) * 1.3f;
            racer.Lane = (i - 2) * 2.5f;
            racer.Speed = JetSkiPace * .90f;
            racer.GatesCleared = 1;
            racer.Checkpoint = track.Gate(0);
            racer.ResetJetSkiWater();
            TickJetSkiWater(racer, 0, 0, false);
            racer.Draw(track, .1f, true);
        }
        camera.Far = 1850;
        camera.Fov = 63;
        camera.GlobalPosition = track.Surface(station - 6.8f, 12.9f) + Vector3.Up * 3.45f;
        camera.LookAt(track.Surface(station + 1.2f, -1) + Vector3.Up * .65f);
        UpdateIslandWaterReflection();
        await SaveFrame(Path.Combine(folder, "Gear-Knight-Regatta.png"));

        var show = islandShow!;
        BeginIslandShowAct(show, true);
        for (int i = 0; i < 180 && show.Projectile is null; i++) TickIslandShow(1f / 60);
        Require(show.Projectile is not null, "the practice range capture has a native spell in flight");
        for (int i = 0; i < 10; i++) TickIslandShow(1f / 60);
        Vector3 landward = FlatDirection(show.Audience, show.Root.Position).Normalized();
        camera.Fov = 49;
        camera.GlobalPosition = show.Root.Position - landward * 12 + landward.Cross(Vector3.Up) * 13 + Vector3.Up * 5.8f;
        camera.LookAt(show.Root.Position + landward * 4 + Vector3.Up * 2.55f);
        UpdateIslandWaterReflection();
        await SaveFrame(Path.Combine(folder, "Asheron-Practice-Range.png"));

        // Drive the actual ground controls long enough for the wakes and spray to spread.
        float peak = 0;
        bool skipCaptured = false, landingCaptured = false;
        bool recordMotion = OS.GetCmdlineUserArgs().Contains("--regatta-motion");
        string motionFolder = Path.Combine(folder, "motion");
        if (recordMotion) Directory.CreateDirectory(motionFolder);
        for (int frame = 0; frame < 175; frame++)
        {
            bool wasAirborne = Player.SkiAirborne;
            bool carve = frame < 44;
            for (int tick = 0; tick < 2; tick++)
            {
                float steer = carve ? -.24f : Mathf.Clamp(-Player.Lane * .22f, -.35f, .35f);
                TickRace(1f / 60, true, false, steer, carve, false);
                peak = Math.Max(peak, Player.Speed);
            }
            SetRaceCamera(frame == 0 ? 1 : .28f);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (recordMotion)
            {
                using var image = GetViewport().GetTexture().GetImage();
                Require(image.SavePng(Path.Combine(motionFolder, $"{frame:D4}.png")) == Error.Ok, "regatta motion frame");
            }
            if (frame == 36) await SaveFrame(Path.Combine(folder, "Regatta-Hard-Carve.png"));
            if (frame == 70) await SaveFrame(Path.Combine(folder, "Regatta-Super-Drift.png"));
            if (!skipCaptured && Player.SkiAirborne && Player.Speed > JetSkiPace - 2)
            {
                skipCaptured = true;
                await SaveFrame(Path.Combine(folder, "Regatta-Crest-Skip.png"));
            }
            if (!landingCaptured && skipCaptured && wasAirborne && !Player.SkiAirborne && Player.Speed > JetSkiPace - 2)
            {
                landingCaptured = true;
                await SaveFrame(Path.Combine(folder, "Regatta-Water-Contact.png"));
            }
        }
        Require(peak >= JetSkiDriftCap - .1f && !Player.Falling, "the filmed ski reaches the super-drift cap under live controls");

        foreach (bool mud in new[] { false, true })
        {
            var golem = islandGolems.First(site => site.Mud == mud);
            Player.Distance = golem.Distance - 5;
            elapsed = golem.Period + IslandGolemQuiet + IslandGolemWarning + IslandGolemRise + .52f - golem.Offset;
            TickIslandGolems(1f / 60);
            TickIslandSplashes(.16f);
            Player.Lane = Mathf.Clamp(IslandGolemAt(golem, elapsed).Lane + 2, -track.HalfWidth, track.HalfWidth);
            Player.ResetJetSkiWater();
            UpdateRegattaSurface();
            TickJetSkiWater(Player, 0, 0, false);
            Player.Draw(track, .1f, true);
            Require(golem.Actor.IsVisibleInTree() && golem.Actor.BodyMeshes.Any(mesh => mesh.IsVisibleInTree()),
                "the native golem is visible above the water during the crossing");
            camera.Fov = 59;
            camera.GlobalPosition = track.Surface(golem.Distance - 8, Player.Lane + 5.5f) + Vector3.Up * 3.45f;
            camera.LookAt(golem.Position + Vector3.Up * 1.3f);
            UpdateIslandWaterReflection();
            await SaveFrame(Path.Combine(folder, mud ? "Mud-Golem-Crossing.png" : "Water-Golem-Crossing.png"));
        }
        PrepareJesusModeCheck();
        for (int drift = 0; drift < 4; drift++) DriveJetSkiDrift(drift);
        Require(Player.JetSkiThrowAge >= 0 && message == JesusModeMessage, "the captured throw comes from four live drifts");
        for (int frame = 0; frame < 23; frame++) TickRace(1f / 60, true, false, 0, false, false);
        FrameJesusMode();
        await SaveFrame(Path.Combine(folder, "Jesus-Mode-Throw.png"));
        for (int frame = 0; frame < 49; frame++) TickRace(1f / 60, true, false, 0, false, false);
        FrameJesusMode();
        await SaveFrame(Path.Combine(folder, "Jesus-Mode.png"));
        GD.Print("ISLAND REGATTA CAPTURE PASS");

        void FrameJesusMode()
        {
            Vector3 heading = Player.Heading(track), right = heading.Cross(Vector3.Up);
            Vector3 focus = Player.Root.Position + Vector3.Up * 1.25f;
            camera.GlobalPosition = focus - heading * 5 + right * 4.7f + Vector3.Up * 1.9f;
            camera.Fov = 51;
            camera.LookAt(focus);
            UpdateIslandWaterReflection();
            raceUi.Show();
            UpdateRaceUi();
        }
    }
}
