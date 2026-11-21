using Godot;
using DerethSurvivors;
using System.Runtime.CompilerServices;

public sealed partial class MountedRacer
{
    public Node3D Root { get; } = new();
    public AcActor Mount { get; }
    public AcActor Rider { get; }
    public Node3D Saddle { get; } = new();
    public string Name { get; }
    public float Distance, Lane, Speed, Lean, Boost, Slowed, Finish = -1;
    public RaceItem Item;
    internal int ItemCharges, Pyreals;
    internal float UseHeldItemAt;
    internal float SudsedUntil;
    internal float Leap, Might, Invulnerable, Phased, Hexed, SpinTime, PowerEffectAt;
    internal AcActor? PowerForm;
    internal bool DodgesItems => Falling || LeapHeight > .32f || Might > 0 || Invulnerable > 0 || Phased > 0;
    internal const float LeapDuration = .90f, SpinDuration = .60f;
    internal float LeapHeight => Leap > 0 ? 2.6f * Mathf.Sin(Mathf.Pi * (1 - Leap / LeapDuration)) : 0;
    public float Checkpoint, ResetHold, ItemGrace, Frozen, Ward;
    public float SwipeCooldown, SwingTime, EjectTime;
    internal float GuardWindow, GuardCooldown, GuardFlash;
    internal float NextBoostSound;
    internal float SuperDriftUntil;
    internal bool GroundDrifting;
    internal int BoostVoiceTake;
    public int Stability = 100, GatesCleared;
    internal readonly List<float> LapCrossings = [];
    public bool FreeDriving;
    internal float FallTime;
    internal bool Falling => FallTime > 0;
    internal readonly bool[] DustContact;
    internal FlightRide? Flight { get; private set; }
    public Vector3 FreePosition;
    public float FreeYaw;
    private Transform3D seatedTransform;
    internal Ejection EjectMotion { get; private set; }
    public const float EjectDuration = .92f;
    public int SwingSide;
    public bool SwingLanded;
    internal RaceWeapon Weapon { get; }
    internal RaceShield Shield { get; }
    private readonly (Node3D Part, Transform3D RestPose)[] weaponArm;
    private readonly (Node3D Part, Transform3D RestPose)[] shieldArm;

    internal RaceMount Rig { get; }
    private readonly Basis saddleRest;
    private readonly Vector3 saddleOffset;
    private readonly MountFoot[] footContacts;
    private readonly Node3D seat;
    private readonly float riderScale, flightRiderScale, relativeScale;
    private readonly RoadContact[] mountRoadContacts, riderRoadContacts, companionRoadContacts;
    private static readonly ConditionalWeakTable<Mesh, Vector3[]> RoadContactVertices = new();
    private float roadPoseLift;
    private readonly record struct RoadContact(MeshInstance3D Instance, Mesh Geometry, Aabb Bounds);

    internal MountedRacer(AcAssets assets, RaceMount rig, RareCatalog.Creature gromnie, CharacterLookDefinition look,
        string name, RaceWeapon weapon, RaceShield shield, int mountColor = 0)
    {
        Name = name;
        Weapon = weapon;
        Shield = shield;
        Rig = rig;
        var skin = rig.ResolveLook(gromnie, mountColor);
        Mount = assets.CreateActor(skin.Setup, skin.Motion, rig.Scale);
        rig.ApplyMountColor(Mount, skin, mountColor);
        bool skates = rig.Kind == RaceMountKind.CarenziSkates;
        if (skates)
        {
            CompanionSkate = assets.CreateActor(skin.Setup, skin.Motion, rig.Scale);
            rig.ApplyMountColor(CompanionSkate, rig.ResolveSkateCompanion(mountColor), mountColor);
            Root.AddChild(CompanionSkate);
            AddSkateSaddle(Mount);
            AddSkateSaddle(CompanionSkate);
        }
        bool platform = rig.Platform != MountPlatform.None;
        if (platform) FitPlatform(assets);
        bool gromnieRig = rig.Kind == RaceMountKind.Gromnie;
        saddleRest = platform || skates ? Basis.Identity : gromnieRig ? new Basis(Vector3.Up, Mathf.Pi) : Mount.BodyPartTransform(rig.SeatPart).Basis.Inverse();
        saddleOffset = gromnieRig ? rig.SeatOffset : saddleRest * rig.SeatOffset;
        footContacts = rig.Feet.Select(foot => foot with
        {
            Offset = Mount.BodyPartTransform(foot.Part).Basis.Inverse() * foot.Offset
        }).ToArray();
        DustContact = new bool[gromnieRig ? 2 : footContacts.Length * (skates ? 2 : 1)];
        Root.AddChild(Mount);
        (skates ? Root : Mount).AddChild(Saddle);
        relativeScale = RaceMount.Get(RaceMountKind.Gromnie).Scale / (skates ? 1 : rig.Scale);
        if (!platform && !skates && rig.Kind is not (RaceMountKind.Asheron or RaceMountKind.BaelZharon)) BuildSaddle(Saddle, relativeScale);
        seat = new Node3D();
        Saddle.AddChild(seat);
        flightRiderScale = Mathf.Clamp(look.Scale, .82f, 1.12f) * .81f;
        riderScale = flightRiderScale * relativeScale;
        Rider = assets.CreateActor(look.Setup, look.Motion, riderScale);
        Rider.SetCharacterAppearance(look);
        seat.AddChild(Rider);
        PoseMounted();
        Rider.Hold(weapon.Setup, 1, scale: weapon.Scale);
        Rider.HoldShield(shield.Look, shield.Look?.Scale ?? 1);
        weaponArm = Rider.Setup.Holdings.TryGetValue(1, out var holding) && holding.Part >= 2
            ? Enumerable.Range(holding.Part - 2, 3).Select(i => (Rider.Part(i), Rider.Part(i).Transform)).ToArray()
            : [];
        shieldArm = shield.Look is not null && Rider.Setup.Holdings.TryGetValue(3, out var offHand) && offHand.Part >= 2
            ? Enumerable.Range(offHand.Part - 2, 3).Select(i => (Rider.Part(i), Rider.Part(i).Transform)).ToArray()
            : [];
        UpdateSaddle(0);
        if (CompanionSkate is { } companion) DrawSkates(companion, 0, false, 0);
        mountRoadContacts = RoadContacts(Mount.BodyMeshes);
        riderRoadContacts = RoadContacts(Rider.BodyMeshes);
        companionRoadContacts = CompanionSkate is { } skate ? RoadContacts(skate.BodyMeshes) : [];
    }

    internal static void BuildSaddle(Node3D anchor, float scale)
    {
        var tack = new Node3D { Scale = Vector3.One * scale };
        anchor.AddChild(tack);
        var leather = BogCircuit.Paint("583423");
        var blanket = BogCircuit.Paint("9e5338");
        var brass = BogCircuit.Paint("e0b263");
        var cushion = new SphereMesh { RadialSegments = 16, Rings = 5 };
        BogCircuit.MeshAt(tack, cushion, new(0, -.075f, 0), new(.73f, .12f, .63f), blanket);
        BogCircuit.MeshAt(tack, cushion, new(0, -.018f, .026f), new(.56f, .13f, .47f), leather);
        foreach (int side in new[] { -1, 1 })
        {
            BogCircuit.Box(tack, new(side * .31f, -.21f, 0), new(.035f, .33f, .095f), leather);
            BogCircuit.Box(tack, new(side * .32f, -.37f, -.03f), new(.13f, .037f, .16f), brass);
        }
    }

    private void PoseMounted()
    {
        if (CompanionSkate is not null) PoseSkateRider();
        else if (Rig.Platform != MountPlatform.None) PosePlatformRider();
        else PoseSeated(relativeScale, Rig.LegSpread, Rig.Kind == RaceMountKind.Asheron, Rig.Kind == RaceMountKind.BaelZharon ? -.44f : 0);
    }

    private void PoseSeated(float offsetScale, float legSpread, bool piggyback = false, float legPitch = 0)
    {
        if (!Rider.Play(0x4300013d, true) && !Rider.Play(0x41000013, true))
            throw new InvalidDataException("This rider is missing a native sitting motion.");
        Rider.HoldFinalPose = true;
        Rider.Tick(60, false, Vector3.Zero);
        Vector3 hip = Rider.BodyPartTransform(0).Origin;
        Rider.Rotation = Vector3.Zero;
        Rider.Position = new Vector3(0, .063f, .025f) * offsetScale - hip * Rider.Scale.Y;
        SplayLeg(1, [1, 2, 3, 4, 25], -1, legSpread, legPitch);
        SplayLeg(5, [5, 6, 7, 8, 26], 1, legSpread, legPitch);
        if (piggyback) PosePiggybackGrip();
        Rider.RefreshBodyPoseCache();
    }

    internal bool PrepareFallingPose() => Rider.Play(0x40000015, false) || Rider.Play(0x40000015, true);

    internal void TakeFlight(FlightRide flight)
    {
        Flight = flight;
        SuperDriftUntil = 0;
        GroundDrifting = false;
        Root.AddChild(flight.Flyer.Root);
        Rider.Reparent(flight.Flyer.RiderAnchor, false);
        Rider.Scale = Vector3.One * flightRiderScale;
        if (flight.Flyer.SeatsRider) PoseSeated(1, 1.25f);
        Rider.SetHeldVisible(false);
        Rider.SetShieldVisible(false);
        SetMountVisible(false);
        Array.Clear(DustContact);
        SwingTime = 0;
    }

    internal void Land()
    {
        if (Flight is not { } flight) return;
        Rider.Reparent(seat, false);
        Rider.Scale = Vector3.One * riderScale;
        Rider.SetHeldVisible(true);
        Rider.SetShieldVisible(true);
        PoseMounted();
        flight.Flyer.Root.Hide();
        flight.Flyer.Root.QueueFree();
        Flight = null;
        SetMountVisible(true);
    }

    internal void SetMountVisible(bool visible)
    {
        Mount.Visible = visible;
        if (CompanionSkate is not { } companion) return;
        companion.Visible = Saddle.Visible = visible;
    }

    private void SplayLeg(int hip, int[] chain, int side, float legSpread, float legPitch)
    {
        if (Rider.Setup.Parts.Length < 17) return;
        Vector3 joint = Rider.BodyPartTransform(hip).Origin;
        var turn = new Basis(Vector3.Right, legPitch) * new Basis(Vector3.Up, -side * .78f * legSpread) *
            new Basis(Vector3.Back, side * .65f * legSpread);
        foreach (int part in chain)
        {
            if (part >= Rider.Setup.Parts.Length) continue;
            var pose = Rider.Part(part).Transform;
            Rider.Part(part).Transform = new(turn * pose.Basis, joint + turn * (pose.Origin - joint));
        }
    }

    private void PosePiggybackGrip()
    {
        if (Rider.Setup.Parts.Length < 17) return;
        int hand = Rider.Setup.Holdings.TryGetValue(1, out var rightHand) && rightHand.Part == 12 ? 15 : 12;
        var upper = Rider.Part(hand - 2);
        var lower = Rider.Part(hand - 1);
        var palm = Rider.Part(hand);
        Vector3 shoulder = upper.Position, elbow = lower.Position, wrist = palm.Position;
        float upperLength = shoulder.DistanceTo(elbow), lowerLength = elbow.DistanceTo(wrist);
        if (Math.Min(upperLength, lowerLength) < .01f) return;

        float side = Math.Sign(shoulder.X);
        Vector3 reach = new(-side * .07f, .08f, -.42f);
        float length = Mathf.Clamp(reach.Length(), Math.Abs(upperLength - lowerLength) + .01f, upperLength + lowerLength - .01f);
        Vector3 forward = reach.Normalized();
        Vector3 outward = new(side, -.45f, 0);
        outward = (outward - forward * outward.Dot(forward)).Normalized();
        float along = (upperLength * upperLength - lowerLength * lowerLength + length * length) / (2 * length);
        Vector3 joint = shoulder + forward * along + outward * MathF.Sqrt(Math.Max(0, upperLength * upperLength - along * along));
        Vector3 grip = shoulder + forward * length;
        var aimUpper = new Basis(new Quaternion((elbow - shoulder).Normalized(), (joint - shoulder).Normalized()));
        var aimLower = new Basis(new Quaternion((wrist - elbow).Normalized(), (grip - joint).Normalized()));
        upper.Basis = aimUpper * upper.Basis;
        lower.Transform = new Transform3D(aimLower * lower.Basis, joint);
        palm.Transform = new Transform3D(aimLower * palm.Basis, grip);
    }

    private void UpdateSaddle(float lean)
    {
        var back = Rig.Platform == MountPlatform.None && CompanionSkate is null ? Mount.BodyPartTransform(Rig.SeatPart) : Transform3D.Identity;
        Saddle.Position = back.Origin + back.Basis * saddleOffset;
        Saddle.Basis = back.Basis * saddleRest;
        if (Rig.SaddleFollow < 1)
        {
            // The Mukkir folds nearly horizontal while sprinting. Keep its saddle
            // above the animated torso without pitching the rider into its claws.
            var pitch = Saddle.Basis.Orthonormalized().GetRotationQuaternion().Normalized();
            Saddle.Basis = new Basis(Quaternion.Identity.Slerp(pitch, Rig.SaddleFollow).Normalized());
            Saddle.Position = back.Origin + Saddle.Basis * Rig.SeatOffset;
        }
        float swing = SwingTime > 0 ? MathF.Sin((1 - SwingTime / RaceWeapon.SwingDuration) * Mathf.Pi) : 0;
        float tilt = Rig.Kind switch
        {
            RaceMountKind.Asheron => -.32f, RaceMountKind.BaelZharon => .08f,
            RaceMountKind.CarenziSkates => -.16f,
            RaceMountKind.FestivusCarpet or RaceMountKind.IsinDuleDisc => 0, _ => -.10f
        };
        float bodyLean = Rig.Platform == MountPlatform.None ? lean * .29f : 0;
        seat.Rotation = new Vector3(tilt, -SwingSide * swing * .24f, bodyLean - SwingSide * swing * .22f);
        if (Rig.Kind == RaceMountKind.BaelZharon) PoseCarryArms();
        for (int i = 0; i < weaponArm.Length; i++)
        {
            var (limb, rest) = weaponArm[i];
            float motion = swing * (i + 1) / 3f;
            var sweep = new Basis(Vector3.Up, -SwingSide * motion * 1.35f);
            var reach = new Vector3(SwingSide * .6f, .26f, -.39f) * motion;
            limb.Transform = new Transform3D(sweep * rest.Basis, rest.Origin + reach);
        }
        float guard = GuardWindow > 0 || GuardFlash > 0 ? 1 : 0;
        for (int i = 0; i < shieldArm.Length; i++)
        {
            var (limb, rest) = shieldArm[i];
            float raise = guard * (i + 1) / shieldArm.Length;
            var block = new Basis(Vector3.Right, -.55f * raise);
            limb.Transform = new Transform3D(block * rest.Basis, rest.Origin + new Vector3(.08f, .24f, -.27f) * raise);
        }
    }

    private void PoseCarryArms()
    {
        Transform3D carrier = Saddle.Transform * seat.Transform;
        PoseCarryArm(10, -1, carrier);
        PoseCarryArm(13, 1, carrier);
        Mount.RefreshBodyPoseCache();
    }

    private void PoseCarryArm(int shoulderPart, int side, Transform3D carrier)
    {
        var upper = Mount.Part(shoulderPart);
        var lower = Mount.Part(shoulderPart + 1);
        var palm = Mount.Part(shoulderPart + 2);
        Vector3 shoulder = upper.Position, upperBone = lower.Position - shoulder, lowerBone = palm.Position - lower.Position;
        float upperLength = upperBone.Length(), lowerLength = lowerBone.Length();
        Vector3 reach = carrier * new Vector3(side * .17f, .08f * relativeScale, .055f) - shoulder;
        if (Math.Min(upperLength, lowerLength) < .01f || reach.LengthSquared() < .0001f) return;

        // Keep the elbows out to the sides while the hands follow the passenger's waist.
        Vector3 direction = reach.Normalized();
        Vector3 elbowSide = new(side, -.85f, .06f);
        elbowSide = (elbowSide - direction * elbowSide.Dot(direction)).Normalized();
        float length = Mathf.Clamp(reach.Length(), Math.Abs(upperLength - lowerLength) + .005f, upperLength + lowerLength - .005f);
        float along = (upperLength * upperLength - lowerLength * lowerLength + length * length) / (2 * length);
        Vector3 elbow = shoulder + direction * along + elbowSide * MathF.Sqrt(Math.Max(0, upperLength * upperLength - along * along));
        Vector3 grip = shoulder + direction * length;
        var upperTurn = new Basis(new Quaternion(upperBone.Normalized(), (elbow - shoulder).Normalized()).Normalized());
        var lowerTurn = new Basis(new Quaternion(lowerBone.Normalized(), (grip - elbow).Normalized()).Normalized());
        upper.Basis = upperTurn * upper.Basis;
        lower.Transform = new Transform3D(lowerTurn * lower.Basis, elbow);
        palm.Transform = new Transform3D(lowerTurn * palm.Basis, grip);
    }

    public Vector3 CoursePosition(BogCircuit track) => Flight?.Position ?? (FreeDriving ? FreePosition : track.Surface(Distance, Lane));
    public Vector3 Heading(BogCircuit track) => Flight?.Heading ?? (FreeDriving ? Vector3.Forward.Rotated(Vector3.Up, FreeYaw) : track.Forward(Distance));
    public Vector3 Right(BogCircuit track) => Heading(track).Cross(Vector3.Up);

    // The paired front claws on each of the native Gromnie's two feet.
    internal static Vector3 ClawPosition(AcActor actor, int foot)
    {
        int first = foot == 0 ? 11 : 17;
        var pad = new Vector3(0, -.065f, -.10f);
        return (actor.Part(first).GlobalTransform * pad + actor.Part(first + 1).GlobalTransform * pad) * .5f;
    }

    internal Vector3 FootPosition(int foot)
    {
        if (Rig.Kind == RaceMountKind.Gromnie) return ClawPosition(Mount, foot);
        var actor = Mount;
        if (foot >= footContacts.Length && CompanionSkate is { } companion)
        {
            actor = companion;
            foot -= footContacts.Length;
        }
        var contact = footContacts[foot];
        return actor.Part(contact.Part).GlobalTransform * contact.Offset;
    }

    public void Eject(BogCircuit track, Vector3 impactDirection, Transform3D? airPose = null, Vector3? airHeading = null)
    {
        SuperDriftUntil = 0;
        GroundDrifting = false;
        seatedTransform = Rider.Transform;
        Vector3 forward = airHeading ?? Heading(track);
        Vector3 velocity = impactDirection * 5.8f + forward * (3.2f + Speed * .22f) + Vector3.Up * 5.9f;
        Vector3 axis = (forward * .9f + impactDirection.Cross(Vector3.Up) * .58f + Vector3.Up * .24f).Normalized();
        int side = impactDirection.Dot(Right(track)) < 0 ? -1 : 1;
        EjectMotion = new(airPose ?? Rider.GlobalTransform, velocity, axis, side);
        Rider.TopLevel = true;
        Rider.GlobalTransform = EjectMotion.Launch;
        EjectTime = EjectDuration;
        SwingTime = 0;
    }

    public void Tumble(BogCircuit track)
    {
        Rider.GlobalTransform = EjectMotion.PoseAt(track, EjectDuration - EjectTime);
        Rider.GlobalPosition += Vector3.Up * RoadMeshLift(track, riderRoadContacts);
    }

    private static RoadContact[] RoadContacts(IEnumerable<MeshInstance3D> meshes)
    {
        var contacts = new List<RoadContact>();
        foreach (var instance in meshes)
            if (instance.Mesh is { } geometry) contacts.Add(new(instance, geometry, geometry.GetAabb()));
        return contacts.ToArray();
    }

    internal static float RoadMeshLift(BogCircuit track, IEnumerable<MeshInstance3D> meshes) =>
        RoadMeshLift(track, RoadContacts(meshes));

    private static float RoadMeshLift(BogCircuit track, ReadOnlySpan<RoadContact> contacts)
    {
        if (!track.HasGradedRoad) return 0;
        const float clearance = .025f;
        float lift = 0;
        foreach (var (mesh, geometry, localBounds) in contacts)
        {
            if (!mesh.IsVisibleInTree()) continue;
            Transform3D pose = mesh.GlobalTransform;
            Aabb bounds = pose * localBounds;
            float highest = track.HighestRoadIn(bounds) + clearance + .24f;
            if (bounds.Position.Y + lift >= highest) continue;
            // The box culls high parts. Use the animated geometry itself to find contact.
            var vertices = RoadContactVertices.GetValue(geometry, static shape =>
                Enumerable.Range(0, shape.GetSurfaceCount())
                    .SelectMany(surface => shape.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    .Distinct().ToArray());
            foreach (Vector3 vertex in vertices)
            {
                Vector3 point = pose * vertex;
                if (point.Y + lift >= highest) continue;
                lift = Math.Max(lift, track.RoadHeight(point) + clearance - point.Y);
            }
        }
        return lift;
    }

    internal void ShowReplicaEjection(Transform3D? pose)
    {
        if (pose is not { } detached)
        {
            Reattach();
            return;
        }
        if (!Rider.TopLevel)
        {
            seatedTransform = Rider.Transform;
            Rider.TopLevel = true;
        }
        EjectTime = .1f;
        Rider.GlobalTransform = detached;
    }

    internal readonly record struct Ejection(Transform3D Launch, Vector3 Velocity, Vector3 Axis, int Side)
    {
        public Transform3D PoseAt(BogCircuit track, float age)
        {
            Vector3 point = Launch.Origin + Velocity * age + Vector3.Down * (8.1f * age * age);
            point.Y = Math.Max(point.Y, track.GroundOrRoadHeight(point) + .3f);
            return new Transform3D(new Basis(Axis, age * 9.2f * Side) * Launch.Basis, point);
        }
    }

    public void Reattach()
    {
        if (!Rider.TopLevel) return;
        Rider.TopLevel = false;
        Rider.Transform = seatedTransform;
        EjectTime = 0;
    }
    public void Draw(BogCircuit track, float dt, bool moving, float steering = 0)
    {
        if (Flight is { } flight)
        {
            roadPoseLift = 0;
            Rider.SetShieldVisible(GuardWindow > 0 || GuardFlash > 0);
            Root.Position = flight.Position;
            Root.Rotation = new Vector3(flight.Pitch, BogCircuit.Yaw(flight.Heading), flight.Bank + flight.RollAngle);
            flight.Flyer.Draw(dt, flight.Age, flight.Bank);
            if (flight.Flyer.SeatsRider) return;
            Rider.TickCurrentMotion(dt);
            // The native falling motion supplies the pose. Its hands follow the talon grip.
            Vector3 hands = (Rider.BodyPartTransform(12).Origin + Rider.BodyPartTransform(16).Origin) * .5f;
            Rider.Position = -(Rider.Basis * hands);
            return;
        }
        Vector3 forward = Heading(track);
        Vector3 right = forward.Cross(Vector3.Up);
        Vector3 point = CoursePosition(track);
        Root.Position = point + Vector3.Up * (.028f + LeapHeight);
        float grade, crossSlope;
        if (Falling)
        {
            grade = -Math.Min(.95f, FallTime * 1.28f);
            crossSlope = Mathf.Clamp(Lane * FallTime * .075f, -.5f, .5f);
        }
        else if (FreeDriving)
        {
            grade = (track.FreeGround(point + forward).Y - track.FreeGround(point - forward).Y) * .5f;
            crossSlope = (track.FreeGround(point + right).Y - track.FreeGround(point - right).Y) * .5f;
        }
        else
        {
            grade = (track.Surface(Distance + 1.2f, Lane).Y - track.Surface(Distance - 1.2f, Lane).Y) / 2.4f;
            crossSlope = track.IsVoid ? track.Right(Distance).Y
                : (track.Surface(Distance, Lane + .75f).Y - track.Surface(Distance, Lane - .75f).Y) / 1.5f;
        }
        float spin = SpinTime > 0 ? Mathf.Tau * (1 - SpinTime / SpinDuration) : 0;
        Root.Rotation = new(Mathf.Clamp(MathF.Atan(grade), -.65f, .65f), BogCircuit.Yaw(forward) + spin,
            Mathf.Clamp(MathF.Atan(crossSlope), -.48f, .48f));
        if (CompanionSkate is { } companion)
        {
            UpdateSaddle(Lean);
            DrawSkates(companion, dt, moving, steering);
        }
        else
        {
            if (Rig.Platform == MountPlatform.None)
            {
                Mount.Tick(dt * (moving ? Rig.GaitRate * Mathf.Clamp(Speed / 15, .55f, 1.65f) : 1), moving, Vector3.Zero);
                Mount.Rotation = new(0, -steering * .29f, -Lean * .21f);
            }
            else DrawPlatform(dt, steering);
            UpdateSaddle(Lean);
        }
        if (!track.HasGradedRoad || FreeDriving)
        {
            roadPoseLift = 0;
            return;
        }
        float lift = PowerForm is { } form ? RoadMeshLift(track, form.BodyMeshes) : RoadMeshLift(track, mountRoadContacts);
        if (PowerForm is null)
            lift = Math.Max(lift, RoadMeshLift(track, companionRoadContacts));
        if (!Rider.TopLevel) lift = Math.Max(lift, RoadMeshLift(track, riderRoadContacts));
        roadPoseLift = Math.Max(lift, dt == 0 ? 0 : roadPoseLift - dt * 5.5f);
        Root.Position += Vector3.Up * roadPoseLift;
    }

    public void DrawGarage(float dt, float yaw)
    {
        Root.Position = Vector3.Zero;
        Root.Rotation = new(0, yaw, 0);
        if (CompanionSkate is { } companion)
        {
            UpdateSaddle(0);
            DrawSkates(companion, dt, false, 0);
            return;
        }
        if (Rig.Platform == MountPlatform.None)
        {
            Mount.Rotation = Vector3.Zero;
            Mount.Tick(dt, false, Vector3.Zero);
        }
        else DrawPlatform(dt, 0);
        UpdateSaddle(0);
    }
}

public enum RaceItem
{
    None, Pepper, MudPie, Burp, Frostbolt, TuskerPus, PortalWard, WhiteRabbit, Gromnatross, OlthoiLift,
    FrostVolley, PyrealSling, PyrealCache, SmithHammer, LeapingDraught, ZefirHex, TuskerMight, Invulnerability, ForceBurst, WispThief,
    UlgrimFolly
}






