using Godot;

public partial class GrandPrix
{
    private readonly List<(MeshInstance3D Left, MeshInstance3D Right, MeshInstance3D? Trim)> checkpointBeacons = [];
    private readonly List<(Node3D Arch, float HalfWidth)> checkpointArches = [];
    private readonly StandardMaterial3D gateGold = Glow(new Color("ffd37c"));
    private readonly StandardMaterial3D gateMuted = BogCircuit.Paint("42766a");
    private readonly StandardMaterial3D voidGateHot = Glow(new Color("ffc187"));
    private readonly StandardMaterial3D voidGateIdle = Glow(new Color("7652c0"));
    private StandardMaterial3D? islandGateHot, islandGateIdle;
    private int highlightedGate = -1;
    private const float MetresPerSecondPerMph = 1609.344f / 3600f;
    private const float RoadPace = 60 * MetresPerSecondPerMph, OffRoadPace = 48 * MetresPerSecondPerMph,
        BoostSpeedCap = 75 * MetresPerSecondPerMph, DriftSpeedCap = 90 * MetresPerSecondPerMph;
    private const float JetSkiPace = 100 * MetresPerSecondPerMph, JetSkiBoostCap = 125 * MetresPerSecondPerMph,
        JetSkiDriftCap = 145 * MetresPerSecondPerMph;
    private const float BoardPace = 48 * MetresPerSecondPerMph, BoardBoostCap = 66 * MetresPerSecondPerMph,
        BoardDriftCap = 79 * MetresPerSecondPerMph;
    private const float GroundAcceleration = 15, BoostAcceleration = 27, GroundBraking = 32,
        TurnBraking = 26, CoastDeceleration = 8;

    private void BuildCheckpointBeacons()
    {
        checkpointBeacons.Clear();
        checkpointArches.Clear();
        Texture2D? plate = track.IsWaterCourse ? RegattaPlateTexture() : null;
        islandGateHot = track.IsWaterCourse ? RegattaFinish(plate, "ffd37c", .38f, .8f) : null;
        islandGateIdle = track.IsWaterCourse ? RegattaFinish(plate, "528e86", .42f, .3f) : null;
        var waterRail = track.IsWaterCourse ? RegattaFinish(plate, "717f7c", .46f) : null;
        var waterCloth = track.IsWaterCourse ? RegattaFinish(plate, "3b5d52", .56f) : null;
        var waterPort = track.IsWaterCourse ? RegattaFinish(plate, "eb4f4f", .4f) : null;
        var waterStarboard = track.IsWaterCourse ? RegattaFinish(plate, "46cfa6", .4f) : null;
        for (int gate = 0; gate < track.GateCount + (track.IsPointToPoint ? 1 : 0); gate++)
        {
            float at = track.Gate(gate);
            float halfWidth = track.WidthAt(at) + .60f;
            Vector3 leftPoint = track.Surface(at, -halfWidth);
            Vector3 rightPoint = track.Surface(at, halfWidth);
            Vector3 center = track.Point(at);
            float top = Math.Max(center.Y, Math.Max(leftPoint.Y, rightPoint.Y)) +
                (gate == 0 ? 5.2f : track.IsWaterCourse ? 2.25f : track.IsSpan(at) ? 1.2f : 4.45f);
            var wood = waterRail ?? BogCircuit.Paint(track.IsVoid ? "211331" : "49362b");
            var cloth = waterCloth ?? BogCircuit.Paint(track.IsVoid ? "220d2d" : "294839");
            var diamond = new BoxMesh { Size = new Vector3(.69f, .69f, .69f) };
            MeshInstance3D AddBeacon(Vector3 point)
            {
                float height = track.IsSnowboardCourse ? 2.42f : top - point.Y;
                if (track.IsWaterCourse)
                {
                    bool port = point == leftPoint;
                    var floater = new CylinderMesh { TopRadius = .68f, BottomRadius = 1.16f, Height = 1.28f, RadialSegments = 12 };
                    var buoy = port ? waterPort! : waterStarboard!;
                    BogCircuit.MeshAt(courseItems, floater, point + Vector3.Up * .50f, Vector3.One, buoy);
                    BogCircuit.Box(courseItems, point + Vector3.Up * (height * .5f), new(.18f, height, .18f), wood);
                    var light = BogCircuit.MeshAt(courseItems, diamond, point + Vector3.Up * height, Vector3.One, islandGateIdle!);
                    light.RotationDegrees = new(12, 22, 45);
                    light.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                    return light;
                }
                BogCircuit.Box(courseItems, point + Vector3.Up * (height * .5f), new(.18f, height, .18f), wood);
                var beacon = BogCircuit.MeshAt(courseItems, diamond, point + Vector3.Up * (height + .10f), Vector3.One, gateMuted);
                if (track.IsSnowboardCourse && (gate == 0 || gate == track.GateCount))
                    courseItems.AddChild(new Label3D
                    {
                        Text = gate == 0 ? "START" : "FINISH", FontSize = 70, PixelSize = .011f, OutlineSize = 7,
                        Position = point + Vector3.Up * (height + .76f),
                        Rotation = new Vector3(0, BogCircuit.Yaw(track.Forward(at)), 0)
                    });
                beacon.RotationDegrees = new(15, 23, 45);
                beacon.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                return beacon;
            }

            MeshInstance3D? rail = null;
            if (!track.IsSnowboardCourse && !track.IsSpan(at) &&
                (track.IsWaterCourse ? gate == 0 : gate != 0 || track.NativeBlockCount > 0 || track.IsVoid))
            {
                var arch = new Node3D
                {
                    Position = new Vector3(center.X, top, center.Z),
                    Rotation = new Vector3(0, BogCircuit.Yaw(track.Forward(at)), 0)
                };
                courseItems.AddChild(arch);
                checkpointArches.Add((arch, halfWidth));
                float span = halfWidth * 2;
                BogCircuit.Box(arch, Vector3.Zero, new(span + .6f, .20f, .30f), wood);
                rail = BogCircuit.Box(arch, new(0, -.06f, .17f), new(span, .055f, .04f), gateMuted);
                if (gate == 0 || track.IsPointToPoint && gate == track.GateCount)
                {
                    float signWidth = Math.Min(12, span * .72f);
                    BogCircuit.Box(arch, new(0, .39f, 0), new(signWidth, .85f, .18f), cloth);
                    arch.AddChild(new Label3D
                    {
                        Text = track.IsSprint ? (gate == 0 ? "HOLTBURG  •  START" : "CRAGSTONE  •  FINISH")
                            : gate == 0 ? track.Name.ToUpperInvariant() : track.IsSnowboardCourse ? "FINISH" : "NEYDISA  •  FINISH",
                        FontSize = 76, PixelSize = .0105f,
                        OutlineSize = 5, Modulate = new Color("f7d899"), Position = new(0, .39f, .105f),
                        DoubleSided = false
                    });
                }
            }
            checkpointBeacons.Add((AddBeacon(leftPoint), AddBeacon(rightPoint), rail));
        }
        highlightedGate = -1;
    }

    private void HighlightCheckpoint()
    {
        int target = track.IsPointToPoint ? Math.Min(track.GateCount, Player.GatesCleared) : Player.GatesCleared % track.GateCount;
        if (target == highlightedGate) return;
        for (int i = 0; i < checkpointBeacons.Count; i++)
        {
            var pair = checkpointBeacons[i];
            var material = track.IsVoid ? (i == target ? voidGateHot : voidGateIdle)
                : track.IsWaterCourse ? (i == target ? islandGateHot : islandGateIdle) : (i == target ? gateGold : gateMuted);
            pair.Left.MaterialOverride = pair.Right.MaterialOverride = material;
            if (pair.Trim is not null) pair.Trim.MaterialOverride = material;
        }
        highlightedGate = target;
    }

    private void MovePlayer(float dt, bool gas, bool brake, float steer, bool sliding)
        => MoveHuman(Player, ref lateral, dt, gas, brake, steer, sliding);

    private void MoveHuman(MountedRacer rider, ref float laneVelocity, float dt, bool gas, bool brake, float steer, bool sliding)
    {
        if (rider.UsesSnowboard)
        {
            MoveSnowboard(rider, dt, gas, brake, steer, sliding);
            return;
        }
        float target = brake || !gas ? 0 : SpeedLimit(rider, GroundPace(rider));
        if (!rider.FreeDriving) target = TurnPace(rider, target, sliding);
        float response = brake ? GroundBraking : !gas ? CoastDeceleration : PaceAcceleration(rider, target);
        MoveGroundRacer(rider, ref laneVelocity, dt, target, response, steer, sliding, allowOffRoad: true);
    }

    private static float GroundPace(MountedRacer racer) =>
        (racer.FreeDriving && !racer.UsesJetSki ? OffRoadPace : CruisePace(racer)) * (racer.MountUnavailable ? .75f : 1);

    private static float CruisePace(MountedRacer racer) => racer.UsesJetSki ? JetSkiPace : racer.UsesSnowboard ? BoardPace : RoadPace;

    private static float BoostCap(MountedRacer racer, bool super) => racer.UsesJetSki
        ? super ? JetSkiDriftCap : JetSkiBoostCap
        : racer.UsesSnowboard ? super ? BoardDriftCap : BoardBoostCap
        : super ? DriftSpeedCap : BoostSpeedCap;

    private static float PaceAcceleration(MountedRacer racer, float target) =>
        target < racer.Speed ? TurnBraking
        : racer.Boost > 0 ? racer.UsesJetSki ? 39 : BoostAcceleration
        : racer.UsesJetSki ? 23 : GroundAcceleration;

    private static float SteeringPace(MountedRacer racer) =>
        1 + .35f * Mathf.SmoothStep(CruisePace(racer), BoostCap(racer, super: true), racer.Speed);

    private static float LaneSteeringRate(MountedRacer racer, bool sliding) =>
        (racer.UsesJetSki ? sliding ? 12.8f : 9.4f : racer.UsesSnowboard ? sliding ? 11.4f : 8.2f : sliding ? 8.6f : 6.6f) * SteeringPace(racer);

    private void MoveGroundRacer(MountedRacer racer, ref float laneVelocity, float dt, float target,
        float acceleration, float steer, bool sliding, bool allowOffRoad)
    {
        if (racer.UsesSnowboard)
        {
            MoveSnowboard(racer, dt, target > 0, target < Math.Max(.2f, racer.Speed - 2.5f), steer, sliding);
            return;
        }
        float waterGrip = 1;
        if (racer.UsesJetSki && !racer.MountUnavailable)
        {
            float contact = racer.SkiAirborne || racer.LeapHeight > .1f ? 0 : racer.SkiWaterContact;
            waterGrip = Mathf.Lerp(.44f, 1, contact);
            acceleration *= Mathf.Lerp(.24f, 1, contact);
        }
        else if (target > 0 && track.NativeBlockCount > 0)
        {
            Vector3 point = racer.CoursePosition(track);
            if (track.NativeSnow(point)) target *= 1 - .065f * UnpackedSnow(point);
        }
        racer.Speed = Math.Min(BoostCap(racer, super: true), Mathf.MoveToward(racer.Speed, target, dt * acceleration));
        racer.Lean = Mathf.Lerp(racer.Lean, steer * (sliding ? 1 : .62f), 1 - Mathf.Exp(-9 * dt));
        if (racer.FreeDriving)
        {
            DriveOffTrack(racer, ref laneVelocity, dt, steer * waterGrip, sliding);
            return;
        }

        float curve = track.Curvature(racer.Distance + 3);
        float pace = SteeringPace(racer);
        float desired = (steer * LaneSteeringRate(racer, sliding) * waterGrip - curve * (sliding ? 5 : 14)) *
            Mathf.Clamp(racer.Speed / 10, 0, 1);
        laneVelocity = Mathf.MoveToward(laneVelocity, desired, dt * (sliding ? 11 : 25) * pace * waterGrip);
        float nextLane = racer.Lane + laneVelocity * dt;
        AdvanceAlongLane(racer, dt, Math.Abs(racer.Lean) * (sliding ? .35f : 1.1f));
        float edge = track.WidthAt(racer.Distance);
        if (track.IsCustom)
        {
            edge -= RidgeRoad.CustomShoulder;
            racer.Lane = Mathf.Clamp(nextLane, -edge, edge);
            if (Math.Abs(nextLane) > edge && laneVelocity * racer.Lane > 0)
                laneVelocity = 0;
            return;
        }
        if (!track.IsSpan(racer.Distance)) edge += .25f;
        racer.Lane = Mathf.Clamp(nextLane, -edge, edge);

        if (!allowOffRoad || racer.Speed < 2 || Math.Abs(nextLane) <= edge || nextLane * steer < .38f) return;
        Vector3 edgePoint = track.Surface(racer.Distance, racer.Lane);
        Vector3 heading = track.Forward(racer.Distance).Rotated(Vector3.Up, -steer * .56f);
        Vector3 outside = edgePoint + track.Right(racer.Distance) * (Math.Sign(nextLane) * .86f);
        if (track.IsRoadDrop(edgePoint, outside))
        {
            racer.FreeDriving = true;
            racer.FreePosition = edgePoint + Vector3.Up * racer.LeapHeight;
            racer.FreeYaw = BogCircuit.Yaw(heading);
            racer.FallTime = float.Epsilon;
            racer.Leap = racer.SwingTime = 0;
            laneVelocity = 0;
            Array.Clear(racer.DustContact);
            if (racer == Player)
            {
                charge = 0;
                drifting = false;
                Callout("OFF THE EDGE!", 1.05f);
            }
            return;
        }
        Vector3 edgeGround = racer.UsesJetSki ? edgePoint : track.FreeGround(edgePoint);
        if (!track.TryFreeMove(edgeGround, edgePoint + heading * racer.Speed * dt, out var grounded, jetSki: racer.UsesJetSki)) return;
        racer.FreeDriving = true;
        racer.FreePosition = grounded;
        racer.FreeYaw = BogCircuit.Yaw(heading);
        UpdateRoadProjection(racer);
    }

    private void TickTrackFall(MountedRacer racer, float dt)
    {
        Vector3 start = racer.FreePosition;
        Vector3 velocity = racer.Heading(track) * racer.Speed + RacePhysics.Acceleration * racer.FallTime;
        Vector3 next = RacePhysics.BallisticPosition(start, velocity, dt);
        racer.FallTime += dt;
        // Sample the terrain below the departed deck. The rescue limit still
        // returns racers from bottomless routes even without a terrain contact.
        if (racer.FallTime >= RacePhysics.TrackFallRescueTime ||
            (racer.FallTime >= .26f && RacePhysics.TryGroundContact(track, start, next, 0,
                includeRoad: false, out _, out _)))
        {
            RecallRacer(racer, "BACK AT THE CHECKPOINT");
            return;
        }
        racer.FreePosition = next;
        racer.Draw(track, dt, false);
    }

    private float TurnPace(MountedRacer racer, float target, bool sliding, float grip = 1)
    {
        if (target <= 0) return 0;
        float limitSquared = target * target;
        float cornerGrip = (sliding ? 75 : 55) * grip;
        float spacing = Mathf.Clamp(racer.Speed * .85f, 12, 40) / 8;
        for (int i = 0; i <= 8; i++)
        {
            float ahead = i * spacing;
            float curve = track.Curvature(racer.Distance + ahead);
            if (Math.Abs(curve) < .0001f) continue;
            float laneRadius = Math.Max(8, 1 / Math.Abs(curve) - racer.Lane * Math.Sign(curve));
            float entryLimit = laneRadius * cornerGrip + 2 * TurnBraking * ahead;
            limitSquared = Math.Min(limitSquared, entryLimit);
        }
        return MathF.Sqrt(limitSquared);
    }

    private void AdvanceAlongLane(MountedRacer racer, float dt, float drag)
    {
        float laneScale = Mathf.Clamp(1 - racer.Lane * track.Curvature(racer.Distance), .57f, 1.7f);
        racer.Distance += Math.Max(0, racer.Speed - drag) * dt / laneScale;
    }
    private void DriveOffTrack(MountedRacer racer, ref float laneVelocity, float dt, float steer, bool sliding)
    {
        float turnRate = (sliding ? 2.75f : 2.1f) * (1 - .20f *
            Mathf.SmoothStep(racer.UsesJetSki ? JetSkiPace : OffRoadPace, BoostCap(racer, super: true), racer.Speed));
        racer.FreeYaw = Mathf.Wrap(racer.FreeYaw - steer * turnRate * dt, -Mathf.Pi, Mathf.Pi);
        Vector3 heading = racer.Heading(track);
        Vector3 destination = racer.FreePosition + heading * (racer.Speed * dt);
        if (track.TryFreeMove(racer.FreePosition, destination, out var grounded, jetSki: racer.UsesJetSki))
            racer.FreePosition = grounded;
        else if (racer.UsesJetSki)
        {
            if (!racer.MountUnavailable && racer.Speed >= 10)
            {
                ImpactRegattaWater(racer.FreePosition, 2.35f, Mathf.Lerp(.10f, .29f, Mathf.SmoothStep(10, 55, racer.Speed)));
                SplashIslandShore(racer, racer.FreePosition, racer.Speed);
            }
            racer.Speed = Math.Min(racer.Speed, 2.3f);
        }
        else
            racer.Speed = Mathf.MoveToward(racer.Speed, 0, GroundBraking * dt);

        UpdateRoadProjection(racer);
        if (Math.Abs(racer.Lane) >= track.WidthAt(racer.Distance) - .14f || heading.Dot(track.Forward(racer.Distance)) < .80f)
            return;
        racer.FreeDriving = false;
        float rejoinPace = racer.UsesJetSki ? LaneSteeringRate(racer, false) : 6 * SteeringPace(racer);
        laneVelocity = Mathf.Clamp(heading.Dot(track.Right(racer.Distance)) * racer.Speed, -rejoinPace, rejoinPace);
    }

    private void UpdateRoadProjection(MountedRacer racer)
    {
        (racer.Distance, racer.Lane) = track.Project(racer.FreePosition, racer.Distance);
    }

    private void RecordGate(MountedRacer racer, Vector3 before, Vector3 after, float dt)
    {
        float fraction;
        if (racer.UsesSnowboard)
        {
            int previousFlags = racer.GatesCleared;
            while (racer.GatesCleared < track.GateCount && racer.Distance >= track.Gate(racer.GatesCleared))
            {
                racer.Checkpoint = track.Gate(racer.GatesCleared);
                racer.GatesCleared++;
            }
            if (racer == Player && racer.GatesCleared != previousFlags) HighlightCheckpoint();
            if (racer.GatesCleared != track.GateCount || !CrossedSnowboardFinish(racer, before, after, out fraction))
                return;
        }
        else if (!track.TryCrossGate(racer.GatesCleared, before, after, out fraction)) return;
        racer.Checkpoint = track.Gate(racer.GatesCleared);
        racer.GatesCleared++;
        int completed = racer.GatesCleared - 1;
        if (completed > 0 && completed % track.GateCount == 0)
        {
            float crossing = elapsed - dt + fraction * dt;
            racer.LapCrossings.Add(crossing);
            if (completed == track.Laps * track.GateCount)
            {
                racer.Finish = crossing;
                racer.Speed = 0;
            }
        }
        if (racer == Player && racer.Finish < 0)
            HighlightCheckpoint();
    }

    private bool CrossedSnowboardFinish(MountedRacer racer, Vector3 before, Vector3 after, out float fraction)
    {
        float finish = track.Gate(track.GateCount);
        Vector3 center = track.Point(finish), forward = track.Forward(finish);
        float entry = (before - center).Dot(forward), exit = (after - center).Dot(forward);
        fraction = 0;
        if (racer.Distance < finish - .2f || exit < 0 || exit <= entry) return false;

        // Allow riders who missed the end of the flag line to ride back into the finish area.
        fraction = entry < 0 ? -entry / (exit - entry) : 1;
        Vector3 crossing = before.Lerp(after, fraction);
        float lane = (crossing - center).Dot(track.Right(finish));
        float height = crossing.Y - track.FreeGround(crossing).Y;
        return exit < track.Length - finish && Math.Abs(lane) <= track.WidthAt(finish) + MountedRacer.SnowboardShoulder &&
            DrySnowboardContact(crossing, 1) && height is > -.9f and < 24;
    }

    private float RaceProgress(MountedRacer racer)
    {
        if (track.IsPointToPoint && racer.Finish >= 0) return track.Gate(track.GateCount);
        float next = track.Gate(racer.GatesCleared);
        float last = racer.Checkpoint;
        if (track.IsPointToPoint) return Mathf.Clamp(racer.Distance, last, next - .01f);
        float distance = racer.Distance + Mathf.Round(((last + next) * .5f - racer.Distance) / track.Length) * track.Length;
        return Mathf.Clamp(distance, last, next - .01f);
    }
}




