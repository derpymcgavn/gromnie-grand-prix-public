using Godot;

public sealed partial class MountedRacer
{
    internal readonly record struct SnowboardLanding(float NormalSpeed, float AngleRadians, bool Wipeout);

    private const float BoardClearance = .06f;
    internal const float SnowboardShoulder = 23;
    private const float BoardContactTolerance = .027f;
    private float boardAirTime, boardLastSpeed, boardPitchRate, boardRollRate;

    internal bool HasSnowboardPhysics { get; private set; }
    internal bool SnowboardAirborne { get; private set; }
    internal Vector3 SnowboardPosition { get; private set; }
    internal Vector3 SnowboardHeading { get; private set; } = Vector3.Forward;
    internal Vector3 SnowboardVelocity { get; private set; }
    internal float SnowboardPitch { get; private set; }
    internal float SnowboardRoll { get; private set; }
    internal float SnowboardLandingImpact { get; private set; }

    internal void ResetSnowboardPhysics()
    {
        HasSnowboardPhysics = SnowboardAirborne = false;
        boardAirTime = boardLastSpeed = boardPitchRate = boardRollRate = 0;
        SnowboardPitch = SnowboardRoll = SnowboardLandingImpact = 0;
        SnowboardPosition = SnowboardVelocity = Vector3.Zero;
        SnowboardHeading = Vector3.Forward;
    }

    internal void ResumeSnowboard(BogCircuit track, Vector3 position, Vector3 heading, float speed)
    {
        ResetSnowboardPhysics();
        StartSnowboardContact(track, track.FreeGround(position) + Vector3.Up * BoardClearance, heading, speed);
        UpdateBoardStation(track);
    }

    internal void ApplyReplicaSnowboardPhysics(Vector3 position, Vector3 velocity, Vector3 heading, float pitch, float roll, bool airborne)
    {
        if (!UsesSnowboard || !position.IsFinite() || !velocity.IsFinite() || !heading.IsFinite() ||
            !float.IsFinite(pitch) || !float.IsFinite(roll) || velocity.LengthSquared() > 110 * 110)
        {
            ResetSnowboardPhysics();
            return;
        }
        var flatHeading = BoardFlat(heading);
        if (flatHeading.LengthSquared() < .01f) return;
        HasSnowboardPhysics = true;
        SnowboardLandingImpact = SnowboardAirborne && !airborne ? Math.Max(0, -SnowboardVelocity.Y) : 0;
        SnowboardAirborne = airborne;
        SnowboardPosition = position;
        SnowboardVelocity = velocity;
        SnowboardHeading = flatHeading.Normalized();
        SnowboardPitch = Mathf.Clamp(pitch, -1.40f, 1.40f);
        SnowboardRoll = Mathf.Clamp(roll, -1.25f, 1.25f);
        boardLastSpeed = Speed;
    }

    internal bool LaunchSnowboardJump(float speed)
    {
        if (!HasSnowboardPhysics || SnowboardAirborne || !float.IsFinite(speed) || speed <= 0) return false;
        SnowboardVelocity += Vector3.Up * Math.Min(speed, 12);
        SnowboardAirborne = true;
        boardAirTime = 0;
        boardLastSpeed = Speed;
        return true;
    }

    internal SnowboardLanding StepSnowboardPhysics(BogCircuit track, float dt, float steer, bool pushing, bool brake,
        bool carving, float boostAcceleration, float powder)
    {
        if (!UsesSnowboard || dt < 0 || !float.IsFinite(dt)) return default;
        SnowboardLandingImpact = 0;
        if (!HasSnowboardPhysics)
            StartSnowboardContact(track, track.Surface(Distance, Lane) + Vector3.Up * BoardClearance,
                track.Forward(Distance), Speed);
        if (dt == 0) return default;

        // Existing pickups and hazards can change the racer's speed between physics ticks.
        if (Speed < boardLastSpeed - .05f)
        {
            float ratio = Mathf.Clamp(Speed / Math.Max(boardLastSpeed, .01f), 0, 1);
            SnowboardVelocity = SnowboardAirborne
                ? BoardFlat(SnowboardVelocity) * ratio + Vector3.Up * SnowboardVelocity.Y
                : SnowboardVelocity * ratio;
        }
        steer = Mathf.Clamp(steer, -1, 1);
        powder = Mathf.Clamp(powder, 0, 1);
        boostAcceleration = Mathf.Clamp(boostAcceleration, 0, 28);
        dt = Math.Min(dt, .10f);
        int steps = Math.Max(1, Mathf.CeilToInt(dt / (1f / 60)));
        float step = dt / steps;
        SnowboardLanding hardest = default;
        for (int i = 0; i < steps; i++)
        {
            SnowboardLanding landing = SnowboardAirborne
                ? StepBoardInAir(track, step, steer, pushing, brake)
                : StepBoardOnGround(track, step, steer, pushing, brake, carving, boostAcceleration, powder);
            if (landing.Wipeout || landing.NormalSpeed > hardest.NormalSpeed) hardest = landing;
            if (landing.Wipeout) break;
        }
        SnowboardLandingImpact = hardest.NormalSpeed;
        Speed = boardLastSpeed = SnowboardVelocity.Length();
        return hardest;
    }

    private void StartSnowboardContact(BogCircuit track, Vector3 position, Vector3 heading, float speed)
    {
        HasSnowboardPhysics = true;
        SnowboardPosition = position;
        SnowboardHeading = heading;
        Vector3 normal = BoardSurfaceNormal(track, SnowboardPosition);
        Speed = boardLastSpeed = Math.Max(0, speed);
        SnowboardVelocity = BoardSurfaceDirection(SnowboardHeading, normal) * Speed;
        (SnowboardPitch, SnowboardRoll) = BoardSurfaceAngles(normal, SnowboardHeading);
    }

    private SnowboardLanding StepBoardOnGround(BogCircuit track, float dt, float steer, bool pushing, bool brake,
        bool carving, float boostAcceleration, float powder)
    {
        Vector3 start = SnowboardPosition;
        Vector3 normal = BoardSurfaceNormal(track, start);
        Vector3 velocity = SnowboardVelocity.Slide(normal);
        float speed = velocity.Length();
        float normalLoad = RacePhysics.Gravity * normal.Y;
        float lookAhead = Mathf.Clamp(speed * .32f, 1.8f, 9);
        Vector3 guidance = track.Forward(Distance + lookAhead);
        Vector3 right = guidance.Cross(Vector3.Up);
        // Guidance bends the intended carve with the flagged run. It never moves the rider.
        float outside = Mathf.SmoothStep(track.WidthAt(Distance) * .62f, track.WidthAt(Distance) + 4, Math.Abs(Lane));
        float edgeAuthority = Mathf.Lerp(.30f, 1, Mathf.SmoothStep(.8f, 7, speed));
        float steerAngle = (steer * (carving ? .94f : .66f) - Math.Sign(Lane) * outside * .52f) * edgeAuthority;
        var horizontalEdge = (guidance + right * steerAngle).Normalized();
        Vector3 edge = BoardSurfaceDirection(horizontalEdge, normal);
        Vector3 acrossEdge = edge.Cross(normal).Normalized();
        float push = 0;
        if (pushing && !brake)
        {
            float kick = 1 - Mathf.SmoothStep(1.8f, 6.5f, speed);
            float drive = Mathf.Lerp(3.85f, 7.25f, kick);
            // Rider drive tapers with speed while gravity and pickup boosts keep applying their own forces.
            push = drive * (1 - Mathf.SmoothStep(18.5f, 26.5f, speed));
        }
        velocity += (RacePhysics.Acceleration.Slide(normal) + edge * (push + boostAcceleration)) * dt;

        float longitudinal = velocity.Dot(edge), lateral = velocity.Dot(acrossEdge);
        float grip = Mathf.Lerp(2.75f, 2.35f, powder) * (carving ? 1.18f : 1) + (brake ? 1.35f : 0);
        lateral = Mathf.MoveToward(lateral, 0, normalLoad * grip * dt);
        // Turning the deck across the fall line plows snow and sheds speed, even on a steep grade.
        float drag = normalLoad * (.047f + .092f * powder + (carving ? .085f : 0) + (brake ? 2.05f : 0)) +
            speed * speed * (carving ? .0063f : .0047f);
        longitudinal = Mathf.MoveToward(longitudinal, 0, drag * dt);
        velocity = edge * longitudinal + acrossEdge * lateral;

        Vector3 destination = start + velocity * dt;
        Vector3 surface = track.FreeGround(destination) + Vector3.Up * BoardClearance;
        SnowboardHeading = SnowboardHeading.Lerp(horizontalEdge, 1 - MathF.Exp(-9 * dt)).Normalized();
        Lean = Mathf.Lerp(Lean, steer, 1 - MathF.Exp(-8 * dt));
        (float pitch, float roll) = BoardSurfaceAngles(normal, SnowboardHeading);
        ApproachBoardAngles(pitch, roll - Lean * (carving ? .38f : .22f), dt);

        // The board can press into the surface but cannot be pulled down over a crest.
        if (speed > 4.8f && destination.Y - surface.Y > BoardContactTolerance)
        {
            SnowboardAirborne = true;
            boardAirTime = dt;
            SnowboardPosition = RacePhysics.BallisticPosition(start, velocity, dt);
            SnowboardVelocity = velocity + RacePhysics.Acceleration * dt;
            UpdateBoardStation(track);
            return default;
        }

        SnowboardPosition = surface;
        SnowboardVelocity = velocity.Slide(BoardSurfaceNormal(track, surface));
        UpdateBoardStation(track);
        bool outsideTerrain = !track.NativeBounds.Grow(-1).HasPoint(new Vector2(surface.X, surface.Z)) ||
            !track.NativeDry(surface) || Math.Abs(Lane) > track.WidthAt(Distance) + SnowboardShoulder;
        return outsideTerrain ? new SnowboardLanding(speed, 0, true) : default;
    }

    private SnowboardLanding StepBoardInAir(BogCircuit track, float dt, float steer, bool pushing, bool brake)
    {
        Vector3 start = SnowboardPosition, velocity = SnowboardVelocity;
        Vector3 destination = RacePhysics.BallisticPosition(start, velocity, dt);
        boardAirTime += dt;
        float pitchControl = brake ? .78f : pushing ? -.55f : 0;
        SnowboardPitch = Mathf.Clamp(SnowboardPitch + pitchControl * dt, -1.35f, 1.35f);
        SnowboardRoll = Mathf.MoveToward(SnowboardRoll, -steer * .54f, (Math.Abs(steer) > .08f ? .95f : .45f) * dt);

        // Sweep the ballistic arc against the heightfield before resolving the contact impulse.
        int samples = Math.Max(2, Mathf.CeilToInt(start.DistanceTo(destination) / .38f));
        for (int sample = 1; sample <= samples; sample++)
        {
            float stop = dt * sample / samples;
            Vector3 point = RacePhysics.BallisticPosition(start, velocity, stop);
            if (point.Y > track.FreeGround(point).Y + BoardClearance) continue;
            float begin = dt * (sample - 1) / samples;
            for (int i = 0; i < 6; i++)
            {
                float mid = (begin + stop) * .5f;
                Vector3 check = RacePhysics.BallisticPosition(start, velocity, mid);
                if (check.Y > track.FreeGround(check).Y + BoardClearance) begin = mid;
                else stop = mid;
            }
            return LandSnowboard(track, RacePhysics.BallisticPosition(start, velocity, stop),
                velocity + RacePhysics.Acceleration * stop);
        }

        SnowboardPosition = destination;
        SnowboardVelocity = velocity + RacePhysics.Acceleration * dt;
        UpdateBoardStation(track);
        bool bounds = !track.NativeBounds.Grow(-1).HasPoint(new Vector2(destination.X, destination.Z));
        return bounds || boardAirTime > 6 ? new SnowboardLanding(Math.Abs(SnowboardVelocity.Y), 0, true) : default;
    }

    private SnowboardLanding LandSnowboard(BogCircuit track, Vector3 point, Vector3 impactVelocity)
    {
        SnowboardPosition = track.FreeGround(point) + Vector3.Up * BoardClearance;
        Vector3 normal = BoardSurfaceNormal(track, SnowboardPosition);
        float impact = Math.Max(0, -impactVelocity.Dot(normal));
        Vector3 boardUp = Basis.FromEuler(new Vector3(SnowboardPitch, BogCircuit.Yaw(SnowboardHeading), SnowboardRoll)).Y;
        float mismatch = MathF.Acos(Mathf.Clamp(boardUp.Dot(normal), -1, 1));
        bool wipeout = !track.NativeDry(point) ||
            (boardAirTime >= .10f && (impact > 11.2f || impact > 6.1f && mismatch > 1.02f));
        Vector3 tangent = impactVelocity.Slide(normal);
        Vector3 edge = BoardSurfaceDirection(SnowboardHeading, normal);
        Vector3 acrossEdge = edge.Cross(normal);
        float sideways = Mathf.MoveToward(tangent.Dot(acrossEdge), 0, impact * 1.2f);
        SnowboardVelocity = (edge * tangent.Dot(edge) + acrossEdge * sideways) *
            (1 - Mathf.SmoothStep(1, 11.2f, impact) * .38f);
        SnowboardAirborne = false;
        boardAirTime = boardPitchRate = boardRollRate = 0;
        UpdateBoardStation(track);
        return new SnowboardLanding(impact, mismatch, wipeout || Math.Abs(Lane) > track.WidthAt(Distance) + SnowboardShoulder);
    }

    private void UpdateBoardStation(BogCircuit track)
    {
        (Distance, Lane) = track.Project(SnowboardPosition, Distance);
        FreeDriving = Math.Abs(Lane) > track.WidthAt(Distance) + .4f;
        FreePosition = SnowboardPosition;
        FreeYaw = BogCircuit.Yaw(SnowboardHeading);
    }

    private void ApproachBoardAngles(float pitch, float roll, float dt)
    {
        boardPitchRate += ((pitch - SnowboardPitch) * 78 - boardPitchRate * 13) * dt;
        boardRollRate += ((roll - SnowboardRoll) * 87 - boardRollRate * 14) * dt;
        SnowboardPitch = Mathf.Clamp(SnowboardPitch + boardPitchRate * dt, -1.35f, 1.35f);
        SnowboardRoll = Mathf.Clamp(SnowboardRoll + boardRollRate * dt, -1.1f, 1.1f);
    }

    private static Vector3 BoardSurfaceNormal(BogCircuit track, Vector3 point)
    {
        const float sample = .73f;
        float dx = track.FreeGround(point + Vector3.Right * sample).Y - track.FreeGround(point + Vector3.Left * sample).Y;
        float dz = track.FreeGround(point + Vector3.Back * sample).Y - track.FreeGround(point + Vector3.Forward * sample).Y;
        return new Vector3(-dx, sample * 2, -dz).Normalized();
    }

    private static (float Pitch, float Roll) BoardSurfaceAngles(Vector3 normal, Vector3 forward) =>
        (MathF.Atan2(-normal.Dot(forward), normal.Y), MathF.Atan2(-normal.Dot(forward.Cross(Vector3.Up)), normal.Y));

    // Lift the intended yaw onto the contact plane while keeping the same horizontal heading.
    private static Vector3 BoardSurfaceDirection(Vector3 heading, Vector3 normal) =>
        (heading with { Y = -normal.Dot(heading) / Math.Max(normal.Y, .05f) }).Normalized();

    private static Vector3 BoardFlat(Vector3 vector) => vector with { Y = 0 };
}
