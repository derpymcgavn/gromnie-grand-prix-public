using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private sealed class ItemShot(RaceItem kind, MountedRacer owner, MountedRacer? target, Node3D node,
        Vector3 launch, Vector3 heading, float duration, float speed)
    {
        public readonly RaceItem Kind = kind;
        public readonly MountedRacer Owner = owner;
        public MountedRacer? Target = target;
        public readonly Node3D Node = node;
        public readonly Vector3 Launch = launch;
        public Vector3 Heading = heading;
        public readonly float Duration = duration, Speed = speed;
        public float Time;
        public FrostboltTrail? IceTrail;
    }

    private sealed class RingEffect(MeshInstance3D mesh, StandardMaterial3D material, Color tint, float duration, float radius)
    {
        public readonly MeshInstance3D Mesh = mesh;
        public readonly StandardMaterial3D Material = material;
        public readonly Color Tint = tint;
        public readonly float Duration = duration, Radius = radius;
        public float Time;
    }

    private readonly List<ItemShot> shots = [];
    private readonly List<FrostboltTrail> frostboltTrails = [];
    private readonly List<RingEffect> rings = [];
    private readonly Dictionary<RaceItem, Texture2D?> itemIcons = [];
    private readonly Dictionary<RaceItem, AcCatalog.Projectile> shotAssets = new()
    {
        [RaceItem.Frostbolt] = AcCatalog.Projectiles["frost"],
        [RaceItem.FrostVolley] = AcCatalog.Projectiles["frost"],
        [RaceItem.TuskerPus] = AcCatalog.Projectiles["acid"]
    };
    private readonly TorusMesh ringMesh = new() { InnerRadius = .82f, OuterRadius = 1, Rings = 20, RingSegments = 6 };
    private Node3D itemFxRoot = null!;
    private AcEffects? itemEffects;
    private float playerTrail;
    private const float FrostboltSpeed = 40, FrostboltClosingSpeed = 26, FrostboltTurnRate = 1.55f, FrostboltHitRadius = 1.3f;
    private static readonly Color IceTint = new("8ceaff"), PusTint = new("c3ec55"), WardTint = new("cca3ff");

    private void ConfigureItemVisuals()
    {
        itemFxRoot = new Node3D { Name = "Race effects" };
        courseItems.AddChild(itemFxRoot);
        itemEffects = new AcEffects(assets, itemFxRoot);
        if (itemIcons.Count == 0)
        {
            var spells = archive!.ReadSpells();
            (RaceItem Item, string Spell)[] titles =
            [
                (RaceItem.Frostbolt, "Frost Bolt I"), (RaceItem.PortalWard, "Armor Self I"),
                (RaceItem.MudPie, "Fester Other I"), (RaceItem.Burp, "Weakness Other I")
            ];
            foreach (var (item, name) in titles)
            {
                uint id = spells.FirstOrDefault(s => s.Name == name)?.Icon ?? 0;
                itemIcons[item] = id == 0 ? null : assets.Texture(id);
            }
            itemIcons[RaceItem.Pepper] = assets.Texture(0x06001b5f);     // Hot Pepper
            itemIcons[RaceItem.TuskerPus] = assets.Texture(0x06005a65);  // Tusker Pus
            itemIcons[RaceItem.WhiteRabbit] = assets.Texture(WhiteRabbitIcon);
            itemIcons[RaceItem.Gromnatross] = assets.Texture(GromnatrossIcon);
            if (creation.OlthoiSpitter is { } spitter)
                itemIcons[RaceItem.OlthoiLift] = assets.Texture(spitter.Icon) ?? itemIcons[RaceItem.Gromnatross];
            foreach (var power in RacePower.All) itemIcons[power.Item] = assets.Texture(power.Icon);
            bunnySmiteEffect = spells.FirstOrDefault(spell => spell.Name == "Bunny Smite")?.TargetEffect ?? 0;
        }
    }

    private Texture2D? ItemIcon(RaceItem item) => itemIcons.GetValueOrDefault(item);

    private static string ItemHint(RaceItem item) => item switch
    {
        RaceItem.Frostbolt => "Homes in and slows a rival",
        RaceItem.TuskerPus => "Recalls the nearest rival",
        RaceItem.PortalWard => "Blocks the next hit",
        RaceItem.Pepper => "A burst of speed",
        RaceItem.MudPie => "Drops a slick behind you",
        RaceItem.Burp => "Slows nearby racers",
        RaceItem.WhiteRabbit => "Bunny Smites the next racer",
        RaceItem.Gromnatross or RaceItem.OlthoiLift => "Take flight. Steer to bank",
        _ => RacePower.Find(item)?.Hint ?? "Ride through a floating icon"
    };

    private void ClearItemVisuals()
    {
        foreach (var hazard in mud) hazard.Node.QueueFree();
        mud.Clear();
        foreach (var shot in shots) shot.Node.QueueFree();
        shots.Clear();
        foreach (var shot in sideShots) shot.Node.QueueFree();
        sideShots.Clear();
        ClearPowerProps();
        ClearUlgrimEffects();
        foreach (var trail in frostboltTrails) trail.Free();
        frostboltTrails.Clear();
        foreach (var chase in rabbits) chase.Root.QueueFree();
        rabbits.Clear();
        ClearRabbitCamera();
        foreach (var ring in rings) ring.Mesh.QueueFree();
        rings.Clear();
        itemEffects?.Clear();
        playerTrail = 0;
    }

    private Vector3 RiderTarget(MountedRacer racer) => racer.CoursePosition(track) + Vector3.Up * (1.45f + racer.LeapHeight);

    private void ThrowItem(MountedRacer racer, RaceItem item)
    {
        Vector3 origin = RiderTarget(racer);
        Vector3 heading = racer.Heading(track);
        Vector3 launch = origin + heading * .85f;
        MountedRacer? target = null;
        if (item == RaceItem.TuskerPus)
        {
            target = racers.Where(other => other != racer && other.Finish < 0 && other.ResetHold <= 0 && other.EjectTime <= 0)
                .MinBy(other => RiderTarget(other).DistanceSquaredTo(origin));
            if (target is null)
            {
                if (racer == Player) Callout("NO RIVAL TO TARGET");
                return;
            }
        }
        else if (item == RaceItem.Frostbolt)
        {
            target = racers.Where(other => other != racer && CanFrostboltHit(other) && !other.DodgesItems)
                .Where(other =>
                {
                    Vector3 delta = RiderTarget(other) - origin;
                    return delta.LengthSquared() <= 40 * 40 && delta.Dot(heading) > .65f
                        && delta.Normalized().Dot(heading) > .82f;
                })
                .MinBy(other => RiderTarget(other).DistanceSquaredTo(origin));
        }

        var profile = shotAssets[item];
        var shotNode = new Node3D { Position = launch };
        itemFxRoot.AddChild(shotNode);
        float distance = target is null ? 36 : launch.DistanceTo(RiderTarget(target));
        float duration = item == RaceItem.TuskerPus ? Mathf.Clamp(distance / 46 + .42f, .66f, 2.15f)
            : target is null ? .95f : 2.2f;
        bool ice = item is RaceItem.Frostbolt or RaceItem.FrostVolley;
        AcActor model = assets.CreateActor(profile.Setup, 0, ice ? .95f : .65f);
        shotNode.AddChild(model);
        var accent = Glow(ice ? IceTint : PusTint);
        var orb = new SphereMesh { Radius = .23f, Height = .46f, RadialSegments = 8, Rings = 4 };
        BogCircuit.MeshAt(shotNode, orb, Vector3.Zero, ice ? new(.7f, .7f, 2.35f) : Vector3.One, accent)
            .CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        shotNode.Basis = Basis.LookingAt(heading);
        float speed = ice ? Math.Max(FrostboltSpeed, racer.Speed + FrostboltClosingSpeed) : 0;
        var shot = new ItemShot(item, racer, target, shotNode, launch, heading, duration, speed);
        if (ice)
        {
            shot.IceTrail = new FrostboltTrail(itemFxRoot, launch);
            frostboltTrails.Add(shot.IceTrail);
        }
        shots.Add(shot);
        itemEffects?.PlayEffect(profile.Effects, 4, shotNode, duration);
        if (target == Player) Callout(item == RaceItem.TuskerPus ? "INCOMING TUSKER PUS!" : "INCOMING FROSTBOLT!", 1.35f);
    }

    private void TickItemEffects(float dt)
    {
        TickSideShots(dt);
        TickWhiteRabbits(dt);
        TickPowerProps(dt);
        TickUlgrimEffects(dt);
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            var shot = shots[i];
            float step = Math.Min(dt, shot.Duration - shot.Time);
            shot.Time += step;
            bool toss = shot.Kind == RaceItem.TuskerPus;
            MountedRacer? hit = toss ? AdvanceTuskerPus(shot) : AdvanceFrostbolt(shot, step);
            shot.IceTrail?.Record(shot.Node.Position);
            if (hit is null && shot.Time < shot.Duration) continue;

            Color tint = toss ? PusTint : IceTint;
            itemEffects?.PlayAt(shotAssets[shot.Kind].Effects, 5, shot.Node.Position);
            Wave(shot.Node.Position, tint, 2.35f, .66f);
            if (hit is not null && AcceptHit(hit, impact: toss ? ShieldImpact.Collision : ShieldImpact.Spell))
            {
                if (toss) RecallRacer(hit, "TUSKER PUS! RECALLED");
                else
                {
                    hit.Frozen = shot.Kind == RaceItem.FrostVolley ? 1.75f : 2.9f;
                    if (shot.Kind == RaceItem.FrostVolley) hit.SpinTime = MountedRacer.SpinDuration;
                    hit.Speed *= .64f;
                    if (hit == Player) Callout("FROSTBITTEN!", 1.4f);
                }
                if (shot.Owner == Player && hit != Player)
                    Callout(toss ? $"{hit.Name.ToUpperInvariant()} RECALLED!" : $"{hit.Name.ToUpperInvariant()} FROSTBITTEN!");
            }
            RemoveItemShot(i);
        }
        for (int i = frostboltTrails.Count - 1; i >= 0; i--)
        {
            var trail = frostboltTrails[i];
            if (trail.Tick(dt)) continue;
            trail.Free();
            frostboltTrails.RemoveAt(i);
        }

        playerTrail -= dt;
        if (playerTrail <= 0)
        {
            float behind = Player.Distance - 1.1f;
            Vector3 at = (Player.FreeDriving ? track.FreeGround(Player.FreePosition - Player.Heading(track) * 1.1f)
                : track.Surface(behind, Player.Lane)) + Vector3.Up * .2f;
            if (Player.Frozen > 0) Wave(at, IceTint, .95f, .48f);
            else if (Player.Boost > 0 || drifting && charge > .25f)
                Wave(at, Player.Boost > 0 ? new("ffd280") : new("69ffca"), 1.14f, .40f);
            playerTrail = .14f;
        }

        for (int i = rings.Count - 1; i >= 0; i--)
        {
            var r = rings[i];
            r.Time += dt;
            float t = r.Time / r.Duration;
            if (t >= 1)
            {
                r.Mesh.QueueFree();
                rings.RemoveAt(i);
                continue;
            }
            r.Mesh.Scale = Vector3.One * Mathf.Lerp(r.Radius * .25f, r.Radius, t);
            r.Material.AlbedoColor = new Color(r.Tint, 1 - t);
        }
        itemEffects?.Tick(dt);
    }

    private MountedRacer? AdvanceTuskerPus(ItemShot shot)
    {
        var target = shot.Target!;
        float t = shot.Time / shot.Duration;
        Vector3 endpoint = RiderTarget(target);
        float arc = Mathf.Clamp(shot.Launch.DistanceTo(endpoint) * .13f, 1.2f, 14);
        Vector3 next = shot.Launch.Lerp(endpoint, t) + Vector3.Up * (Mathf.Sin(t * Mathf.Pi) * arc);
        MoveItemShot(shot, next);
        return t >= 1 ? target : null;
    }

    private MountedRacer? AdvanceFrostbolt(ItemShot shot, float dt)
    {
        Vector3 from = shot.Node.Position;
        float speed = shot.Speed;
        if (shot.Target is { } target)
        {
            Vector3 aim = (RiderTarget(target) - from).Normalized();
            if (!CanFrostboltHit(target) || target.DodgesItems || aim.Dot(shot.Heading) <= 0) shot.Target = null;
            else
            {
                speed = Math.Max(speed, target.Speed + FrostboltClosingSpeed);
                float angle = shot.Heading.AngleTo(aim);
                float turn = FrostboltTurnRate * (speed / FrostboltSpeed) * dt;
                shot.Heading = angle <= turn ? aim : shot.Heading.Slerp(aim, turn / angle).Normalized();
            }
        }

        Vector3 next = from + shot.Heading * (speed * dt);
        float ground = track.FreeGround(next).Y;
        if (shot.Target is null) next.Y = Mathf.MoveToward(next.Y, ground + 1.45f, 10 * dt);
        next.Y = Math.Max(next.Y, ground + .72f);

        MountedRacer? hit = null;
        Vector3 hitAt = next;
        float closest = float.PositiveInfinity;
        foreach (var rival in racers)
        {
            if (rival == shot.Owner || !CanFrostboltHit(rival) || rival.DodgesItems) continue;
            Vector3 center = RiderTarget(rival);
            Vector3 contact = Geometry3D.GetClosestPointToSegment(center, from, next);
            float travel = from.DistanceSquaredTo(contact);
            if (travel >= closest || contact.DistanceSquaredTo(center) > FrostboltHitRadius * FrostboltHitRadius) continue;
            hit = rival;
            hitAt = contact;
            closest = travel;
        }
        MoveItemShot(shot, hitAt);
        return hit;
    }

    private static bool CanFrostboltHit(MountedRacer racer) =>
        racer.Finish < 0 && racer.ResetHold <= 0 && racer.EjectTime <= 0 && racer.Flight is null;

    private void CheckMudHits(MountedRacer racer, Vector3 from, Vector3 to)
    {
        if (racer.Finish >= 0 || racer.DodgesItems || racer.EjectTime > 0 || racer.ResetHold > 0 || racer.Flight is not null) return;
        const float radius = 1.35f;
        for (int i = mud.Count - 1; i >= 0; i--)
        {
            var hazard = mud[i];
            if (racer == hazard.Owner || hazard.Expires <= elapsed) continue;
            Vector3 center = hazard.Node.Position;
            Vector3 contact = Geometry3D.GetClosestPointToSegment(center, from, to);
            if (contact.DistanceSquaredTo(center) > radius * radius) continue;
            if (AcceptHit(racer))
            {
                racer.Slowed = 1.5f;
                if (racer == Player) Callout("MUD PIE!");
            }
            hazard.Node.QueueFree();
            mud.RemoveAt(i);
        }
    }

    private void RemoveItemShot(int index)
    {
        var shot = shots[index];
        shot.Node.QueueFree();
        if (shot.IceTrail is { } trail) trail.Flying = false;
        shots.RemoveAt(index);
    }

    private static void MoveItemShot(ItemShot shot, Vector3 next)
    {
        Vector3 forward = next - shot.Node.Position;
        shot.Node.Position = next;
        if (forward.LengthSquared() > .001f && Math.Abs(forward.Normalized().Y) < .99f)
            shot.Node.Basis = Basis.LookingAt(forward);
    }

    private void RecallRacer(MountedRacer racer, string playerMessage)
    {
        racer.Land();
        racer.Reattach();
        ClearRacerPowers(racer);
        ResetRivalMotion(racer);
        Array.Clear(racer.DustContact);
        racer.FreeDriving = false;
        racer.FallTime = 0;
        racer.Stability = 100;
        racer.SwingTime = 0;
        racer.GuardWindow = racer.GuardFlash = 0;
        racer.Distance = racer.Checkpoint;
        racer.Lane = 0;
        racer.Speed = racer.Slowed = racer.Frozen = racer.Boost = racer.Lean = 0;
        racer.ResetHold = .82f;
        racer.ItemGrace = 1.65f;
        racer.Draw(track, 0, false);
        Wave(racer.Root.Position + Vector3.Up * .65f, WardTint, 2, .95f);
        if (racer == Player)
        {
            lateral = charge = 0;
            drifting = false;
            SetRaceCamera(1);
            Callout(playerMessage, 1.45f);
        }
    }

    private bool AcceptHit(MountedRacer racer, bool allowFlight = false, ShieldImpact impact = ShieldImpact.Collision)
    {
        if (racer.DodgesItems || (racer.Flight is { } ride && (!allowFlight || ride.DodgingRoll)) ||
            racer.Finish >= 0 || racer.ItemGrace > 0 || racer.ResetHold > 0 || racer.EjectTime > 0) return false;
        if (BlockWithShield(racer, impact)) return false;
        if (racer.Ward <= 0) return true;
        racer.Ward = 0;
        Wave(RiderTarget(racer), WardTint, 2.6f, .6f);
        if (racer == Player) Callout("PORTAL WARD BLOCK!");
        return false;
    }

    private void Wave(Vector3 position, Color tint, float radius, float duration, Vector3 tilt = default, uint layers = 1)
    {
        if (OnlineHost && onlineBegun && onlineWaves.Count < 64)
            onlineWaves.Add(new OnlineWave(new(position), tint.ToRgba32(), radius, duration, new(tilt)));
        var material = Glow(tint);
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        var effect = BogCircuit.MeshAt(itemFxRoot, ringMesh, position, Vector3.One * (radius * .25f), material);
        effect.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        effect.Layers = layers;
        effect.Rotation = tilt;
        rings.Add(new(effect, material, tint, duration, radius));
    }

    private static StandardMaterial3D Glow(Color color) => new()
    {
        AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = .9f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled
    };
}


