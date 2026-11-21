using Godot;

public partial class GrandPrix
{
    private void CheckLandmarkRaces(string? courseName = null)
    {
        Require(landmarkCircuits.Count == 6, "the six landmark circuits are available");
        foreach (var circuit in LandmarkCoursesForCheck(courseName))
        {
            PopulateCoursePicker(circuit.Name);
            Require(courseList.ItemCount == 1 && courseList.GetItemMetadata(0).AsInt32() == circuit.Id,
                $"{circuit.Name} can be found by name");
            OpenStable();
            ChooseCourse(circuit.Id);
            StartRace();
            Require(activeTown == circuit.Id && track.NativeBlockCount == circuit.GridSide * circuit.GridSide && track.GateCount == 16 &&
                track.Laps == 1, $"{circuit.Name} loads its native grid and normal XL checkpoints");
            Require(course.GetNode("Landmark signs").GetChildCount() == circuit.Landmarks.Length,
                $"{circuit.Name} posts all {circuit.Landmarks.Length} landmark signs");
            if (circuit.Battle is not null)
            {
                CheckCitadelBattle();
                ResetRace();
            }

            const float step = 1f / 60;
            for (int i = 0; i < track.Length * 7 && !finished; i++)
            {
                bool flipped = IsSudsed(Player);
                float lane = circuit.Battle is not null && flyover is { } route && Player.Distance < route.End &&
                    Player.Distance > route.Start - 85 ? track.HalfWidth * .62f : 0;
                float steer = Mathf.Clamp((lane - Player.Lane) * (flipped ? -.29f : .29f), -1, 1);
                TickRace(step, !flipped, flipped, steer, false, false);
            }
            Require(finished && Player.GatesCleared == track.GateCount + 1 && bestByCourse.ContainsKey(circuit.Id),
                $"{circuit.Name} reaches the results through every checkpoint ({Player.Distance:F0} m, gate {Player.GatesCleared})");
            GD.Print($"LANDMARK RACE PASS | {circuit.Name} | {track.Length:F0} m | {Player.Finish:F1}s.");
            OpenStable();
        }
        ChooseCourse(0);
    }

    private void CheckCitadelBattle()
    {
        const float step = 1f / 60;
        Require(citadel is { Party.Count: 5 } && citadel.Boss.Setup.Id == AerbaxSetup &&
            flyover is { CitadelFlyby: not null }, "the native Aerbax and five mock players stage the citadel flyover");
        var route = flyover!.Value;
        countdown = 0;
        float start = Player.Distance;
        PressRoll();
        TickRace(step, false, false, 0, false, false);
        Require(Player.Distance == start && Player.ResetHold <= 0, "R does not reset a grounded racer");
        PutPlayerAtLaunch(route);
        TickRace(step, true, false, 0, false, false);
        Require(Player.Flight is { CitadelFlyby: not null }, "the normal launch pad starts the citadel flight");
        for (int frame = 0; frame < 1800 && Player.Flight is not null; frame++)
            TickRace(step, true, false, 0, false, false);
        Require(citadel!.Party.Any(actor => actor.LastActionAnimations.Length > 0) &&
            Player.EjectTime > 0 && Player.Rider.TopLevel &&
            Player.EjectMotion.Launch.Origin.Y - track.Point(Player.Distance).Y > 30,
            "the actors cast and a live stray projectile unseats an exposed rider in midair");
        for (int frame = 0; frame < 90 && Player.EjectTime > 0; frame++) TickRace(step, false, false, 0, false, false);
        Require(Player.ResetHold > 0 && !Player.Rider.TopLevel && Player.Distance == Player.Checkpoint,
            "an aerial knockoff rejoins the race at the checkpoint");

        ResetRace();
        PutPlayerAtLaunch(route);
        TickRace(step, true, false, 0, false, false);
        for (int frame = 0; frame < 2000 && Player.Flight is not null; frame++)
        {
            float bank = citadel.WarnedRacers.Contains(Player) ? -1 : 0;
            TickRace(step, true, false, bank, false, false);
        }
        Require(Player.EjectTime <= 0 && Player.Flight is null && Player.Distance >= route.End - .1f,
            "banking clears live spellfire and reaches the normal landing");

        ResetRace();
        PutPlayerAtLaunch(route);
        TickRace(step, true, false, 0, false, false);
        bool pressed = false, spun = false, dodged = false;
        for (int frame = 0; frame < 2000 && Player.Flight is { } flight; frame++)
        {
            var bolt = citadel.Bolts.FirstOrDefault(shot => shot.StrayFor == Player);
            if (!pressed && bolt is not null && elapsed >= bolt.CastAt + .73f)
            {
                PressRoll();
                pressed = true;
            }
            TickRace(step, true, false, 0, false, false);
            spun |= Math.Abs(flight.RollAngle) > 1.5f;
            dodged |= bolt is not null && (bolt.RollDodgeMask & 1) != 0;
        }
        Require(pressed && spun && dodged && Player.EjectTime <= 0 && Player.Distance >= route.End - .1f,
            "a timed R barrel roll spins the mount, dodges live spellfire and lands");
        GD.Print("CITADEL BATTLE PASS | native platform and cast, aerial dismount, banking and R barrel-roll dodge.");

        void PressRoll()
        {
            _Input(new InputEventKey { PhysicalKeycode = Key.R, Pressed = true });
            _Input(new InputEventKey { PhysicalKeycode = Key.R, Pressed = false });
        }
    }

    private void StartCitadelCheckRace()
    {
        OpenStable();
        int id = landmarkCircuits.Values.Single(circuit => circuit.Battle is not null).Id;
        ChooseCourse(id);
        StartRace();
        Require(activeTown == id, "Aerbax's Citadel opens from the course picker");
    }

    private LandmarkCircuit[] LandmarkCoursesForCheck(string? courseName) => courseName is null
        ? landmarkCircuits.Values.ToArray()
        : [landmarkCircuits.Values.Single(circuit => circuit.Name == courseName)];

    private async Task CaptureLandmarkRaces(string folder, string? courseName = null)
    {
        foreach (var circuit in LandmarkCoursesForCheck(courseName))
        {
            OpenStable();
            ChooseCourse(circuit.Id);
            StartRace();
            Require(activeTown == circuit.Id, $"{circuit.Name} opens from the course picker");
            paused = true;
            countdown = 0;
            if (circuit.Battle is not null)
            {
                await CaptureCitadelBattle(folder);
                continue;
            }
            var subject = circuit.Id == 11106 ? circuit.Landmarks.Single(landmark => landmark.Name == "Eastern Lighthouse")
                : circuit.Landmarks[circuit.Id == 11103 ? 1 : 0];
            Vector2 point = subject.Point;
            var projection = track.Project(new Vector3(point.X, 0, point.Y), track.Length * .5f);
            Player.Distance = Mathf.PosMod(projection.Distance - 22, track.Length);
            Player.Lane = -Math.Sign(projection.Lane) * 3.2f;
            Player.Speed = 20;
            Player.GatesCleared = 1 + (int)(Player.Distance / (track.Length / track.GateCount));
            Player.Draw(track, .17f, true);
            Vector3 forward = Player.Heading(track), right = Player.Right(track);
            camera.Position = Player.CoursePosition(track) - forward * 13 + right * -Math.Sign(projection.Lane) * 2.6f + Vector3.Up * 6.2f;
            camera.Fov = 61;
            Vector3 focus = track.Point(Player.Distance + 31) + right * Math.Sign(projection.Lane) * 6 + Vector3.Up * 1.9f;
            camera.LookAt(focus);
            UpdateRaceUi();
            string name = circuit.Name.Replace(' ', '-');
            await SaveFrame(Path.Combine(folder, name + ".png"));
            await CaptureRegionalOverview(folder, name + "-Route.png");
        }
        OpenStable();
    }

    private async Task CaptureCitadelBattle(string folder)
    {
        var route = flyover!.Value;
        PutPlayerAtLaunch(route);
        TickRace(1f / 60, true, false, 0, false, false);
        Require(Player.Flight is not null, "citadel capture uses the flight pad");
        while (Player.Flight is { } flight && Player.Distance < Mathf.Lerp(route.Start, route.End, .484f))
            TickRace(1f / 60, true, false, flight.Bank, false, false);
        Require(Player.Flight is not null, "the camera approaches the mock battle in flight");
        Vector3 flyer = Player.Flight!.Position;
        Vector3 across = Player.Right(track);
        camera.Position = flyer - Player.Heading(track) * 32 - across * 22 + Vector3.Up * 15;
        camera.Fov = 71;
        camera.LookAt(flyer.Lerp(citadel!.Boss.Position + Vector3.Up * 4, .32f));
        itemEffects?.Tick(0);
        Callout("SPELLFIRE · R TO ROLL!", 1.4f);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Aerbax-Citadel-Flyover.png"));
        await CaptureRegionalOverview(folder, "Aerbax-Citadel-Route.png");
    }
}
