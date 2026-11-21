using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const float CitadelClimb = 100, AirHitHeight = 2.35f, AirHitRadius = 1.32f;
    private const uint AerbaxSetup = 0x02001749, AerbaxMotion = 0x090001D1;
    private const uint CitadelDeckModel = 0x01003D0A;
    private static readonly Vector3[] CitadelPartyOffsets =
    [
        new(-21, 0, 21), new(-23, 0, -22), new(-44, 0, 2), new(22, 0, 22), new(0, 0, 27)
    ];
    private static readonly (AcCatalog.Projectile Model, Color Tint)[] CitadelMagic =
    [
        (AcCatalog.Projectiles["frost"], new("88dcff")),
        (AcCatalog.Projectiles["fire"], new("ff9a64")),
        (AcCatalog.Projectiles["lightning"], new("c6a9ff"))
    ];
    private readonly SphereMesh citadelCore = new() { Radius = .30f, Height = .60f, RadialSegments = 12, Rings = 6 };
    private CitadelScene? citadel;

    private sealed class CitadelScene(Node3D root, AcActor boss)
    {
        public readonly Node3D Root = root;
        public readonly AcActor Boss = boss;
        public readonly List<AcActor> Party = [];
        public readonly List<CitadelBolt> Bolts = [];
        public readonly HashSet<MountedRacer> WarnedRacers = [];
        public float NextVolley;
        public int Volley;
    }

    private sealed class CitadelBolt(AcActor caster, Vector3 end, float castAt, float travel, int element,
        MountedRacer? strayFor)
    {
        public readonly AcActor Caster = caster;
        public readonly Vector3 End = end;
        public readonly float CastAt = castAt, Travel = travel;
        public readonly int Element = element;
        public readonly MountedRacer? StrayFor = strayFor;
        public Vector3 Start, Before;
        public Node3D? Node;
        public int RollDodgeMask;
    }

    private static void ValidateCitadelFlyover(BogCircuit track, AcLandArena land, LandmarkCircuit.CitadelBattle battle,
        float start, float end)
    {
        Vector3 deck = battle.Platform(track), anchor = battle.FlybyPoint(track);
        if (start <= BogCircuit.Grid + 30 || end < start + 240 || end >= track.Length - 110 ||
            !land.CollisionObjects.Any(prop => prop.Model == CitadelDeckModel && prop.Position.DistanceTo(deck) < 3))
            throw new InvalidDataException($"The native citadel platform or flight sector is missing ({start:F0} to {end:F0} m).");
        for (int lane = -1; lane <= 1; lane++)
        {
            Vector3 previous = SummitPoint(track, start, end, 0, 0, CitadelClimb, lane * 3.1f, anchor);
            for (int sample = 1; sample <= 96; sample++)
            {
                float t = sample / 96f;
                Vector3 point = SummitPoint(track, start, end, t, 0, CitadelClimb, lane * 3.1f, anchor);
                var from = new System.Numerics.Vector3(previous.X, previous.Y + 1.25f, previous.Z);
                var to = new System.Numerics.Vector3(point.X, point.Y + 1.25f, point.Z);
                if (land.Physics.Trace(from, to, 2.1f, includeTerrain: false) is not null)
                    throw new InvalidDataException($"Citadel scenery crosses the flight at {t:P0} ({point.X:F0}, {point.Y:F0}, {point.Z:F0}).");
                previous = point;
            }
        }
        GD.Print($"Citadel flyover: native deck at {deck.Y:F0} m, course launch {start:F0} m, landing {end:F0} m.");
    }

    private void BuildCitadelBattle()
    {
        citadel = null;
        if (!landmarkCircuits.TryGetValue(activeTown, out var circuit) || circuit.Battle is not { } battle) return;
        // The native walkway models have their floor four metres below the placement origin.
        Vector3 floor = battle.Platform(track) - Vector3.Up * 3.91f;
        var root = new Node3D { Name = "Aerbax's mock battle" };
        courseItems.AddChild(root);
        var boss = assets.CreateActor(AerbaxSetup, AerbaxMotion, 1.18f);
        root.AddChild(boss);
        boss.Position = floor + Vector3.Left * 23;
        boss.Face(Vector3.Back);
        boss.OnHook = (hook, _) => raceAudio?.PlayEmote(hook, boss);
        var scene = new CitadelScene(root, boss);
        citadel = scene;
        for (int i = 0; i < CitadelPartyOffsets.Length; i++)
        {
            var look = creation.Resolve(RivalLook(i + 1));
            var mage = assets.CreateActor(look.Setup, look.Motion, look.Scale);
            mage.SetCharacterAppearance(look);
            root.AddChild(mage);
            mage.Position = floor + CitadelPartyOffsets[i];
            mage.Face(FlatDirection(mage.Position, boss.Position));
            mage.OnHook = (hook, _) => raceAudio?.PlayEmote(hook, mage);
            scene.Party.Add(mage);
        }
        root.AddChild(new Label3D
        {
            Text = "AERBAX", Position = boss.Position + Vector3.Up * 6.8f, FontSize = 67, PixelSize = .013f,
            Modulate = new Color("e9bae9"), OutlineSize = 12, OutlineModulate = new Color("291932"),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, VisibilityRangeEnd = 166, VisibilityRangeEndMargin = 18
        });
    }

    private static Vector3 FlatDirection(Vector3 from, Vector3 to) => (to - from) with { Y = 0 };

    private void ResetCitadelBattle()
    {
        if (citadel is not { } scene) return;
        foreach (var bolt in scene.Bolts)
        {
            if (bolt.Node is null) continue;
            scene.Root.RemoveChild(bolt.Node);
            bolt.Node.QueueFree();
        }
        scene.Bolts.Clear();
        scene.WarnedRacers.Clear();
        scene.NextVolley = scene.Volley = 0;
    }

    private void TickCitadelBattle(float dt)
    {
        if (citadel is not { } scene || flyover is not { } route) return;
        Vector3 center = scene.Boss.Position;
        bool visible = Player.CoursePosition(track).DistanceSquaredTo(center) < 250 * 250;
        if (visible)
        {
            scene.Boss.Tick(dt, false, Vector3.Zero);
            foreach (var actor in scene.Party) actor.Tick(dt, false, Vector3.Zero);
            if (elapsed >= scene.NextVolley)
            {
                int order = scene.Volley++;
                scene.NextVolley = elapsed + .58f;
                var member = scene.Party[order % scene.Party.Count];
                var actor = order % 4 == 2 ? scene.Boss : member;
                var target = actor == scene.Boss ? member : scene.Boss;
                if (!actor.ActionBusy)
                {
                    actor.Face(FlatDirection(actor.Position, target.Position));
                    actor.Cast(false, 1.04f);
                    Vector3 end = target.Position + Vector3.Up * (target == scene.Boss ? 2.5f : 1.48f);
                    scene.Bolts.Add(new(actor, end, elapsed + .44f,
                        Mathf.Clamp(actor.Position.DistanceTo(end) / 23, .45f, 2), order % CitadelMagic.Length, null));
                }
            }
        }
        foreach (var racer in racers)
        {
            if (racer.Flight is not { CitadelFlyby: not null } ride || scene.WarnedRacers.Contains(racer)) continue;
            float t = (racer.Distance - ride.Start) / (ride.End - ride.Start);
            if (t is < .435f or > .59f) continue;
            var caster = scene.Party.FirstOrDefault(actor => !actor.ActionBusy);
            if (caster is null) continue;
            const float contactIn = 1.57f, windup = .58f;
            float projected = Mathf.Clamp(t + contactIn * racer.Speed * ride.CourseMetresPerAirMetre / (ride.End - ride.Start), t, .70f);
            Vector3 meet = SummitPoint(track, ride.Start, ride.End, projected, ride.StartLane, route.Height, 0, route.CitadelFlyby)
                + Vector3.Up * AirHitHeight;
            caster.Face(FlatDirection(caster.Position, meet));
            caster.Cast(false, 1.05f);
            Vector3 origin = caster.Position + Vector3.Up * 1.6f;
            int element = racers.IndexOf(racer) % CitadelMagic.Length;
            scene.Bolts.Add(new(caster, origin + (meet - origin) * 1.24f, elapsed + windup, (contactIn - windup) * 1.24f, element, racer));
            scene.WarnedRacers.Add(racer);
            if (visible) Wave(origin, CitadelMagic[element].Tint, 3.2f, windup);
            if (racer == Player) Callout("SPELLFIRE · R TO ROLL!", 1.4f);
        }
        for (int i = scene.Bolts.Count - 1; i >= 0; i--)
        {
            var bolt = scene.Bolts[i];
            if (elapsed < bolt.CastAt) continue;
            var (model, tint) = CitadelMagic[bolt.Element];
            if (bolt.Node is null)
            {
                bolt.Start = bolt.Caster.Position + Vector3.Up * (bolt.Caster == scene.Boss ? 2.55f : 1.6f);
                var node = new Node3D { Position = bolt.Start, Name = "Citadel spell projectile" };
                scene.Root.AddChild(node);
                bolt.Node = node;
                node.LookAt(bolt.End);
                node.AddChild(assets.CreateActor(model.Setup, 0, bolt.StrayFor is null ? .80f : 1.20f));
                BogCircuit.MeshAt(node, citadelCore, Vector3.Zero, new(.85f, .85f, 1.65f), Glow(tint))
                    .CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                if (visible) itemEffects?.PlayEffect(model.Effects, 4, node, bolt.Travel);
            }
            float progress = Mathf.Clamp((elapsed - bolt.CastAt) / bolt.Travel, 0, 1);
            bolt.Before = bolt.Node.Position;
            bolt.Node.Position = bolt.Start.Lerp(bolt.End, progress);
            if (progress >= 1)
            {
                if (visible) itemEffects?.PlayAt(model.Effects, 5, bolt.End);
                bolt.Node.QueueFree();
                scene.Bolts.RemoveAt(i);
            }
        }
    }

    private float CitadelRivalBank(MountedRacer racer, FlightRide flight, int index)
    {
        if (flight.CitadelFlyby is null || citadel is not { } scene || index == 1) return 0;
        var bolt = scene.Bolts.FirstOrDefault(shot => shot.StrayFor == racer);
        if (bolt is null) return 0;
        return racer.Right(track).Dot(bolt.Caster.Position - flight.Position) > 0 ? -1 : 1;
    }

    private bool CheckCitadelSpellHits(MountedRacer racer, FlightRide ride, Vector3 previous)
    {
        if (citadel is not { } scene) return false;
        int racerBit = 1 << racers.IndexOf(racer);
        for (int i = scene.Bolts.Count - 1; i >= 0; i--)
        {
            var bolt = scene.Bolts[i];
            if (bolt.Node is null || bolt.StrayFor is null || (bolt.RollDodgeMask & racerBit) != 0) continue;
            Vector3 offset = previous + Vector3.Up * AirHitHeight - bolt.Before;
            Vector3 end = ride.Position + Vector3.Up * AirHitHeight - bolt.Node.Position;
            Vector3 sweep = end - offset;
            float fraction = Mathf.Clamp(-offset.Dot(sweep) / Math.Max(.00001f, sweep.LengthSquared()), 0, 1);
            if ((offset + sweep * fraction).LengthSquared() > AirHitRadius * AirHitRadius) continue;
            if (ride.DodgingRoll)
            {
                bolt.RollDodgeMask |= racerBit;
                if (racer == Player) Callout("BARREL ROLL DODGE!", 1.1f);
                continue;
            }
            bool ward = racer.Ward > 0, guarding = racer.GuardWindow > 0;
            bool hit = AcceptHit(racer, allowFlight: true, impact: ShieldImpact.Spell);
            bool shielded = (ward && racer.Ward == 0) || (guarding && racer.GuardWindow == 0);
            if (!hit && !shielded) continue;
            itemEffects?.PlayAt(CitadelMagic[bolt.Element].Model.Effects, 5, bolt.Node.Position);
            bolt.Node.QueueFree();
            scene.Bolts.RemoveAt(i);
            if (!hit) continue;
            racer.Draw(track, 0, true);
            Transform3D airPose = racer.Rider.GlobalTransform;
            racer.Land();
            racer.Stability = 0;
            Vector3 push = FlatDirection(bolt.Start, bolt.End).Normalized();
            racer.Eject(track, push, airPose, ride.Heading);
            racer.Speed = racer.Boost = 0;
            if (racer == Player) Callout("KNOCKED OUT OF THE SKY!", 1.5f);
            return true;
        }
        return false;
    }
}
