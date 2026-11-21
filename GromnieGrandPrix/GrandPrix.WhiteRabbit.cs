using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    // Orb of the Bunny Booty and original White Rabbit weenie visual resources.
    private const uint BunnyOrbSetup = 0x02000986, WhiteRabbitSetup = 0x0200047b;
    private const uint WhiteRabbitMotion = 0x09000062, WhiteRabbitIcon = 0x060016bc;
    private const float RabbitSummonTime = .42f, RabbitBiteTime = .25f, RabbitChaseTime = 24f;
    private const float RabbitClearance = .18f;
    private static readonly Color BunnyGold = new("ffce6c"), BunnyPink = new("ffb3db");
    private static readonly RareCatalog.Creature RabbitWhiteFur = new()
    {
        Id = 2568, Name = "White Rabbit", Palette = 0x040001b4, Clothing = 0x1000010d, PaletteTemplate = 61
    };
    private readonly List<RabbitChase> rabbits = [];
    private uint bunnySmiteEffect;

    private sealed class RabbitChase(MountedRacer owner, MountedRacer target, Node3D root, AcActor orb, AcActor rabbit,
        float targetStationOffset)
    {
        public readonly MountedRacer Owner = owner;
        public readonly Node3D Root = root;
        public readonly AcActor Orb = orb, Rabbit = rabbit;
        public MountedRacer Target = target;
        public float TargetStationOffset = targetStationOffset;
        public float Time, Distance = owner.Distance, Lane = owner.Lane, Bite, Trail;
        public Vector3 BiteHeading;
        public bool OffRoad, SearchedRoadAccess, Smote;
    }

    private MountedRacer? WhiteRabbitTarget(MountedRacer owner)
    {
        var order = racers.Where(r => r.Finish < 0).OrderByDescending(RaceProgress).ToArray();
        int index = Array.IndexOf(order, owner);
        if (index < 0 || order.Length < 2) return null;
        return order[index == 0 ? 1 : index - 1];
    }

    private void SummonWhiteRabbit(MountedRacer owner)
    {
        var target = WhiteRabbitTarget(owner);
        if (target is null)
        {
            if (owner == Player) Callout("NO RIVAL TO TARGET");
            return;
        }

        var root = new Node3D { Name = "White Rabbit", Position = owner.CoursePosition(track) };
        itemFxRoot.AddChild(root);
        var orb = assets.CreateActor(BunnyOrbSetup, 0, 1.25f);
        orb.ApplyNativeAppearance(RabbitWhiteFur);
        orb.Position = Vector3.Up * 1.9f;
        root.AddChild(orb);
        var rabbit = assets.CreateActor(WhiteRabbitSetup, WhiteRabbitMotion, 2.05f);
        rabbit.ApplyNativeAppearance(RabbitWhiteFur);
        root.AddChild(rabbit);
        rabbit.Hide();
        var chase = new RabbitChase(owner, target, root, orb, rabbit, RabbitTargetStationOffset(owner, target))
        {
            OffRoad = RabbitOffRoad(owner)
        };
        rabbits.Add(chase);
        if (owner == Player) StartRabbitCamera(chase);
        Wave(root.Position + Vector3.Up * .2f, BunnyPink, 2.5f, .65f);
        Wave(root.Position + Vector3.Up * 1.9f, BunnyGold, 1.6f, .48f, new(.55f, 0, .35f));
        if (target == Player) Callout("INCOMING WHITE RABBIT!", 1.3f);
    }

    private void TickWhiteRabbits(float dt)
    {
        TickRabbitReplay(dt);
        if (rabbitCameraHold > 0)
        {
            rabbitCameraHold = Math.Max(0, rabbitCameraHold - dt);
            if (rabbitCameraHold == 0) UpdateRabbitCameraVisibility();
        }
        for (int i = rabbits.Count - 1; i >= 0; i--)
        {
            var chase = rabbits[i];
            chase.Time += dt;
            bool active = chase.Time < RabbitChaseTime && KeepRabbitTarget(chase) && AdvanceWhiteRabbit(chase, dt);
            if (chase == filmedRabbit)
            {
                FrameRabbit(chase, dt);
                if (!active)
                {
                    filmedRabbit = null;
                    if (chase.Smote) StartRabbitReplay(chase);
                    else rabbitCameraHold = chase.Bite >= RabbitBiteTime ? .35f : 0;
                    UpdateRabbitCameraVisibility();
                }
            }
            if (active) continue;
            chase.Root.QueueFree();
            rabbits.RemoveAt(i);
        }
    }

    private bool KeepRabbitTarget(RabbitChase chase)
    {
        if (chase.Target.Finish < 0) return true;
        if (WhiteRabbitTarget(chase.Owner) is not { } target) return false;
        chase.Target = target;
        chase.TargetStationOffset = RabbitTargetStationOffset(chase.Owner, target);
        chase.Bite = 0;
        if (target == Player) Callout("INCOMING WHITE RABBIT!", 1.3f);
        return true;
    }

    private float RabbitTargetStationOffset(MountedRacer owner, MountedRacer target)
    {
        if (track.IsSprint) return 0;
        float rawGap = target.Distance - owner.Distance;
        float raceGap = RaceProgress(target) - RaceProgress(owner);
        float gap = rawGap + Mathf.Round((raceGap - rawGap) / track.Length) * track.Length;
        if (Math.Abs(gap) >= track.Length) gap = Math.Sign(gap) * Mathf.PosMod(Math.Abs(gap), track.Length);
        return gap - rawGap;
    }

    private bool RabbitOffRoad(MountedRacer racer) => !track.IsVoid && racer.FreeDriving && !racer.Falling && racer.Flight is null;

    private float RabbitLane(float distance, float lane)
    {
        float edge = track.WidthAt(distance) - .57f;
        return Mathf.Clamp(lane, -edge, edge);
    }

    private Vector3 RabbitRoadPoint(float distance, float lane) =>
        track.Surface(distance, RabbitLane(distance, lane)) + Vector3.Up * RabbitClearance;

    private Vector3 RabbitGroundStep(Vector3 from, Vector3 destination, float distance)
    {
        Vector3 next = from.MoveToward(destination with { Y = from.Y }, distance);
        float ground = track.FreeGround(next).Y;
        float deck = track.RoadHeight(next);
        if (deck <= from.Y + 1.25f) ground = Math.Max(ground, deck);
        next.Y = Math.Max(ground + RabbitClearance, from.Y - distance * .9f);
        return next;
    }

    private void FindRabbitRoadAccess(RabbitChase chase, Vector3 from)
    {
        chase.SearchedRoadAccess = true;
        // A rabbit beside a raised deck needs a low shoulder or ramp instead of trying to jump through the road.
        float origin = chase.Distance;
        float side = (from - track.Point(origin)).Dot(track.Right(origin)) < 0 ? -1 : 1;
        float minimum = origin - 240, maximum = origin + 240;
        if (track.IsSprint) { minimum = Math.Max(0, minimum); maximum = Math.Min(track.Length, maximum); }
        float best = float.PositiveInfinity;
        for (float station = minimum; station <= maximum; station += 8)
        {
            float lane = RabbitLane(station, side * track.WidthAt(station));
            Vector3 entry = RabbitRoadPoint(station, lane);
            if (entry.Y - track.FreeGround(entry).Y > 1.2f) continue;
            float distance = from.DistanceSquaredTo(entry);
            if (distance >= best) continue;
            best = distance;
            chase.Distance = station;
            chase.Lane = lane;
        }
    }

    private bool AdvanceWhiteRabbit(RabbitChase chase, float dt)
    {
        if (chase.Time < RabbitSummonTime)
        {
            chase.Root.Position = chase.Owner.CoursePosition(track);
            chase.Distance = chase.Owner.Distance;
            chase.Lane = chase.Owner.Lane;
            chase.OffRoad = RabbitOffRoad(chase.Owner);
            chase.Orb.Rotation = new(.18f, chase.Time * 16, .12f);
            chase.Orb.Position = Vector3.Up * (1.9f + chase.Time * 1.5f);
            return true;
        }
        chase.Orb.Hide();
        chase.Rabbit.Show();

        var target = chase.Target;
        Vector3 wanted = target.CoursePosition(track);
        Vector3 from = chase.Root.Position;
        bool targetOffRoad = RabbitOffRoad(target);
        float goal = (target.Flight?.End ?? target.Distance) + chase.TargetStationOffset;
        float gap = goal - chase.Distance;
        Vector3 flatGap = (wanted - from) with { Y = 0 };
        bool close = (Math.Abs(gap) < 6 || chase.OffRoad && targetOffRoad) &&
            flatGap.LengthSquared() <= 2.25f * 2.25f && Math.Abs(wanted.Y - from.Y) < 2.1f;
        bool groundedTarget = target.Flight is null && !target.Falling && target.EjectTime <= 0 && target.ResetHold <= 0;
        if (close && groundedTarget)
        {
            if (chase.Bite <= 0)
            {
                chase.BiteHeading = flatGap.LengthSquared() > .01f ? flatGap.Normalized() : target.Heading(track);
                chase.Rabbit.Attack();
                Wave(wanted + Vector3.Up * .55f, BunnyPink, 1.25f, .26f);
            }
            chase.Bite += dt;
            Vector3 approach = chase.BiteHeading;
            if (targetOffRoad)
                chase.Root.Position = RabbitGroundStep(from, wanted - approach * .9f, 75 * dt);
            else
            {
                chase.Distance = goal - track.Forward(goal).Dot(approach) * .9f;
                chase.Lane = RabbitLane(chase.Distance, target.Lane - track.Right(goal).Dot(approach) * .9f);
                chase.Root.Position = RabbitRoadPoint(chase.Distance, chase.Lane);
                chase.OffRoad = false;
            }
            chase.Root.Rotation = new(0, BogCircuit.Yaw(approach), 0);
            chase.Rabbit.Tick(dt, false, Vector3.Zero);
            if (chase.Bite < RabbitBiteTime) return true;
            BunnySmite(chase, approach);
            return false;
        }

        chase.Bite = 0;
        float pace = Mathf.Clamp(47 + Math.Abs(target.Speed) + Math.Abs(gap) * .77f, 47, 280);
        Vector3 next;
        if (chase.OffRoad)
        {
            bool followOffRoad = targetOffRoad && (Math.Abs(gap) < 22 || flatGap.LengthSquared() < 18 * 18);
            Vector3 destination = followOffRoad ? wanted : RabbitRoadPoint(chase.Distance, chase.Lane);
            next = RabbitGroundStep(from, destination, Math.Min(pace, 74) * dt);
            if (!followOffRoad)
            {
                if (next.DistanceSquaredTo(destination) < .55f * .55f)
                {
                    chase.OffRoad = false;
                    next = destination;
                }
                else if (!chase.SearchedRoadAccess && ((next - destination) with { Y = 0 }).LengthSquared() < .55f * .55f)
                    FindRabbitRoadAccess(chase, next);
            }
        }
        else
        {
            chase.Distance = Mathf.MoveToward(chase.Distance, goal, pace * dt);
            float lane = target.Flight is null && !target.Falling ? target.Lane : 0;
            chase.Lane = RabbitLane(chase.Distance, Mathf.MoveToward(chase.Lane, lane, 24 * dt));
            next = RabbitRoadPoint(chase.Distance, chase.Lane);
            if (targetOffRoad && Math.Abs(goal - chase.Distance) < 6 &&
                (Math.Abs(wanted.Y - next.Y) < 3.6f || next.Y - track.FreeGround(next).Y < 1.2f))
            {
                chase.OffRoad = true;
                chase.SearchedRoadAccess = false;
            }
        }
        Vector3 direction = new(next.X - from.X, 0, next.Z - from.Z);
        bool moving = direction.LengthSquared() > .001f;
        if (moving)
        {
            float pitch = Mathf.Clamp(Mathf.Atan2(next.Y - from.Y, direction.Length()), -.55f, .55f);
            chase.Root.Rotation = new(pitch, BogCircuit.Yaw(direction), 0);
        }
        chase.Root.Position = next;
        chase.Rabbit.Tick(dt, moving, Vector3.Zero);
        chase.Trail -= dt;
        if (moving && chase.Trail <= 0)
        {
            Wave(next + Vector3.Up * .1f, BunnyPink, .8f, .31f);
            chase.Trail = .13f;
        }
        return true;
    }

    private void BunnySmite(RabbitChase chase, Vector3 impact)
    {
        var target = chase.Target;
        if (!AcceptHit(target, impact: ShieldImpact.Spell)) return;
        Vector3 at = target.CoursePosition(track);
        uint layers = chase == filmedRabbit ? RaceOnlyLayer : 1;
        Wave(at + Vector3.Up * .28f, BunnyGold, 4.9f, .73f, layers: layers);
        Wave(at + Vector3.Up * 1.18f, BunnyPink, 3.9f, .55f, new(.75f, 0, .60f), layers);
        Wave(at + Vector3.Up * 1.50f, Colors.White, 3.05f, .4f, new(-.75f, 0, -.6f), layers);
        if (bunnySmiteEffect != 0) itemEffects?.PlayAt(AcCatalog.Player.Effects, bunnySmiteEffect, RiderTarget(target), layers);
        target.Stability = 0;
        ClearRacerPowers(target);
        target.Eject(track, impact);
        chase.Smote = true;
        target.Speed = target.Boost = 0;
        if (target == Player || chase.Owner == Player) Callout("BUNNY SMITE!", 1.55f);
    }
}
