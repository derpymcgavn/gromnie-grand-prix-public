using Godot;

public partial class GrandPrix
{
    // Casting Stein from Ulgrim's Home Brew quest, weenie 23774.
    private const uint CastingSteinSetup = 0x02000ec9;
    internal const uint CastingSteinIcon = 0x06001e99;
    private const float SudsDuration = 10, PourWindup = .24f, SplashRange = 42;
    private static readonly Color LagerTint = new("e8ab3e"), FoamTint = new("fff1cd");

    private readonly List<SteinPour> steinPours = [];
    private readonly Dictionary<MountedRacer, BeerSuds> beerSuds = [];
    private readonly SphereMesh bubbleMesh = new() { Radius = 1, Height = 2, RadialSegments = 9, Rings = 4 };
    private readonly StandardMaterial3D foamMaterial = new() { AlbedoColor = FoamTint, Roughness = .92f };
    private readonly StandardMaterial3D lagerMaterial = new() { AlbedoColor = new("e5a03e"), Roughness = .44f };
    private readonly StandardMaterial3D bubbleMaterial = new()
    {
        AlbedoColor = new Color(.83f, .95f, 1f, .62f), Roughness = .2f,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha
    };

    private sealed record SteinPour(MountedRacer Owner, MountedRacer Target, Node3D Stein,
        Node3D Stream, MeshInstance3D[] Drops, Vector3 Origin, float Travel)
    {
        public float Age;
        public Vector3 Origin { get; set; } = Origin;
        public Node3D SplashHead => Drops[0];
    }

    private sealed record BeerSuds(Node3D Root, MeshInstance3D[] Foam, MeshInstance3D[] Bubbles)
    {
        public float Age;
    }

    private bool IsSudsed(MountedRacer racer) => racer.SudsedUntil > elapsed;

    private (bool Gas, bool Brake, float Steer) DrivingInputs(MountedRacer racer, bool gas, bool brake, float steer) =>
        IsSudsed(racer) ? (brake, gas, -steer) : (gas, brake, steer);

    private bool PourUlgrimFolly(MountedRacer owner)
    {
        Vector3 origin = SteinPosition(owner);
        var target = racers.Where(rival => rival != owner && CanFrostboltHit(rival) && !rival.DodgesItems
                && RiderTarget(rival).DistanceSquaredTo(origin) <= SplashRange * SplashRange)
            .MinBy(rival => RiderTarget(rival).DistanceSquaredTo(origin));
        if (target is null)
        {
            if (owner == Player) Callout("NO RIVAL WITHIN SPLASH RANGE");
            return false;
        }

        var stein = assets.CreateActor(CastingSteinSetup, 0, 1.6f);
        stein.Position = origin;
        itemFxRoot.AddChild(stein);
        var stream = new Node3D { Name = "Ulgrim's home brew" };
        itemFxRoot.AddChild(stream);
        var drops = Enumerable.Range(0, 11).Select(i => Bubble(stream, i % 4 == 3 ? foamMaterial : lagerMaterial)).ToArray();
        foreach (var drop in drops)
        {
            drop.Position = origin;
            drop.Hide();
        }
        float distance = origin.DistanceTo(RiderTarget(target));
        steinPours.Add(new(owner, target, stein, stream, drops, origin, Mathf.Clamp(distance / 36f, .32f, .98f)));
        if (owner == Player) Callout("ULGRIM'S FOLLY!");
        return true;
    }

    private Vector3 SteinPosition(MountedRacer racer) =>
        RiderTarget(racer) + racer.Right(track) * .60f + Vector3.Up * .40f;

    private MeshInstance3D Bubble(Node3D root, StandardMaterial3D material)
    {
        var mesh = BogCircuit.MeshAt(root, bubbleMesh, Vector3.Zero, Vector3.One, material);
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return mesh;
    }

    private void TickUlgrimEffects(float dt)
    {
        for (int i = steinPours.Count - 1; i >= 0; i--)
        {
            var pour = steinPours[i];
            pour.Age += dt;
            Vector3 endpoint = RiderTarget(pour.Target);
            pour.Stein.Position = SteinPosition(pour.Owner);
            Vector3 aim = endpoint - pour.Stein.Position;
            aim.Y = 0;
            Vector3 direction = aim.Normalized();
            if (pour.Age - dt < PourWindup)
            {
                pour.Origin = pour.Stein.Position + direction * .30f;
                pour.SplashHead.Position = pour.Origin;
            }
            if (aim.LengthSquared() > .001f)
                pour.Stein.Rotation = new(-1.52f * Mathf.SmoothStep(0, PourWindup + .10f, pour.Age), BogCircuit.Yaw(aim), -.20f);
            pour.Stein.Visible = pour.Age < PourWindup + .28f;
            for (int dropIndex = 0; dropIndex < pour.Drops.Length; dropIndex++)
            {
                var drop = pour.Drops[dropIndex];
                float t = (pour.Age - PourWindup - dropIndex * .013f) / pour.Travel;
                drop.Visible = t is >= 0 and <= 1;
                if (!drop.Visible) continue;
                float arc = Mathf.Sin(t * Mathf.Pi);
                float spray = Mathf.Sin(dropIndex * 2.12f) * arc * .45f;
                Vector3 spill = direction.Cross(Vector3.Up) * spray;
                drop.Position = pour.Origin.Lerp(endpoint, t) + Vector3.Up * (arc * (1.12f + dropIndex * .045f)) + spill;
                drop.Scale = Vector3.One * (dropIndex == 0 ? .24f : .095f + (dropIndex % 3) * .028f);
            }
            if (pour.Age < PourWindup + pour.Travel) continue;
            Wave(endpoint, LagerTint, 2.2f, .42f, new(.64f, 0, .23f));
            if (AcceptHit(pour.Target))
            {
                CoatInSuds(pour.Target);
                if (pour.Target == Player) Callout("SUDSED! CONTROLS FLIPPED", 1.7f);
                else if (pour.Owner == Player) Callout(pour.Target.Name.ToUpperInvariant() + " GOT SUDSED!", 1.4f);
            }
            RemoveSteinPour(i);
        }

        foreach (var (racer, suds) in beerSuds)
        {
            if (!IsSudsed(racer) || racer.Finish >= 0)
            {
                suds.Root.Hide();
                suds.Root.QueueFree();
                beerSuds.Remove(racer);
                racer.SudsedUntil = 0;
                continue;
            }
            suds.Age += dt;
            AnimateSuds(racer, suds);
        }
    }

    private void CoatInSuds(MountedRacer target)
    {
        target.SudsedUntil = elapsed + SudsDuration;
        if (!beerSuds.TryGetValue(target, out var suds))
        {
            var root = new Node3D { Name = "Beer suds", TopLevel = true };
            target.Root.AddChild(root);
            var foam = Enumerable.Range(0, 15).Select(_ => Bubble(root, foamMaterial)).ToArray();
            var bubbles = Enumerable.Range(0, 9).Select(_ => Bubble(root, bubbleMaterial)).ToArray();
            beerSuds[target] = suds = new(root, foam, bubbles);
        }
        suds.Age = 0;
        AnimateSuds(target, suds);
    }

    private void AnimateSuds(MountedRacer racer, BeerSuds suds)
    {
        bool transformed = racer.PowerForm is not null;
        var pose = transformed ? racer.Root.GlobalTransform : racer.Rider.GlobalTransform;
        Vector3 basePoint = transformed ? Vector3.Up * .24f : racer.Rider.BodyPartTransform(0).Origin;
        suds.Root.GlobalTransform = pose * new Transform3D(Basis.Identity, basePoint);
        float fade = Mathf.Clamp((racer.SudsedUntil - elapsed) / .35f, .04f, 1);
        float splash = Mathf.Sin(Mathf.Clamp(suds.Age / .40f, 0, 1) * Mathf.Pi);
        for (int i = 0; i < suds.Foam.Length; i++)
        {
            int row = i / 5;
            float angle = i * 2.39996f;
            float height = .15f + row * .28f;
            float radius = (row == 2 ? .18f : .28f) + splash * .48f;
            suds.Foam[i].Position = new Vector3(Mathf.Cos(angle) * radius, height + splash * .34f, Mathf.Sin(angle) * radius * .77f);
            suds.Foam[i].Scale = Vector3.One * ((.12f + i % 4 * .019f) * fade);
        }
        for (int i = 0; i < suds.Bubbles.Length; i++)
        {
            float rise = Mathf.PosMod(suds.Age * (.62f + i % 3 * .16f) + i * .39f, 1);
            float angle = i * 2.39996f + rise * 1.3f;
            float radius = .32f + rise * .56f + splash * .6f;
            suds.Bubbles[i].Position = new Vector3(Mathf.Cos(angle) * radius, .28f + rise * 1.36f, Mathf.Sin(angle) * radius);
            suds.Bubbles[i].Scale = Vector3.One * ((.075f + i % 3 * .035f) * Mathf.Sin(Mathf.Pi * rise) * fade);
        }
    }

    private void RemoveSteinPour(int index)
    {
        var pour = steinPours[index];
        pour.Stein.QueueFree();
        pour.Stream.QueueFree();
        steinPours.RemoveAt(index);
    }

    private void ClearUlgrimEffects()
    {
        for (int i = steinPours.Count - 1; i >= 0; i--) RemoveSteinPour(i);
        foreach (var (racer, suds) in beerSuds)
        {
            suds.Root.QueueFree();
            racer.SudsedUntil = 0;
        }
        beerSuds.Clear();
    }
}
