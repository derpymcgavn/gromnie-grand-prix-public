using Godot;

public partial class GrandPrix
{
    // Tidal Siraluun feather icon from the native item catalog.
    private const uint GromnatrossIcon = 0x060036b0;
    private const float BasicFlightDistance = 132;
    private const int RivalGromnatrossSkin = 2;
    private readonly record struct Flyover(string Name, float Start, float End, float Height, float Sweep, bool Direct = false,
        Vector3? CitadelFlyby = null);
    private Flyover? flyover;
    private bool requestRoll;

    private string FlightControlHint(FlightRide flight)
    {
        if (flight.Rolling) return "Barrel rolling";
        if (flight.RollCooldown > 0) return $"Roll ready in {flight.RollCooldown:F1}s";
        if (raceTouchUi is not null)
        {
            if (!FreeFlightControls) return "Steer to bank · Air roll";
            return flight.DiveReady ? "Bank · Dive · Climb · Air roll" : "Release, then hold Dive";
        }
        if (!FreeFlightControls) return "A/D bank · R barrel roll";
        return flight.DiveReady ? "A/D bank · W dive · S climb · R roll" : "A/D bank · Re-press W to dive · S climb";
    }

    private static Flyover VoidFlyover(BogCircuit circuit) =>
        new("SINGULARITY FLYOVER", circuit.Length * .057f, circuit.Length * .145f, 84, 0, Direct: true);

    private void BuildFlyover()
    {
        flyover = activeTown switch
        {
            YanshiXlCourseId => new Flyover("YANSHI FLYOVER", 74, 385, 62, -77),
            XlCourseOffset + 72 => new Flyover("RIVER BLUFF FLYOVER", 108, 376, 57, -86),
            VoidCourseId => VoidFlyover(track),
            _ => null
        };
        if (ridgeCircuits.TryGetValue(activeTown, out var ridge))
        {
            var (launchDistance, landingDistance) = ridge.FlightSector(track);
            flyover = new(ridge.FlightName, launchDistance, landingDistance, ridge.FlightHeight, 0, Direct: true);
        }
        if (landmarkCircuits.TryGetValue(activeTown, out var landmark) && landmark.Battle is { } battle)
        {
            var (launchDistance, landingDistance) = battle.FlightSector(track);
            flyover = new("AERBAX'S CITADEL · BANK TO DODGE", launchDistance, landingDistance, CitadelClimb, 0,
                Direct: true, CitadelFlyby: battle.FlybyPoint(track));
        }
        if (flyover is not { } route) return;

        float launchWidth = track.HalfWidth * .24f;
        track.Ribbon(courseItems, -launchWidth, launchWidth, .055f, "4b9994", route.Start - 2.7f, route.Start + 2.7f);
        for (int i = 0; i < 3; i++)
        {
            float stripe = route.Start + i * 1.25f - 2;
            track.Ribbon(courseItems, -launchWidth, launchWidth, .07f, "a5ebd5", stripe, stripe + .24f);
        }
        var start = new Node3D { Position = track.Point(route.Start) };
        courseItems.AddChild(start);
        var launchIcon = ItemIcon(route.Direct && creation.OlthoiSpitter is not null ? RaceItem.OlthoiLift : RaceItem.Gromnatross);
        start.AddChild(new Sprite3D
        {
            Texture = launchIcon, PixelSize = 1.85f / (launchIcon?.GetWidth() ?? 1), Position = Vector3.Up * 1.65f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
        start.AddChild(new Label3D
        {
            Text = route.Name, Position = Vector3.Up * 4.4f, FontSize = 60, PixelSize = .025f,
            OutlineSize = 11, Modulate = new Color("c2f7e4"), OutlineModulate = new Color("223836"),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, VisibilityRangeEnd = 140, VisibilityRangeEndMargin = 15
        });

        var mesh = new TorusMesh { InnerRadius = .981f, OuterRadius = 1, Rings = 40, RingSegments = 4 };
        var material = PickupColor("8ee9e9");
        foreach (float t in new[] { .33f, .54f, .73f })
        {
            var position = FlyoverPoint(route, t);
            var ring = new Node3D { Position = position + Vector3.Up * 1.55f };
            courseItems.AddChild(ring);
            Vector3 flightLine = FlyoverPoint(route, t + .008f) - position;
            ring.LookAt(ring.Position + flightLine);
            var model = BogCircuit.MeshAt(ring, mesh, Vector3.Zero, Vector3.One * 10.8f, material);
            model.RotationDegrees = new Vector3(90, 0, 0);
            model.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        if (route.Direct)
        {
            track.Ribbon(courseItems, -launchWidth, launchWidth, .085f, "70c7ba", route.End - 2, route.End + 3.2f);
            foreach (int edge in new[] { -1, 1 })
            {
                var landing = track.Surface(route.End, track.HalfWidth * edge * .72f);
                BogCircuit.Box(courseItems, landing + Vector3.Up * 1.8f, new(.26f, 3.6f, .26f), BogCircuit.Paint("8ae1d0"));
            }
        }
    }

    private bool TryFlyover(MountedRacer racer, float before)
    {
        if (flyover is not { } route) return false;
        if (track.IsVoid)
        {
            float offset = Mathf.Floor(before / track.Length) * track.Length;
            route = route with { Start = route.Start + offset, End = route.End + offset };
        }
        if (racer.GatesCleared == 0 || route.Start <= track.Gate(racer.GatesCleared - 1) ||
            route.End >= track.Gate(racer.GatesCleared) ||
            !Crossed(before, racer.Distance, route.Start) || Math.Abs(racer.Lane) > track.HalfWidth * .24f) return false;
        return LaunchFlight(racer, route);
    }

    private Vector3[] FlyoverMapPoints()
    {
        if (flyover is not { } route) return [];
        return Enumerable.Range(0, 13).Select(i => FlyoverPoint(route, i / 12f)).ToArray();
    }

    private Vector3 FlyoverPoint(Flyover route, float t) => route.Direct
        ? SummitPoint(track, route.Start, route.End, t, 0, route.Height, 0, route.CitadelFlyby)
        : FlightPoint(Mathf.Lerp(route.Start, route.End, t), t, 0, route.Height, route.Sweep, 0);

    private bool LaunchFlight(MountedRacer racer, Flyover? route = null, bool olthoi = false)
    {
        if (racer.FreeDriving || racer.Flight is not null || racer.EjectTime > 0 || racer.ResetHold > 0 || racer.Finish >= 0)
            return false;
        var spitter = olthoi || route is { Direct: true } ? creation.OlthoiSpitter : null;
        if (olthoi && spitter is null) return false;
        float at = racer.Distance;
        if (track.IsSprint && track.Gate(track.GateCount) - at < 12 || spitter is null && !racer.PrepareFallingPose()) return false;

        FlightMount flyer = spitter is not null ? new OlthoiSpitter(assets, spitter)
            : new Gromnatross(assets, GromnieLook(racer == Player ? skinIndex : RivalGromnatrossSkin));
        float end = route?.End ?? at + BasicFlightDistance;
        if (track.IsSprint) end = Math.Min(end, track.Length - 2);
        var flight = new FlightRide(flyer, at, end, racer.Lane,
            route?.Height ?? 19, route?.Sweep ?? 0)
        {
            Position = track.Surface(at, racer.Lane) + Vector3.Up * .4f,
            Heading = track.Forward(at)
        };
        if (route is { Direct: true } summit)
        {
            flight.Direct = true;
            flight.CitadelFlyby = summit.CitadelFlyby;
            Vector3 previous = flight.Position;
            float length = 0;
            for (int i = 1; i <= 24; i++)
            {
                Vector3 point = SummitPoint(track, at, summit.End, i / 24f, racer.Lane, flight.Height, 0, flight.CitadelFlyby);
                length += previous.DistanceTo(point);
                previous = point;
            }
            flight.CourseMetresPerAirMetre = (summit.End - at) / Math.Max(length, 1);
        }
        ClearRacerPowers(racer);
        ResetRivalMotion(racer);
        racer.Boost = 0;
        racer.Speed = Math.Min(racer.Speed, flight.Height > 20 ? 64 : 54);
        racer.TakeFlight(flight);
        if (racer == Player)
        {
            charge = lateral = 0;
            drifting = false;
            Callout(route?.Name ?? (spitter is not null ? "OLTHOI LIFT!" : "GROMNATROSS LIFT!"), 1.45f);
        }
        return true;
    }

    private Vector3 FlightPoint(float distance, float t, float lane, float height, float sweep, float steer)
    {
        float travel = Mathf.Sin(Mathf.Pi * t);
        float altitude = Mathf.SmoothStep(0, .27f, t) * (1 - Mathf.SmoothStep(.72f, 1, t));
        float offset = lane + sweep * travel * travel + steer * travel;
        if (sweep == 0) offset = Mathf.Clamp(offset, -track.WidthAt(distance) + .5f, track.WidthAt(distance) - .5f);
        Vector3 at = track.Surface(distance, offset);
        at.Y += height * altitude + .4f;
        return at;
    }

    private static Vector3 SummitPoint(BogCircuit track, float start, float end, float t, float lane, float height, float steer,
        Vector3? citadelFlyby = null)
    {
        Vector3 launch = track.Surface(start, lane), landing = track.Point(end);
        float tangent = launch.DistanceTo(landing) * .26f;
        Vector3 outbound = track.Forward(start), inbound = track.Forward(end);
        Vector3 enter = launch + outbound * tangent, exit = landing - inbound * tangent;
        Vector3 point = launch.BezierInterpolate(enter, exit, landing, t);
        if (citadelFlyby is { } anchor)
        {
            Vector3 middle = launch.BezierInterpolate(enter, exit, landing, .5f) + Vector3.Up * (height + 4.9f);
            float weight = 4 * t * (1 - t);
            point += (anchor - middle) * weight * weight;
        }
        float midflight = Mathf.Sin(Mathf.Pi * t);
        var bankDirection = citadelFlyby is null ? outbound.Lerp(inbound, t).Normalized() : (landing - launch).Normalized();
        point += bankDirection.Cross(Vector3.Up).Normalized() * (steer * midflight);
        float lift = 4.5f * Mathf.SmoothStep(0, .11f, t) * (1 - Mathf.SmoothStep(.89f, 1, t));
        float climb = citadelFlyby is null ? midflight * midflight : Mathf.SmoothStep(0, .37f, t) * (1 - Mathf.SmoothStep(.63f, 1, t));
        point.Y = Math.Max(point.Y + height * climb + lift, track.FreeGround(point).Y + lift) + .4f;
        return point;
    }

    private static bool CheckSummitFlight(BogCircuit track, float start, float end)
    {
        if (start <= BogCircuit.Grid + 30 || end <= start + 180 || end >= track.Length - 55) return false;
        Vector3 launch = track.Point(start), landing = track.Point(end);
        if (launch.Y - track.FreeGround(launch).Y > 2.3f || landing.Y - track.FreeGround(landing).Y > 2.3f) return false;
        float canyon = Math.Min(launch.Y, landing.Y) - track.FreeGround(launch.Lerp(landing, .5f)).Y;
        return canyon > 55 && launch.DistanceTo(landing) > 260;
    }

    private void TickFlight(MountedRacer racer, FlightRide flight, float dt, float steer, bool free = false,
        bool dive = false, bool climb = false)
    {
        Vector3 previous = flight.Position;
        float before = racer.Distance;
        flight.Age += dt;
        float distance = flight.End - flight.Start;
        // A held throttle carries into neutral cruise. Press it again in the air to dive.
        if (!dive) flight.DiveReady = true;
        float pitchInput = free ? (climb ? 1 : 0) - (dive && flight.DiveReady ? 1 : 0) : 0;
        flight.AirPitch = Mathf.Lerp(flight.AirPitch, pitchInput, 1 - Mathf.Exp(-3.8f * dt));
        float speedAdjustment = flight.AirPitch * FlightControlWeight((before - flight.Start) / distance) * (flight.Direct ? 10 : 6);
        racer.Speed = Mathf.MoveToward(racer.Speed, (flight.Height > 20 ? 43 : 36) - speedAdjustment, 19 * dt);
        racer.Distance = Mathf.MoveToward(racer.Distance, flight.End, racer.Speed * flight.CourseMetresPerAirMetre * dt);
        float t = (racer.Distance - flight.Start) / distance;
        racer.Lane = Mathf.Lerp(flight.StartLane, 0, Mathf.SmoothStep(.44f, 1, t));
        float bankRange = free ? flight.Direct ? Mathf.Clamp(distance * .075f, 14, 29)
            : flight.Sweep != 0 ? 19 : Mathf.Clamp(track.WidthAt(racer.Distance) * .88f, 5.5f, 15) : 3.1f;
        flight.AirSteer = Mathf.Lerp(flight.AirSteer, steer * bankRange, 1 - Mathf.Exp(-3.2f * dt));
        Vector3 next = flight.Direct ? SummitPoint(track, flight.Start, flight.End, t, flight.StartLane, flight.Height,
                flight.AirSteer, flight.CitadelFlyby)
            : FlightPoint(racer.Distance, t, racer.Lane, flight.Height, flight.Sweep, flight.AirSteer);
        float pitchOffset = flight.AirPitch * FlightControlWeight(t);
        if (Math.Abs(pitchOffset) > .0001f)
        {
            float heightRange = flight.Direct || flight.Sweep != 0 ? Mathf.Clamp(flight.Height * .40f, 12, 27) : 9;
            float height = next.Y + pitchOffset * heightRange;
            // Use the deck at the guided height so dives cannot shift to a different level at an overpass.
            next.Y = pitchOffset > 0 ? height : Math.Max(height, Math.Min(next.Y, track.GroundOrRoadHeight(next) + 3.4f));
        }
        Vector3 motion = next - flight.Position;
        var flat = new Vector3(motion.X, 0, motion.Z);
        if (flat.LengthSquared() > .0001f) flight.Heading = flat.Normalized();
        float posture = Mathf.Atan2(motion.Y, flat.Length()) + pitchOffset * .15f;
        flight.Pitch = Mathf.Lerp(flight.Pitch, Mathf.Clamp(posture, -.52f, .47f), 1 - Mathf.Exp(-6 * dt));
        flight.Bank = Mathf.Lerp(flight.Bank, -steer * (free ? .48f : .32f), 1 - Mathf.Exp(-4 * dt));
        flight.Position = next;
        if (flight.CitadelFlyby is not null && CheckCitadelSpellHits(racer, flight, previous)) return;
        if (track.IsVoid)
        {
            CheckVoidBattleHits(racer, racers.IndexOf(racer), previous, next);
            if (racer.EjectTime > 0) return;
        }

        // Wings can pass over a gate. The gate still requires the real horizontal crossing.
        // Scenic detours begin and end between the same pair of gates.
        if (!flight.Direct && flight.Sweep == 0)
            RecordGate(racer, track.GroundedRacePoint(previous, before), track.GroundedRacePoint(next, racer.Distance), dt);
        if (t >= 1 || racer.Finish >= 0)
        {
            racer.Land();
            racer.Speed = Math.Min(racer.Speed, DriftSpeedCap);
            GrantBoost(racer, .55f);
            if (racer == Player) Callout("BACK IN THE SADDLE", .85f);
        }
        racer.Draw(track, dt, true);
    }

    private static float FlightControlWeight(float t) => Mathf.SmoothStep(.06f, .24f, t) * (1 - Mathf.SmoothStep(.73f, .98f, t));
}
