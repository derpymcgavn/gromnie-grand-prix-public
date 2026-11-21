using Godot;
using DerethSurvivors;

internal sealed class OlthoiSpitter : FlightMount
{
    private const int FrontThorax = 8;
    public override bool SeatsRider => true;
    internal AcActor Body { get; }
    private readonly Vector3 saddleOffset;
    private readonly Basis saddleRest;

    public OlthoiSpitter(AcAssets assets, CharacterCreationCatalog.Gender rig) : base("Olthoi Spitter")
    {
        Body = assets.CreateActor(rig.Setup, rig.Motion, rig.Scale * 2.05f);
        Body.SetCharacterAppearance(new CharacterLookDefinition(CharacterCreationCatalog.OlthoiSpitterHeritageId,
            rig.Id, rig.Setup, rig.Motion, rig.Sound, rig.Scale, rig.BasePalette, rig.Description, []));
        Body.Position = Vector3.Up * .95f;
        Root.AddChild(Body);

        // Set the saddle blanket into the thorax so it stays flush through the wingbeat.
        var thorax = Body.BodyPartTransform(FrontThorax);
        saddleOffset = thorax.AffineInverse() * new Vector3(0, 3.17f, -.66f);
        saddleRest = thorax.Basis.Inverse();
        MountedRacer.BuildSaddle(RiderAnchor, 1);
    }

    protected override void Pose(float dt, float age, float bank)
    {
        // The native Spitter's idle cycle already hovers and flaps all four wings.
        Body.Tick(dt * 1.25f, false, Vector3.Zero);
        var thorax = Body.BodyPartTransform(FrontThorax);
        RiderAnchor.Position = Body.Transform * (thorax * saddleOffset);
        RiderAnchor.Basis = thorax.Basis * saddleRest;
    }
}
