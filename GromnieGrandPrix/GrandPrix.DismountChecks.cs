using Godot;
using GromnieOnline;

public partial class GrandPrix
{
    private const float DismountCheckStep = 1f / 60;

    private void StartDismountSprintCheck()
    {
        OpenStable();
        PopulateCoursePicker(DismountCourseName);
        Require(courseList.ItemCount == 1 && courseList.GetItemMetadata(0).AsInt32() == DismountCourseId,
            "Last Legs Sprint can be found in the course picker");
        ChooseCourse(DismountCourseId);
        StartRace();
        Require(racing && activeTown == DismountCourseId && track.IsDismountSprint && track.IsPointToPoint,
            "Last Legs Sprint starts from the stable: " + courseProblem);
        Require(track.IsMedium && track.NativeBlockCount == 49 && track.Laps == 1 && track.Length is > 4750 and < 5350 &&
                Math.Abs(track.DismountLine / track.Length - .5f) < .015f,
            "the open medium course uses native terrain and ends its road halfway through");
        Require(racers.All(racer => !racer.MountUnavailable && !racer.OnFootToFinish),
            "every racer starts mounted");
        countdown = 0;
    }

    private void StageDismountApproach(float metresBeforeEnd)
    {
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            racer.Distance = track.DismountLine - metresBeforeEnd - i * .7f;
            racer.Lane = i == 0 ? 0 : (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 4f;
            racer.Speed = RoadPace * .92f;
            racer.GatesCleared = Enumerable.Range(0, track.GateCount + 1).Count(index => track.Gate(index) <= racer.Distance);
            racer.Checkpoint = track.Gate(racer.GatesCleared - 1);
            racer.Draw(track, 0, true);
        }
        HighlightCheckpoint();
    }

    private void TickDismountCheck()
    {
        bool sudsed = IsSudsed(Player);
        float steer = Mathf.Clamp(-Player.Lane * (sudsed ? -.45f : .45f), -1, 1);
        TickRace(DismountCheckStep, !sudsed, sudsed, steer, false, false);
    }

    private void CheckDismountSprint()
    {
        StartDismountSprintCheck();
        float line = track.DismountLine;
        Vector3 lastRoad = track.Point(line - 4), firstTrail = track.Point(line + 16);
        Require(float.IsFinite(track.RoadHeight(lastRoad)) && float.IsNegativeInfinity(track.RoadHeight(firstTrail)) &&
                Math.Abs(firstTrail.Y - track.FreeGround(firstTrail).Y) < .03f,
            "the packed road surface ends and the foot trail uses the original terrain height");
        Require(checkpointBeacons.Count == track.GateCount + 1 && track.Gate(track.GateCount) < track.Length,
            "the sprint has checkpoints and a distinct finish at the far end");

        StageDismountApproach(3.2f);
        HoldItem(Player, RaceItem.Pepper);
        var mounted = Wire.Packet("racer", RacerState(Player, 0));
        var mountedRacer = mounted.Read<OnlineRacer>();
        Require(mounted.Data is { } data && !data.TryGetProperty(nameof(OnlineRacer.OnFootToFinish), out _) &&
                mountedRacer is { OnFootToFinish: false },
            "snapshots without the optional on-foot field still deserialize as mounted");
        var originalGates = racers.Select(racer => racer.GatesCleared).ToArray();
        var launchPoints = new Vector3?[racers.Count];
        var travel = new Vector2[racers.Count];
        OnlineRacer? launchedPlayer = null;
        Vector3 forward = track.Forward(line);
        for (int frame = 0; frame < 120; frame++)
        {
            TickDismountCheck();
            for (int i = 0; i < racers.Count; i++)
            {
                var racer = racers[i];
                if (!racer.OnFootToFinish || racer.EjectTime <= 0 || !racer.Rider.TopLevel) continue;
                Vector3 position = racer.Rider.GlobalPosition;
                launchPoints[i] ??= position;
                Vector3 delta = position - launchPoints[i]!.Value;
                travel[i] = new(Math.Max(travel[i].X, delta.Dot(forward)), Math.Max(travel[i].Y, delta.Y));
                if (racer == Player) launchedPlayer ??= Wire.Packet("racer", RacerState(racer, 0)).Read<OnlineRacer>();
            }
        }

        Require(travel.All(delta => delta.X > 1f && delta.Y > .15f),
            "each rider is thrown visibly upward and forward at the road end");
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            int expected = Enumerable.Range(0, track.GateCount + 1).Count(index => track.Gate(index) <= racer.Distance);
            Require(racer.OnFootToFinish && racer.MountUnavailable && racer.EjectTime <= 0 && !racer.Rider.TopLevel &&
                    racer.Rider.GetParent() == racer.Root && racer.Distance > line && racer.GatesCleared == expected &&
                    racer.GatesCleared >= originalGates[i],
                $"{racer.Name} lands running beyond the road end and keeps the checkpoints in order");
        }

        var replica = racers[1];
        var running = Wire.Packet("racer", RacerState(Player, 0)).Read<OnlineRacer>()!;
        Require(launchedPlayer is { OnFootToFinish: true, Ejected: not null } && running.OnFootToFinish && running.Ejected is null,
            "the host sends the throw pose and retains the on-foot flag after landing");
        ApplyOnlineRacer(replica, mountedRacer!);
        replica.Draw(track, 0, true);
        Require(!replica.OnFootToFinish && replica.Mount.Visible, "a guest replica initially sees the rider mounted");
        ApplyOnlineRacer(replica, launchedPlayer!);
        Require(replica.OnFootToFinish && replica.Rider.TopLevel && replica.Rider.GetParent() == replica.Root,
            "a guest replica leaves the mount and renders the airborne rider with the native falling pose");
        ApplyOnlineRacer(replica, running);
        replica.Draw(track, 0, true);
        Require(replica.OnFootToFinish && replica.Rider.GetParent() == replica.Root && !replica.Rider.TopLevel,
            "a guest replica switches from the throw pose to running");

        GrantBoost(Player, 2.25f);
        ReleaseDriftBoost(Player, 1.30f);
        Require(Player.Boost == 0 && Player.SuperDriftUntil == 0 && !WantsGroundDrift(Player, true, 1, false) && !LaunchFlight(Player),
            "boost, drift and direct flight cannot bring a mount back during the foot leg");
        Require(Player.Item == RaceItem.PortalWard, "the Pepper carried across the road end turns into a Portal Ward");
        UseItem(Player);
        Require(Player.Item == RaceItem.None && Player.Ward > 0, "the replacement Ward works while running");
        Require(footPickupItems.Length > 0 && footPickupItems.All(item => !IsMountedPickup(item)) &&
                pickups.Where(pickup => pickup.Distance > line).All(pickup => !IsMountedPickup(pickup.Displayed)),
            "the running leg only offers pickups that can be used on foot");
        foreach (var item in new[] { RaceItem.Pepper, RaceItem.Gromnatross, RaceItem.OlthoiLift, RaceItem.TuskerMight })
        {
            HoldItem(Player, item);
            UseItem(Player);
            Require(Player.Item == item && Player.Boost == 0 && Player.Flight is null && Player.Might == 0,
                $"{item} cannot activate on the foot trail");
        }
        HoldItem(Player, RaceItem.None);

        int footGate = Player.GatesCleared;
        RecallRacer(Player, "BACK AT THE CHECKPOINT");
        Require(Player.OnFootToFinish && Player.MountUnavailable && Player.Distance >= line && Player.GatesCleared == footGate,
            "even a recall before the next checkpoint keeps the player on the foot trail");

        // The staged lead-in is a test fixture and should never be recorded as a best time.
        best = 0;
        int frames = Mathf.CeilToInt(((track.Length - line) / (RoadPace * .55f) + 45) * 60);
        for (int frame = 0; frame < frames && !finished; frame++)
        {
            TickDismountCheck();
            Require(Player.OnFootToFinish && Player.MountUnavailable && Player.MountRecovery == 0 && Player.RemountTime == 0 &&
                    Player.GatesCleared >= footGate && Player.GatesCleared <= footGate + 1,
                "the entire foot leg advances checkpoint by checkpoint without a recovery or a remount");
            footGate = Player.GatesCleared;
        }
        Require(finished && Player.Finish > MountRecoveryDuration && Player.GatesCleared == track.GateCount + 1 &&
                Player.LapCrossings.Count == 1 && lap == 1,
            $"running reaches the official result after every checkpoint ({Player.Distance:F0} m, gate {Player.GatesCleared}, {elapsed:F1}s)");
        GD.Print($"DISMOUNT SPRINT PASS | {track.Length:F0} m, road ends at {line:F0} m, every rider thrown, on-foot finish {Player.Finish:F1}s.");

        ResetRace();
        Require(racers.All(racer => !racer.OnFootToFinish && !racer.MountUnavailable && racer.Mount.Visible),
            "restarting resets every mount on the grid");
        OpenStable();
        StartRace();
        Require(racing && activeTown == DismountCourseId && racers.All(racer => !racer.OnFootToFinish && !racer.MountUnavailable),
            "starting again from the stable uses mounted racers");
        OpenStable();
    }

    private async Task CaptureDismountSprint(string folder)
    {
        StartDismountSprintCheck();
        SetPhysicsProcess(false);
        StageDismountApproach(9);
        ClearSpeedBlur();
        float line = track.DismountLine;
        camera.Fov = 63;
        camera.Position = track.Surface(line - 19, 12) + Vector3.Up * 6.2f;
        camera.LookAt(track.Point(line + 1) + Vector3.Up * 1.1f);
        raceUi.Hide();
        await SaveFrame(Path.Combine(folder, "Last-Legs-Road-End.png"));

        for (int frame = 0; frame < 160; frame++)
        {
            TickDismountCheck();
            if (Player.Distance >= line + 22 && racers.All(racer => racer.OnFootToFinish && !racer.Rider.TopLevel)) break;
        }
        Require(racers.All(racer => racer.OnFootToFinish && !racer.Rider.TopLevel),
            "the captured racers are running along the native terrain beyond the road end");
        camera.Fov = 56;
        camera.Position = track.Surface(line + 33, -10) + Vector3.Up * 4.2f;
        camera.LookAt(track.Point(line + 17) + Vector3.Up * .8f);
        await SaveFrame(Path.Combine(folder, "Last-Legs-On-Foot.png"));
        GD.Print("DISMOUNT SPRINT CAPTURE PASS");
        OpenStable();
    }
}
