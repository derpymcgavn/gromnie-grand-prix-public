using Godot;
using DerethSurvivors;

public sealed partial class MountedRacer
{
    private const int CarenziBack = 1;
    internal AcActor? CompanionSkate { get; }
    private Vector3 skateDeck, leftBoot, rightBoot;

    private void AddSkateSaddle(AcActor actor)
    {
        var back = actor.BodyPartTransform(CarenziBack);
        var rest = back.Basis.Inverse();
        var saddle = new Node3D
        {
            Name = "Boot pad", Basis = rest,
            Position = rest * new Vector3(0, .097f, .047f)
        };
        actor.Part(CarenziBack).AddChild(saddle);
        BogCircuit.Box(saddle, Vector3.Zero, new Vector3(.148f, .035f, .22f), BogCircuit.Paint("553729"));
        skateDeck = saddle.Position + rest * new Vector3(0, .022f, 0);
    }

    private void PoseSkateRider()
    {
        Rider.HoldFinalPose = false;
        Rider.Play(0x41000003, false);
        Rider.TickCurrentMotion(0);
        SplayLeg(1, [1, 2, 3, 4, 25], -1, Rig.LegSpread, -.12f);
        SplayLeg(5, [5, 6, 7, 8, 26], 1, Rig.LegSpread, .13f);
        Rider.Position = Vector3.Zero;
        Rider.Rotation = Vector3.Zero;
        Rider.RefreshBodyPoseCache();
        leftBoot = BootSole(3, 4);
        rightBoot = BootSole(7, 8);
    }

    private Vector3 BootSole(int heel, int toe)
    {
        var boot = (Rider.Part(heel).Position + Rider.Part(toe).Position) * .5f;
        float bottom = float.PositiveInfinity;
        foreach (int index in new[] { heel, toe })
        {
            var part = Rider.Part(index);
            foreach (var mesh in part.GetChildren().OfType<MeshInstance3D>())
                if (mesh.Mesh is { } geometry)
                    bottom = Math.Min(bottom, (part.Transform * mesh.Transform * geometry.GetAabb()).Position.Y);
        }
        boot.Y = float.IsFinite(bottom) ? bottom : Rider.Part(toe).Position.Y;
        return boot;
    }

    private void DrawSkates(AcActor companion, float dt, bool moving, float steering)
    {
        float gait = moving ? Rig.GaitRate * Mathf.Clamp(Speed / 15, .55f, 1.65f) : 1;
        if (moving && companion.CurrentCommand != 0x44000007) companion.Tick(.13f, true, Vector3.Zero);
        Mount.Tick(dt * gait, moving, Vector3.Zero);
        companion.Tick(dt * gait * .96f, moving, Vector3.Zero);

        // Keep the boots on the two animated backs, while their average height
        // sets the rider's bob. A dismounted rider leaves the skates on the track.
        var stance = seat.Transform * (Rider.TopLevel ? seatedTransform : Rider.Transform);
        Vector3 left = stance * leftBoot, right = stance * rightBoot;
        var leftBasis = new Basis(Vector3.Up, .10f - steering * .23f).Scaled(Vector3.One * Rig.Scale);
        var rightBasis = new Basis(Vector3.Up, -.10f - steering * .23f).Scaled(Vector3.One * Rig.Scale);
        Vector3 firstDeck = leftBasis * (Mount.BodyPartTransform(CarenziBack) * skateDeck);
        Vector3 secondDeck = rightBasis * (companion.BodyPartTransform(CarenziBack) * skateDeck);
        Saddle.Position = Vector3.Up * ((firstDeck.Y + secondDeck.Y - left.Y - right.Y) * .5f + .012f);
        Mount.Transform = new Transform3D(leftBasis, Saddle.Position + left - firstDeck);
        companion.Transform = new Transform3D(rightBasis, Saddle.Position + right - secondDeck);
    }
}
