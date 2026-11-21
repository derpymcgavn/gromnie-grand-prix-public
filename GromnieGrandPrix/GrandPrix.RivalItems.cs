using Godot;

public partial class GrandPrix
{
    private void DecideRivalItem(MountedRacer racer, RivalDriver driver, RivalContact[] contacts, List<RivalObstacle> obstacles)
    {
        if (racer.Item == RaceItem.None || elapsed < racer.UseHeldItemAt) return;
        bool straight = Math.Abs(track.Curvature(racer.Distance + 16)) < .009f && driver.Brake > .9f;
        float held = elapsed - driver.ItemSince;
        float incoming = IncomingRivalThreat(racer);
        bool obstacle = obstacles.Any(row => row.Ahead < 14 && Math.Abs(row.Lane - racer.Lane) < row.Radius + .48f);
        bool following = contacts.Any(row => row.Ahead is > -12 and < -1.1f && Math.Abs(row.Across) < 2.0f);
        bool nearby = contacts.Any(row => row.Range < 13);
        bool forward = contacts.Any(row => !row.Racer.DodgesItems && row.Ahead > 2 && row.Range <= 39 && row.Ahead / row.Range > .84f);
        bool fire = racer.Item switch
        {
            RaceItem.Pepper or RaceItem.PyrealCache => racer.Speed >= 8 && racer.Boost < .2f && (straight || held > 6),
            RaceItem.Invulnerability or RaceItem.TuskerMight => racer.Speed >= 8 && (incoming < 20 || nearby || straight && held > 2.4f),
            RaceItem.PortalWard => racer.Ward <= 0 && (incoming < 17 || obstacle || nearby && driver.PlayerSwipes > .40f || held > 10),
            RaceItem.ForceBurst => incoming < 6.1f || contacts.Any(row => row.Range < 5.5f && !row.Racer.DodgesItems),
            RaceItem.LeapingDraught => incoming is > 2 and < 14 || obstacle,
            RaceItem.MudPie => following,
            RaceItem.Burp => contacts.Any(row => row.Range < 8 && !row.Racer.DodgesItems),
            RaceItem.Frostbolt => forward,
            RaceItem.FrostVolley => contacts.Any(row =>
                !row.Racer.DodgesItems && row.Ahead is > 3 and < 32 && Math.Abs(row.Across) < 1.55f),
            RaceItem.TuskerPus or RaceItem.UlgrimFolly => contacts.Any(row => !row.Racer.DodgesItems && row.Range < 38),
            RaceItem.WhiteRabbit => WhiteRabbitTarget(racer) is { } target &&
                target.CoursePosition(track).DistanceSquaredTo(racer.CoursePosition(track)) < 104 * 104,
            RaceItem.ZefirHex => contacts.Any(row => !row.Racer.DodgesItems && row.Range < 55),
            RaceItem.PyrealSling => racer.Pyreals < MaxPyreals && straight,
            RaceItem.SmithHammer => contacts.Any(row =>
                !row.Racer.DodgesItems && row.Ahead is >= 3 and <= 27 && Math.Abs(row.Across) < 2.0f),
            RaceItem.WispThief => incoming < 18 || nearby || held > 8,
            RaceItem.Gromnatross or RaceItem.OlthoiLift => racer.Speed > 14 && !track.IsSpan(racer.Distance) &&
                (straight || held > 3) && !(flyover is { } route && AheadOnCourse(racer.Distance, route.Start) is > 0 and < 120),
            _ => false
        };
        if (!fire) return;
        var item = racer.Item;
        UseItem(racer);
        if (racer.Item != item) driver.ItemSince = elapsed;
        racer.UseHeldItemAt = Math.Max(racer.UseHeldItemAt, elapsed + (racer.Item == RaceItem.FrostVolley ? .34f : .68f));
    }

    private float IncomingRivalThreat(MountedRacer racer, RaceShield? shield = null)
    {
        Vector3 target = RiderTarget(racer);
        float closest = float.PositiveInfinity;
        foreach (var shot in shots)
        {
            if (shot.Owner == racer ||
                shield?.Blocks(shot.Kind == RaceItem.TuskerPus ? ShieldImpact.Collision : ShieldImpact.Spell) == false) continue;
            Vector3 offset = target - shot.Node.Position;
            float range = offset.Length();
            if (range < closest && (shot.Target == racer || range < 22 && shot.Heading.Dot(offset) > range * .92f))
                closest = range;
        }
        foreach (var shot in sideShots)
        {
            if (shot.Owner == racer || shield?.Blocks(shot.Impact) == false) continue;
            Vector3 relative = target - shot.Node.Position;
            Vector3 velocity = shot.Velocity - racer.Heading(track) * racer.Speed;
            float duration = relative.Dot(velocity) / Math.Max(.00001f, velocity.LengthSquared());
            if (duration >= 0 && duration <= shot.Lifetime - shot.Time &&
                (relative - velocity * duration).LengthSquared() <= shot.Radius * shot.Radius)
                closest = Math.Min(closest, relative.Length());
        }
        foreach (var chase in rabbits)
            if (chase.Target == racer && shield?.Blocks(ShieldImpact.Spell) != false)
                closest = Math.Min(closest, chase.Root.Position.DistanceTo(target));
        foreach (var pour in steinPours)
            if (pour.Target == racer && shield?.Blocks(ShieldImpact.Collision) != false)
                closest = Math.Min(closest, pour.SplashHead.Position.DistanceTo(target));
        return closest;
    }
}
