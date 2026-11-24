using System.Text.Json;
using Godot;
using GromnieOnline;

public partial class GrandPrix
{
    private static CustomTrack HybridTestRoute() => new(
        CustomTrack.FirstId, "Dereth Land, Sea and Sky", 0xd798ffff, 14, 16,
        [
            [-900, -495], [-900, -405], [-900, -305], [-900, -205], [-930, -120], [-1030, -80],
            [-1130, -160], [-1160, -265], [-1160, -390], [-1120, -490], [-1020, -540], [-940, -540]
        ],
        Modes:
        [
            TrackTravelMode.Ground, TrackTravelMode.Ground, TrackTravelMode.Ground, TrackTravelMode.Flight,
            TrackTravelMode.Flight, TrackTravelMode.Flight, TrackTravelMode.JetSki, TrackTravelMode.JetSki,
            TrackTravelMode.JetSki, TrackTravelMode.JetSki, TrackTravelMode.Flight, TrackTravelMode.Flight
        ]);

    private void CheckHybridAndSnowboard()
    {
        CheckHybridDraft();
        foreach (var (id, name) in SnowboardCourses)
        {
            if (SnowboardingEnabled)
            {
                CheckSnowboardDescent(id, name);
                continue;
            }

            PopulateCoursePicker(name);
            Require(courseList.ItemCount == 0 && !HasCourse(id), $"{name} is absent from the active course catalog");
            int previous = selectedTown;
            ChooseCourse(id);
            Require(selectedTown == previous, $"{name} cannot be selected for an offline or online race");
            selectedTown = id;
            Require(!PrepareRaceCourse(), $"a stale course selection cannot load {name}");
            selectedTown = previous;
        }
        if (!SnowboardingEnabled) GD.Print("SNOWBOARD COURSES DISABLED PASS");
    }

    private void CheckHybridDraft()
    {
        CustomTrack fixture = HybridTestRoute();
        OpenTrackEditor();
        LoadTrackDraft(fixture);
        Require(editorClosed && editorLand?.Block.Id == fixture.Center &&
            editorModes.SequenceEqual(fixture.Modes!) && sketch.Sections.Length == fixture.Points.Length,
            "a hybrid draft opens with all three colored travel modes (" + editorStatus.Text + ")");

        ClickTrackPoint(fixture.Controls()[0]);
        ChangeTrackTravelMode(TrackTravelMode.Flight);
        Require(editorModes[0] == TrackTravelMode.Flight && sketch.GuideModes[0] == TrackTravelMode.Flight,
            "the section option changes the selected point's outgoing leg");
        RecallShape(undoTrack, redoTrack);
        Require(editorModes[0] == TrackTravelMode.Ground, "undo restores the original section mode");
        RecallShape(redoTrack, undoTrack);
        Require(editorModes[0] == TrackTravelMode.Flight, "redo restores the changed section mode");
        RecallShape(undoTrack, redoTrack);

        CustomTrack draft = MakeCustomTrack(CustomTrack.FirstId);
        var saved = JsonSerializer.Deserialize<CustomTrack>(JsonSerializer.Serialize(draft));
        Require(saved is { IsValid: true, Modes: not null } && saved.Modes.SequenceEqual(fixture.Modes!) &&
            saved.Controls().SequenceEqual(fixture.Controls()), "a saved hybrid track round-trips section modes and points");
        string legacyJson = JsonSerializer.Serialize(draft with { Modes = null });
        var legacy = JsonSerializer.Deserialize<CustomTrack>(legacyJson);
        Require(legacy is { IsValid: true, Modes: null }, "an older track without travel modes is still valid");
        LoadTrackDraft(legacy);
        Require(editorModes.Count == fixture.Points.Length && editorModes.All(mode => mode == TrackTravelMode.Ground) &&
            MakeCustomTrack(CustomTrack.PreviewId).Modes is null, "older tracks default to ground in the editor");
        LoadTrackDraft(saved);
        int previousCourse = selectedTown;
        TestCustomTrack();
        Require(racing && track.IsCustom && track.HasHybridTravel && track.HasWaterSections &&
            activeTown == CustomTrack.PreviewId && track.TravelSections.Count == 4,
            "Test Drive loads the native hybrid track (" + editorStatus.Text + ")");
        countdown = 0;

        var transitions = new List<TrackTravelMode>();
        var snapshots = new Dictionary<TrackTravelMode, OnlineRacer>();
        var rider = Player.Rider;
        int gates = 0;
        int limit = Mathf.CeilToInt((track.Length / (RoadPace * .55f) + 55) * 60);
        for (int frame = 0; frame < limit && !finished; frame++)
        {
            TickHybridCheck();
            Require(Player.GatesCleared >= gates && Player.GatesCleared <= gates + 1,
                "hybrid gates are recorded in sequence across vehicle changes");
            gates = Player.GatesCleared;
            if (Player.EjectTime > 0 || Player.ResetHold > 0) continue;
            TrackTravelMode mode = Player.Flight is not null ? TrackTravelMode.Flight
                : Player.UsesJetSki ? TrackTravelMode.JetSki : TrackTravelMode.Ground;
            if (transitions.Count > 0 && transitions[^1] == mode) continue;
            transitions.Add(mode);
            Require(track.TravelMode(Player.Distance) == mode && Player.Rider == rider,
                $"the racer enters the {mode} section with the original rider");
            if (mode == TrackTravelMode.JetSki)
                Require(Player.Saddle.GetParent() == Player.JetSkiVisual && Player.JetSkiVisual!.Visible &&
                    !Player.Mount.Visible && Player.HasSkiWaterState, "the jet ski takes the saddle and runs the water physics");
            else if (mode == TrackTravelMode.Flight)
                Require(Player.Rider.GetParent() == Player.Flight!.Flyer.RiderAnchor &&
                    !Player.Mount.Visible && Player.JetSkiVisual is { Visible: false },
                    "the aircraft carries the same rider with both ground vehicles stowed");
            if (!snapshots.ContainsKey(mode))
                snapshots[mode] = Wire.Packet("racer", RacerState(Player, 0)).Read<OnlineRacer>()!;
        }
        Require(finished && Player.Finish > 0 && gates == track.GateCount + 1 && Player.LapCrossings.Count == 1 &&
            transitions.SequenceEqual([TrackTravelMode.Ground, TrackTravelMode.Flight, TrackTravelMode.JetSki,
                TrackTravelMode.Flight, TrackTravelMode.Ground]),
            $"a hybrid lap completes ground, flight, water, flight and ground ({string.Join(", ", transitions)}; {gates} gates; {Player.Distance:F1} m)");

        var replica = racers[1];
        foreach (TrackTravelMode mode in new[] { TrackTravelMode.Ground, TrackTravelMode.Flight, TrackTravelMode.JetSki, TrackTravelMode.Ground })
        {
            ApplyOnlineRacer(replica, snapshots[mode]);
            replica.Draw(track, 0, true);
            Require((replica.Flight is not null) == (mode == TrackTravelMode.Flight) &&
                replica.UsesJetSki == (mode == TrackTravelMode.JetSki),
                $"a transmitted snapshot restores the {mode} vehicle on a guest");
        }
        GD.Print($"HYBRID COURSE PASS | {track.Length:F0} m, {gates} gates, {Player.Finish:F1}s | ground -> flight -> jet ski -> flight -> ground | editor, save and replica verified.");
        OpenStable();
        Require(trackEditorUi.Visible && selectedTown == previousCourse && editorModes.SequenceEqual(fixture.Modes!),
            "leaving a hybrid Test Drive returns to the same draft");
        HideTrackEditor();
    }

    private void CheckSnowboardDescent(int id, string name)
    {
        PopulateCoursePicker(name);
        Require(courseList.ItemCount == 1 && courseList.GetItemMetadata(0).AsInt32() == id,
            $"{name} is available in the course picker");
        ChooseCourse(id);
        StartRace();
        Require(racing && activeTown == id && track.IsSnowboardCourse && track.IsPointToPoint &&
            track.Point(0).Y - track.Point(track.Length).Y > 175,
            $"{name} loads as a native downhill sprint ({courseProblem})");
        Require(racers.All(racer => racer.UsesSnowboard && !racer.Mount.Visible &&
            racer.Saddle.GetParent() is Node3D { Name: var boardName, Visible: true } && boardName == "Snowboard"),
            $"every racer starts {name} standing on their own snowboard");
        Require(snowTiles.Count == 0, $"{name} starts with unpacked snow");
        CheckSnowboardProfile(name);
        countdown = 0;
        best = 0;
        int gates = 0, airFrames = 0, launches = 0, brakeFrames = 0;
        var wipeouts = new List<string>();
        var recalls = new List<string>();
        Vector3? packedPoint = null;
        float previousTime = id == MountEsperDownhillId ? 50.4f : 77.6f;
        int limit = Mathf.CeilToInt((previousTime + 25) * 60);
        for (int frame = 0; frame < limit && !finished; frame++)
        {
            float before = Player.Distance;
            bool wasAirborne = Player.SnowboardAirborne, wasEjected = Player.EjectTime > 0, wasRecalled = Player.ResetHold > 0;
            // Race the real gates and terrain with opponents present, excluding weapon hits from the route measurement.
            Player.ItemGrace = Math.Max(Player.ItemGrace, .25f);
            if (TickSnowboardCheck()) brakeFrames++;
            bool airborne = Player.SnowboardAirborne && Player.EjectTime <= 0;
            if (airborne) airFrames++;
            if (airborne && !wasAirborne) launches++;
            if (!wasEjected && Player.EjectTime > 0) wipeouts.Add($"{Player.Distance:F0} m ({message})");
            if (!wasRecalled && Player.ResetHold > 0) recalls.Add($"{before:F0} m ({message})");
            Require(Player.GatesCleared >= gates && Player.GatesCleared <= gates + 1,
                $"{name} records gates in order");
            gates = Player.GatesCleared;
            Vector3 point = Player.CoursePosition(track);
            if (packedPoint is null && Player.Speed > 5 && UnpackedSnow(point) < .90f) packedPoint = point;
        }
        GD.Print($"SNOWBOARD RUN | {name} | {Player.Finish:F1}s (previously {previousTime:F1}s) | {gates}/{track.GateCount + 1} gates | " +
            $"{launches} launches, {airFrames / 60f:F1}s airborne | {brakeFrames / 60f:F1}s braking | " +
            $"{wipeouts.Count} wipeouts [{string.Join(", ", wipeouts)}], {recalls.Count} recalls [{string.Join(", ", recalls)}].");
        Require(finished && Player.Finish > 0 && gates == track.GateCount + 1 && Player.LapCrossings.Count == 1,
            $"{name} reaches the official finish ({gates} gates; {Player.Distance:F1} m)");
        Require(Player.Finish < previousTime && recalls.Count == 0,
            $"{name} is faster with active pushing and has no checkpoint recalls ({Player.Finish:F1}s, {recalls.Count} recalls)");
        Require(packedPoint is { } packed && UnpackedSnow(packed) < .90f && snowTiles.Count > 0,
            $"{name} leaves a persistent compressed track in the powder");
        GD.Print($"SNOWBOARD COURSE PASS | {name} | {track.Length:F0} m, {gates} gates, {Player.Finish:F1}s | snow packing verified.");
        if (id == MountEsperDownhillId)
        {
            CheckSnowboardFlags();
            CheckSnowboardGravity();
        }
        OpenStable();
    }

    private void CheckSnowboardProfile(string name)
    {
        float start = track.Gate(0), finish = track.Gate(track.GateCount);
        float lowest = track.Point(start).Y, worstRise = 0, steepest = 0, crossfall = 0;
        for (float at = start; at < finish; at += 4)
        {
            float height = track.Point(at).Y;
            lowest = Math.Min(lowest, height);
            worstRise = Math.Max(worstRise, height - lowest);
            float forward = Math.Min(finish, at + 8);
            steepest = Math.Max(steepest, (height - track.Point(forward).Y) / (forward - at));
            float lane = Math.Min(8, track.WidthAt(at) * .65f);
            float sideGrade = Math.Abs(track.Surface(at, lane).Y - track.Surface(at, -lane).Y) / (lane * 2);
            crossfall = Math.Max(crossfall, sideGrade);
        }

        float entryDrop = track.Point(start - 8).Y - track.Point(start + 12).Y;
        float runoutDrop = track.Point(finish - 28).Y - track.Point(finish).Y;
        float beyondFinish = track.Point(finish).Y - track.Point(track.Length).Y;
        float insideRadius = track.TightestInsideRadius();
        GD.Print($"SNOWBOARD PROFILE | {name} | {insideRadius:F1} m inside turn radius | {worstRise:F1} m longest rise | " +
            $"{steepest:P0} peak grade, {crossfall:P0} peak crossfall | {entryDrop:F1} m entry drop, {runoutDrop:F1} m finish approach, " +
            $"{beyondFinish:F1} m drop after the finish.");
        Require(insideRadius >= 33 && worstRise <= 14 && entryDrop > .25f &&
            runoutDrop is >= -4 and <= 18 && beyondFinish is >= -6 and <= 14,
            $"{name} has a downhill start, sweeping turns, minimal uphill interruptions and a gentle runout");
        for (int gate = 0; gate <= track.GateCount; gate++)
        {
            float at = track.Gate(gate);
            Vector3 approach = track.Point(at - 5), departure = track.Point(at + 5);
            Require(track.NativeDry(approach) && track.NativeDry(departure) &&
                track.TryCrossGate(gate, approach, departure, out _),
                $"{name} gate {gate + 1} has a dry, passable centerline approach");
        }
    }

    private void CheckSnowboardFlags()
    {
        const float dt = 1f / 60;
        const int flag = 2;
        float at = track.Gate(flag);
        float lane = track.WidthAt(at) + 3;
        Vector3 before = track.Surface(at - 2, lane), after = track.Surface(at + 2, lane);
        if (!track.NativeDry(before) || !track.NativeDry(after))
        {
            before = track.Surface(at - 2, -lane);
            after = track.Surface(at + 2, -lane);
        }
        Require(track.NativeDry(before) && track.NativeDry(after) &&
            !track.TryCrossGate(flag, before, after, out _), "the board passes outside an intermediate flag on dry snow");

        Player.Finish = -1;
        Player.LapCrossings.Clear();
        Player.GatesCleared = flag;
        Player.Checkpoint = track.Gate(flag - 1);
        Player.Distance = track.Project(after, at).Distance;
        RecordGate(Player, before, after, dt);
        Require(Player.GatesCleared == flag + 1 && RaceProgress(Player) >= at && Player.ResetHold == 0,
            "passing a slalom flag off to the side still advances snowboard progress");

        for (int gate = flag + 1; gate < track.GateCount; gate++)
        {
            at = track.Gate(gate);
            Player.Distance = at + 2;
            RecordGate(Player, track.Surface(at - 2, 0), track.Surface(at + 2, 0), dt);
        }

        at = track.Gate(track.GateCount);
        Player.Distance = at + 2;
        RecordGate(Player, track.Surface(at - 5, 0), track.Surface(at - 2, 0), dt);
        Require(Player.Finish < 0, "projected progress cannot finish the race before crossing the line");
        float farOutside = track.WidthAt(at) + 30;
        RecordGate(Player, track.Surface(at - 2, farOutside), track.Surface(at + 2, farOutside), dt);
        Require(Player.Finish < 0, "crossing outside the snowboard corridor does not count as a finish");
        RecordGate(Player, track.Surface(at - 2, 0), track.Surface(at + 2, 0), dt);
        Require(Player.Finish >= 0 && Player.GatesCleared == track.GateCount + 1 && Player.LapCrossings.Count == 1 &&
            Player.ResetHold == 0, "a rider can finish after missing an intermediate flag");
        GD.Print("SNOWBOARD FLAGS PASS | passed outside a flag and continued through the finish.");
    }

    private void CheckSnowboardGravity()
    {
        const float dt = 1f / 60;
        int samples = Mathf.FloorToInt((track.Gate(track.GateCount) - BogCircuit.Grid - 75) / 6);
        float start = Enumerable.Range(0, samples).Select(i => BogCircuit.Grid + 25 + i * 6f)
            .MaxBy(at => track.Point(at - 3).Y - track.Point(at + 7).Y);
        Player.Distance = start;
        Player.Lane = Player.Speed = 0;
        Player.ResetSnowboardPhysics();
        Vector3 original = track.Surface(start, 0);
        for (int frame = 0; frame < 78; frame++) Player.StepSnowboardPhysics(track, dt, 0, false, false, false, 0, 1);
        float coastSpeed = Player.Speed;
        Require(coastSpeed > 2 && Player.SnowboardPosition.Y < original.Y - .38f,
            $"gravity alone builds downhill speed ({coastSpeed:F2} m/s)");
        for (int frame = 0; frame < 48; frame++) Player.StepSnowboardPhysics(track, dt, 0, false, true, false, 0, 1);
        Require(Player.Speed < coastSpeed, "bearing onto the edge brakes the board on snow");

        float flat = Enumerable.Range(0, samples).Select(i => BogCircuit.Grid + 25 + i * 6f)
            .MinBy(at => Enumerable.Range(0, 5).Sum(i =>
                Math.Abs(track.Point(at + (i + 1) * 3).Y - track.Point(at + i * 3).Y)));
        const float rollingSpeed = 11;
        float coasting = RollOnGentleSnow(false), pushing = RollOnGentleSnow(true);
        Require(pushing > rollingSpeed && pushing > coasting + 1.3f,
            $"pushing still builds speed while already rolling on gentle snow ({pushing:F2} vs {coasting:F2} m/s)");

        Player.ResetSnowboardPhysics();
        Player.Lane = Player.Speed = 0;
        Player.Distance = track.Length - 22;
        Vector3 forward = track.Forward(Player.Distance);
        Vector3 release = track.Surface(Player.Distance, 0) + Vector3.Up * 28;
        Player.ApplyReplicaSnowboardPhysics(release, Vector3.Zero, forward, 0, 0, true);
        for (int frame = 0; frame < 30; frame++) Player.StepSnowboardPhysics(track, dt, 0, false, false, false, 0, 1);
        Require(Player.SnowboardAirborne && Math.Abs(Player.SnowboardPosition.Y - (release.Y - RacePhysics.Gravity * .125f)) < .045f &&
            Math.Abs(Player.SnowboardVelocity.Y + RacePhysics.Gravity * .5f) < .025f,
            "a detached board drops along a 9.80665 m/s² ballistic arc");
        var packet = Wire.Packet("board", RacerState(Player, 0)).Read<OnlineRacer>()!;
        var replica = racers[1];
        ApplyOnlineRacer(replica, packet);
        Require(replica.SnowboardAirborne && replica.SnowboardPosition.DistanceTo(Player.SnowboardPosition) < .01f,
            "the board's world-space flight survives an online snapshot");

        Player.Finish = -1;
        Player.GatesCleared = track.GateCount;
        Player.Checkpoint = track.Gate(Player.GatesCleared - 1);
        for (int frame = 0; frame < 160 && Player.SnowboardAirborne; frame++)
            MoveSnowboard(Player, dt, false, false, 0, false);
        float impact = Player.SnowboardLandingImpact;
        Require(impact > 11 && Player.EjectTime > 0 && Player.Rider.TopLevel && !Player.SnowboardAirborne &&
            Player.SnowboardPosition.Y >= track.FreeGround(Player.SnowboardPosition).Y,
            $"a hard landing collides without tunneling and throws the rider ({impact:F2} m/s)");
        var landing = Player.SnowboardPosition;
        float station = Player.Distance, checkpoint = Player.Checkpoint;
        int gates = Player.GatesCleared;
        Player.EjectTime = dt * .5f;
        TickRace(dt, false, false, 0, false, false);
        RequireLocalRecovery("a hard landing recovers at the board", landing, station, checkpoint, gates);

        RecallRacer(Player, "TUSKER PUS!");
        RequireLocalRecovery("an item recall recovers at the board", landing, station, checkpoint, gates);
        GD.Print($"SNOWBOARD GRAVITY PASS | {coastSpeed:F1} m/s downhill without throttle | " +
            $"{pushing:F1} vs {coasting:F1} m/s with and without pushing on gentle snow | 1g fall | " +
            $"{impact:F1} m/s impact | local recovery after landing and combat | airborne replica.");
        ResetRace();

        float RollOnGentleSnow(bool push)
        {
            Player.ResetSnowboardPhysics();
            Player.Distance = flat;
            Player.Lane = 0;
            Player.Speed = rollingSpeed;
            for (int frame = 0; frame < 56; frame++)
                Player.StepSnowboardPhysics(track, dt, 0, push, false, false, 0, 1);
            return Player.Speed;
        }

        void RequireLocalRecovery(string description, Vector3 position, float progress, float lastFlag, int flags)
        {
            Vector3 movement = Player.SnowboardPosition - position;
            Require(Player.HasSnowboardPhysics && !Player.SnowboardAirborne && !Player.Rider.TopLevel &&
                Player.EjectTime == 0 && Player.ResetHold == 0 && Player.Speed is >= 3 and <= 6 &&
                Math.Abs(Player.Distance - progress) < .15f &&
                Player.Checkpoint == lastFlag && Player.GatesCleared == flags &&
                new Vector2(movement.X, movement.Z).Length() < .15f,
                $"{description} with progress and flags intact ({movement.Length():F2} m displacement)");
        }
    }

    private void TickHybridCheck()
    {
        if (Player.UsesSnowboard)
        {
            TickSnowboardCheck();
            return;
        }
        bool sudsed = IsSudsed(Player);
        float steer = Mathf.Clamp(Player.Lane * (sudsed ? .43f : -.43f), -1, 1);
        TickRace(1f / 60, !sudsed, sudsed, steer, false, false);
    }

    private bool TickSnowboardCheck()
    {
        bool sudsed = IsSudsed(Player);
        float sideSpeed = Player.SnowboardVelocity.Dot(track.Right(Player.Distance));
        float aim = Mathf.Clamp(-Player.Lane * .38f - sideSpeed * .24f, -1, 1);
        float projectedLane = Player.Lane + sideSpeed * .6f;
        bool leaving = Math.Abs(projectedLane) > track.WidthAt(Player.Distance) * .59f && sideSpeed * projectedLane > 0;

        // Brake for bends a rider can see ahead, using more modest lateral grip than land racers.
        float cornerSpeedSquared = 32 * 32;
        float lookAhead = Mathf.Clamp(Player.Speed * 1.35f, 12, 44);
        for (float ahead = 0; ahead <= lookAhead; ahead += 4)
        {
            float radius = track.TurnRadius(Player.Distance + ahead);
            float usableRadius = Math.Max(8, radius - Math.Abs(Player.Lane));
            cornerSpeedSquared = Math.Min(cornerSpeedSquared, usableRadius * 8.5f + 2 * 4.5f * ahead);
        }
        bool brake = !Player.SnowboardAirborne && (sudsed || Player.Speed > MathF.Sqrt(cornerSpeedSquared) + 1 ||
            leaving && Player.Speed > 13);
        TickRace(1f / 60, !brake && !Player.SnowboardAirborne, brake, sudsed ? -aim : aim, false, false);
        return brake;
    }
}
