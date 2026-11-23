using Godot;

public partial class GrandPrix
{
    private readonly List<(MeshInstance3D Left, MeshInstance3D Right, MeshInstance3D? Trim)> checkpointBeacons = [];
    private readonly List<(Node3D Arch, float HalfWidth)> checkpointArches = [];
    private readonly StandardMaterial3D gateGold = Glow(new Color("ffd37c"));
    private readonly StandardMaterial3D gateMuted = BogCircuit.Paint("42766a");
    private readonly StandardMaterial3D voidGateHot = Glow(new Color("ffc187"));
    private readonly StandardMaterial3D voidGateIdle = Glow(new Color("7652c0"));
    private int highlightedGate = -1;
    private const float MetresPerSecondPerMph = 1609.344f / 3600f;
    private const float RoadPace = 60 * MetresPerSecondPerMph, OffRoadPace = 48 * MetresPerSecondPerMph,
        BoostSpeedCap = 75 * MetresPerSecondPerMph, DriftSpeedCap = 90 * MetresPerSecondPerMph;
    private const float GroundAcceleration = 15, BoostAcceleration = 27, GroundBraking = 32,
        TurnBraking = 26, CoastDeceleration = 8;

    private void BuildCheckpointBeacons()
    {
        checkpointBeacons.Clear();
        checkpointArches.Clear();
        for (int gate = 0; gate < track.GateCount + (track.IsSprint ? 1 : 0); gate++)
        {
            float at = track.Gate(gate);
            float halfWidth = track.WidthAt(at) + .60f;
            Vector3 leftPoint = track.Surface(at, -halfWidth);
            Vector3 rightPoint = track.Surface(at, halfWidth);
            Vector3 center = track.Point(at);
            float top = Math.Max(center.Y, Math.Max(leftPoint.Y, rightPoint.Y)) + (track.IsSpan(at) ? 1.2f : gate == 0 ? 5.2f : 4.45f);
            var wood = BogCircuit.Paint(track.IsVoid ? "211331" : "49362b");
            var cloth = BogCircuit.Paint(track.IsVoid ? "220d2d" : "294839");
            var diamond = new BoxMesh { Size = new Vector3(.69f, .69f, .69f) };
            MeshInstance3D AddBeacon(Vector3 point)
            {
                float height = top - point.Y;
                BogCircuit.Box(courseItems, point + Vector3.Up * (height * .5f), new(.18f, height, .18f), wood);
                var beacon = BogCircuit.MeshAt(courseItems, diamond, new(point.X, top + .10f, point.Z), Vector3.One, gateMuted);
                beacon.RotationDegrees = new(15, 23, 45);
                beacon.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                return beacon;
            }

            MeshInstance3D? rail = null;
            if (!track.IsSpan(at) && (gate != 0 || track.NativeBlockCount > 0 || track.IsVoid))
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
                if (gate == 0 || track.IsSprint && gate == track.GateCount)
                {
                    float signWidth = Math.Min(12, span * .72f);
                    BogCircuit.Box(arch, new(0, .39f, 0), new(signWidth, .85f, .18f), cloth);
                    arch.AddChild(new Label3D
                    {
                        Text = track.IsSprint ? (gate == 0 ? "HOLTBURG  •  START" : "CRAGSTONE  •  FINISH") : track.Name.ToUpperInvariant(), FontSize = 76, PixelSize = .0105f,
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
        int target = track.IsSprint ? Math.Min(track.GateCount, Player.GatesCleared) : Player.GatesCleared % track.GateCount;
        if (target == highlightedGate) return;
        for (int i = 0; i < checkpointBeacons.Count; i++)
        {
            var pair = checkpointBeacons[i];
            var material = track.IsVoid ? (i == target ? voidGateHot : voidGateIdle) : (i == target ? gateGold : gateMuted);
            pair.Left.MaterialOverride = pair.Right.MaterialOverride = material;
            if (pair.Trim is not null) pair.Trim.MaterialOverride = material;
        }
        highlightedGate = target;
    }

    private void MovePlayer(float dt, bool gas, bool brake, float steer, bool sliding)
        => MoveHuman(Player, ref lateral, dt, gas, brake, steer, sliding);

    private void MoveHuman(MountedRacer rider, ref float laneVelocity, float dt, bool gas, bool brake, float steer, bool sliding)
    {
        float target = brake || !gas ? 0 : SpeedLimit(rider, GroundPace(rider));
        if (!rider.FreeDriving) target = TurnPace(rider, target, sliding);
        float response = brake ? GroundBraking : !gas ? CoastDeceleration : PaceAcceleration(rider, target);
        MoveGroundRacer(rider, ref laneVelocity, dt, target, response, steer, sliding, allowOffRoad: true);
    }

    private static float GroundPace(MountedRacer racer) =>
        (racer.FreeDriving ? OffRoadPace : RoadPace) * (racer.MountUnavailable ? .75f : 1);

    private static float PaceAcceleration(MountedRacer racer, float target) =>
        target < racer.Speed ? TurnBraking : racer.Boost > 0 ? BoostAcceleration : GroundAcceleration;

    private static float SteeringPace(float speed) => 1 + .35f * Mathf.SmoothStep(RoadPace, DriftSpeedCap, speed);

    private void MoveGroundRacer(MountedRacer racer, ref float laneVelocity, float dt, float target,
        float acceleration, float steer, bool sliding, bool allowOffRoad)
    {
        racer.Speed = Math.Min(DriftSpeedCap, Mathf.MoveToward(racer.Speed, target, dt * acceleration));
        racer.Lean = Mathf.Lerp(racer.Lean, steer * (sliding ? 1 : .62f), 1 - Mathf.Exp(-9 * dt));
        if (racer.FreeDriving)
        {
            DriveOffTrack(racer, ref laneVelocity, dt, steer, sliding);
            return;
        }

        float curve = track.Curvature(racer.Distance + 3);
        float pace = SteeringPace(racer.Speed);
        float desired = (steer * (sliding ? 8.6f : 6.6f) * pace - curve * (sliding ? 5 : 14)) *
            Mathf.Clamp(racer.Speed / 10, 0, 1);
        laneVelocity = Mathf.MoveToward(laneVelocity, desired, dt * (sliding ? 11 : 25) * pace);
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
        if (!track.TryFreeMove(track.FreeGround(edgePoint), edgePoint + heading * racer.Speed * dt, out var grounded)) return;
        racer.FreeDriving = true;
        racer.FreePosition = grounded;
        racer.FreeYaw = BogCircuit.Yaw(heading);
        UpdateRoadProjection(racer);
    }

    private void TickTrackFall(MountedRacer racer, float dt)
    {
        const float gravity = 25;
        float fall = racer.FallTime;
        racer.FallTime += dt;
        racer.FreePosition += racer.Heading(track) * (racer.Speed * dt) - Vector3.Up * (gravity * (fall + dt * .5f) * dt);
        if (racer.FallTime >= 1.10f || (racer.FallTime >= .34f && racer.FreePosition.Y <= track.FreeGround(racer.FreePosition).Y))
        {
            RecallRacer(racer, "BACK AT THE CHECKPOINT");
            return;
        }
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
        float turnRate = (sliding ? 2.75f : 2.1f) * (1 - .20f * Mathf.SmoothStep(OffRoadPace, DriftSpeedCap, racer.Speed));
        racer.FreeYaw = Mathf.Wrap(racer.FreeYaw - steer * turnRate * dt, -Mathf.Pi, Mathf.Pi);
        Vector3 heading = racer.Heading(track);
        Vector3 destination = racer.FreePosition + heading * (racer.Speed * dt);
        if (track.TryFreeMove(racer.FreePosition, destination, out var grounded))
            racer.FreePosition = grounded;
        else
            racer.Speed = Mathf.MoveToward(racer.Speed, 0, GroundBraking * dt);

        UpdateRoadProjection(racer);
        if (Math.Abs(racer.Lane) >= track.WidthAt(racer.Distance) - .14f || heading.Dot(track.Forward(racer.Distance)) < .80f)
            return;
        racer.FreeDriving = false;
        float rejoinPace = 6 * SteeringPace(racer.Speed);
        laneVelocity = Mathf.Clamp(heading.Dot(track.Right(racer.Distance)) * racer.Speed, -rejoinPace, rejoinPace);
    }

    private void UpdateRoadProjection(MountedRacer racer)
    {
        (racer.Distance, racer.Lane) = track.Project(racer.FreePosition, racer.Distance);
    }

    private void RecordGate(MountedRacer racer, Vector3 before, Vector3 after, float dt)
    {
        if (!track.TryCrossGate(racer.GatesCleared, before, after, out float fraction)) return;
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

    private float RaceProgress(MountedRacer racer)
    {
        if (track.IsSprint && racer.Finish >= 0) return track.Gate(track.GateCount);
        float next = track.Gate(racer.GatesCleared);
        float last = racer.Checkpoint;
        if (track.IsSprint) return Mathf.Clamp(racer.Distance, last, next - .01f);
        float distance = racer.Distance + Mathf.Round(((last + next) * .5f - racer.Distance) / track.Length) * track.Length;
        return Mathf.Clamp(distance, last, next - .01f);
    }
}




