using Godot;
using DerethSurvivors;

public sealed partial class MountedRacer
{
    private Node3D? snowboard;
    private CpuParticles3D? snowSpray, snowboardTouchdownSpray;
    private Transform3D[]? snowboardStance;
    private float snowboardFooting, snowboardCrouch, snowboardBrace, previousSnowboardImpact;
    private const float SnowboardStanceYaw = -1.37f;
    private static readonly Vector3[] BoardSoleSamples =
    [
        new(0, .20f, -1.47f), new(0, .15f, 1.39f),
        new(-.24f, .07f, -.96f), new(.24f, .07f, -.96f),
        new(-.24f, .04f, 0), new(.24f, .04f, 0),
        new(-.22f, .06f, .96f), new(.22f, .06f, .96f)
    ];
    internal bool UsesSnowboard => snowboard is not null;

    internal void EnableSnowboard(AcAssets assets, int color)
    {
        var deck = new Node3D { Name = "Snowboard" };
        Root.AddChild(deck);
        snowboard = deck;
        var material = SkiArmor(assets, SilverScopeArmor, new[] { "a33531", "347d95", "d5a34b", "438568", "68538f" }[color % 5]);
        BogCircuit.MeshAt(deck, BuildBoardDeck(), Vector3.Zero, Vector3.One, material);
        var strap = SkiGrip(assets);
        foreach (float stance in new[] { -.48f, .48f })
        {
            var binding = BogCircuit.Box(deck, new Vector3(0, .16f, stance), new Vector3(.46f, .09f, .28f), strap);
            binding.Rotation = Vector3.Up * Math.Sign(stance) * .22f;
        }
        Saddle.Reparent(deck, false);
        SetSnowboardVisible(true);
        PoseSnowboardRider();
        if (DisplayServer.GetName() == "headless") return;

        var sprayMesh = new QuadMesh { Size = new Vector2(.27f, .30f) };
        var sprayMaterial = CreateRegattaSprayMaterial(assets.Texture(RegattaSpraySprite));
        snowSpray = new CpuParticles3D
        {
            Name = "DAT powder spray", Emitting = false, LocalCoords = false, FixedFps = 30,
            Position = new(0, .07f, .87f), Amount = 28, Lifetime = .48f, LifetimeRandomness = .35f,
            Direction = new(0, .68f, 1), Spread = 46, Gravity = Vector3.Down * 9.80665f,
            InitialVelocityMin = 2.1f, InitialVelocityMax = 5.5f, ScaleAmountMin = .38f, ScaleAmountMax = 1.24f,
            Mesh = sprayMesh, MaterialOverride = sprayMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        deck.AddChild(snowSpray);
        snowboardTouchdownSpray = new CpuParticles3D
        {
            Name = "Touchdown powder", Emitting = false, OneShot = true, Explosiveness = 1,
            LocalCoords = false, FixedFps = 30, Position = new(0, .06f, .33f), Amount = 22,
            Lifetime = .42f, LifetimeRandomness = .35f, Direction = new(0, .65f, 1), Spread = 77,
            Gravity = Vector3.Down * 9.80665f, InitialVelocityMin = 1.7f, InitialVelocityMax = 5.2f,
            ScaleAmountMin = .36f, ScaleAmountMax = 1.15f, Mesh = sprayMesh, MaterialOverride = sprayMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        deck.AddChild(snowboardTouchdownSpray);
    }

    private bool SetSnowboardVisible(bool visible)
    {
        if (snowboard is null) return false;
        Mount.Hide();
        CompanionSkate?.Hide();
        Saddle.Show();
        snowboard.Visible = visible;
        if (!visible)
        {
            if (snowSpray is not null) snowSpray.Emitting = false;
            if (snowboardTouchdownSpray is not null) snowboardTouchdownSpray.Emitting = false;
        }
        return true;
    }

    private void PoseSnowboardRider()
    {
        Rider.HoldFinalPose = false;
        Rider.Play(0x41000003, false);
        Rider.TickCurrentMotion(0);
        SplayLeg(1, [1, 2, 3, 4, 25], -1, .41f, -.09f);
        SplayLeg(5, [5, 6, 7, 8, 26], 1, .41f, .09f);
        Rider.Position = Vector3.Zero;
        Rider.Scale = Vector3.One * (flightRiderScale * 1.04f);
        Rider.Rotation = Vector3.Up * SnowboardStanceYaw;
        snowboardFooting = Math.Min(BootSole(3, 4).Y, BootSole(7, 8).Y) * Rider.Scale.Y;
        snowboardStance ??= new Transform3D[Rider.Setup.Parts.Length];
        for (int part = 0; part < snowboardStance.Length; part++)
            snowboardStance[part] = Rider.Part(part).Transform;
        Rider.RefreshBodyPoseCache();
    }

    private bool DrawSnowboard(BogCircuit track, float dt, bool moving, float steering)
    {
        if (snowboard is not { } board) return false;
        bool suspended = SnowboardAirborne || LeapHeight >= .10f || Falling;
        bool boardOnSnow = board.Visible && !suspended && EjectTime <= 0;
        bool onPowder = track.IsSnowboardCourse || track.NativeSnow(CoursePosition(track));
        board.Position = Vector3.Zero;
        // The solver supplies the real board orientation. Idle poses still
        // follow the native slope supplied by the ordinary race drawing path.
        board.Rotation = !HasSnowboardPhysics && !suspended
            ? new Vector3(0, -steering * .14f, Mathf.Clamp(-Lean * .26f, -.16f, .16f))
            : Vector3.Zero;
        if (HasSnowboardPhysics)
            board.GlobalPosition -= Vector3.Up * .071f;
        if (boardOnSnow)
        {
            Transform3D pose = board.GlobalTransform;
            float clearance = 0;
            foreach (Vector3 sole in BoardSoleSamples)
            {
                Vector3 point = pose * sole;
                clearance = Math.Max(clearance, track.FreeGround(point).Y - point.Y + .021f);
            }
            board.GlobalPosition += Vector3.Up * Math.Min(clearance, .58f);
        }

        float impact = SnowboardLandingImpact;
        bool touchdown = impact > 0 && !Mathf.IsEqualApprox(impact, previousSnowboardImpact);
        previousSnowboardImpact = impact;
        if (touchdown) snowboardBrace = Mathf.SmoothStep(.65f, 7.2f, impact);
        float load = Mathf.SmoothStep(2, 24, Speed);
        float crouch = suspended ? .21f + .08f * Math.Abs(steering)
            : .08f + load * (.09f + .08f * Math.Abs(steering)) + snowboardBrace * .24f;
        snowboardCrouch = Mathf.Lerp(snowboardCrouch, crouch, 1 - MathF.Exp(-dt * 17));
        snowboardBrace *= MathF.Exp(-dt * 7.8f);

        Saddle.Position = Vector3.Up * (.20f - snowboardFooting);
        Saddle.Rotation = Vector3.Zero;
        seat.Rotation = Vector3.Zero;
        if (!Rider.TopLevel && PowerForm is null)
        {
            if (!Mathf.IsEqualApprox(Rider.Rotation.Y, SnowboardStanceYaw))
                PoseSnowboardRider();
            AnimateSnowboardStance(snowboardCrouch, suspended ? steering * .17f : steering * .11f);
        }
        if (snowSpray is { } powder)
        {
            powder.Emitting = boardOnSnow && onPowder && moving && Speed > 5;
            float edge = Mathf.Clamp(Lean + steering * .6f, -1, 1);
            powder.Direction = new Vector3(edge * 1.45f, GroundDrifting ? 1 : .38f, 1).Normalized();
            powder.InitialVelocityMax = Mathf.Lerp(3.7f, 8, load) + (GroundDrifting ? 1.8f : 0);
        }
        if (onPowder && board.Visible && touchdown && impact > 1.2f && snowboardTouchdownSpray is { } burst)
        {
            burst.InitialVelocityMax = Mathf.Lerp(3.8f, 8.5f, Mathf.SmoothStep(1, 8.5f, impact));
            burst.Restart();
            burst.Emitting = true;
        }
        return true;
    }

    // The rider lowers their weight without moving either boot off the bindings.
    // Native poses are already in setup space, so solve the two legs separately.
    private void AnimateSnowboardStance(float crouch, float lean)
    {
        if (snowboardStance is not { Length: >= 17 } rest) return;
        Vector3 hips = (rest[1].Origin + rest[5].Origin) * .5f;
        Vector3 down = Vector3.Down * crouch;
        var stance = new Basis(Vector3.Up, -SnowboardStanceYaw);
        // Keep the upper body over the board while the deck follows the slope.
        var balance = new Basis(stance * Vector3.Right, -.34f * SnowboardPitch) *
            new Basis(stance * Vector3.Back, -.30f * SnowboardRoll);
        var torso = balance * new Basis(Vector3.Forward, lean) * new Basis(Vector3.Right, crouch * .54f);
        for (int part = 0; part < rest.Length; part++)
        {
            Transform3D pose = rest[part];
            if (part is >= 1 and <= 8 or 25 or 26)
            {
                Rider.Part(part).Transform = pose;
                continue;
            }
            Rider.Part(part).Transform = new Transform3D(torso * pose.Basis, hips + down + torso * (pose.Origin - hips));
        }
        BendLeg(1);
        BendLeg(5);

        void BendLeg(int hip)
        {
            Vector3 oldHip = rest[hip].Origin;
            Vector3 oldKnee = rest[hip + 1].Origin;
            Vector3 foot = rest[hip + 2].Origin;
            Vector3 thigh = oldKnee - oldHip, shin = foot - oldKnee;
            float upper = thigh.Length(), lower = shin.Length();
            Vector3 newHip = oldHip + down;
            Vector3 hipToFoot = foot - newHip;
            if (upper < .02f || lower < .02f || hipToFoot.LengthSquared() < .0025f) return;
            Vector3 axis = hipToFoot.Normalized();
            float distance = Mathf.Clamp(hipToFoot.Length(), Math.Abs(upper - lower) + .005f, upper + lower - .005f);
            Vector3 front = thigh - axis * thigh.Dot(axis);
            if (front.LengthSquared() < .001f) front = Vector3.Forward - axis * Vector3.Forward.Dot(axis);
            float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            Vector3 knee = newHip + axis * along + front.Normalized() * MathF.Sqrt(Math.Max(0, upper * upper - along * along));
            var thighTurn = new Basis(new Quaternion(thigh.Normalized(), (knee - newHip).Normalized()));
            var shinTurn = new Basis(new Quaternion(shin.Normalized(), (foot - knee).Normalized()));
            Rider.Part(hip).Transform = new Transform3D(thighTurn * rest[hip].Basis, newHip);
            Rider.Part(hip + 1).Transform = new Transform3D(shinTurn * rest[hip + 1].Basis, knee);
        }
    }

    private static ArrayMesh BuildBoardDeck()
    {
        (float Z, float HalfWidth, float Lift)[] shape =
            [(-1.60f,.05f,.17f),(-1.42f,.25f,.09f),(-1.11f,.30f,.024f),(-.62f,.27f,0),
             (0,.243f,0),(.63f,.27f,0),(1.07f,.29f,.022f),(1.34f,.23f,.08f),(1.50f,.05f,.15f)];
        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        for (int row = 1; row < shape.Length; row++)
        {
            var a = shape[row - 1];
            var b = shape[row];
            Vector3 left = new(-a.HalfWidth, a.Lift + .11f, a.Z), right = new(a.HalfWidth, a.Lift + .11f, a.Z);
            Vector3 nextLeft = new(-b.HalfWidth, b.Lift + .11f, b.Z), nextRight = new(b.HalfWidth, b.Lift + .11f, b.Z);
            Quad(left, right, nextLeft, nextRight);
            Vector3 thickness = Vector3.Down * .069f;
            Quad(right + thickness, left + thickness, nextRight + thickness, nextLeft + thickness);
            Quad(left + thickness, left, nextLeft + thickness, nextLeft);
            Quad(right, right + thickness, nextRight, nextRight + thickness);
        }
        mesh.GenerateNormals();
        return mesh.Commit();

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            foreach (var point in new[] { a, b, c, b, d, c })
            {
                mesh.SetUV(new Vector2(.12f + (point.X + .30f) * .34f, .07f + (point.Z + 1.60f) * .09f));
                mesh.AddVertex(point);
            }
        }
    }
}
