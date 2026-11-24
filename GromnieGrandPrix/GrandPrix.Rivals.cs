using Godot;

public partial class GrandPrix
{
    private readonly record struct RivalStyle(float Pace, float Reaction, float Nerve, float Focus, float Drift, float Bias);
    private static readonly RivalStyle[] RivalStyles =
    [
        new(.935f, .23f, .90f, .72f, .70f, -.37f),  // Sir Chomps takes close passes.
        new(.965f, .32f, .42f, 1f, .84f, .28f),   // Puddle Pete studies the line.
        new(.950f, .28f, .68f, .82f, .99f, -.14f), // Mudonna commits to drifts.
        new(.920f, .38f, .28f, .87f, .52f, .42f)   // The Honk looks for space.
    ];

    private sealed class RivalDriver(RivalStyle style, float lane)
    {
        public readonly RivalStyle Style = style;
        public float AimLane = lane, LaneVelocity, NextDecision, Brake, DriftCharge, DriftReady, ItemSince;
        public float PlayerSwipes, PlayerSwipeSide;
        public bool Sliding;
    }

    private readonly record struct RivalContact(MountedRacer Racer, float Ahead, float Across, float Range);
    private readonly record struct RivalObstacle(float Lane, float Ahead, float Radius, bool Crossing = false);
    private readonly RivalLineMemory rivalLines = new();
    private RivalDriver[] rivalDrivers = [];
    private float lastLineSample = float.NegativeInfinity;

    private void ResetRivalDrivers()
    {
        rivalDrivers = racers.Skip(1).Select((racer, index) => new RivalDriver(RivalStyles[index], racer.Lane)).ToArray();
        lastLineSample = float.NegativeInfinity;
    }

    private void ResetRivalMotion(MountedRacer racer)
    {
        int index = racers.IndexOf(racer) - 1;
        if (index < 0 || index >= rivalDrivers.Length) return;
        var driver = rivalDrivers[index];
        driver.LaneVelocity = driver.DriftCharge = 0;
        driver.Sliding = false;
        driver.NextDecision = 0;
    }

    private int LineSector(float distance) =>
        (int)((track.IsPointToPoint ? Mathf.Clamp(distance, 0, track.Length) : Mathf.PosMod(distance, track.Length)) / RivalLineMemory.SectorLength);

    private void ObservePlayerLine(bool gas, bool brake)
    {
        if (activeTown == 0 || !gas || brake || Player.Finish >= 0 || Player.FreeDriving || Player.Flight is not null || Player.ResetHold > 0 ||
            Player.EjectTime > 0 || Player.SpinTime > 0 || Player.Frozen > 0 || Player.Slowed > 0 || Player.Hexed > 0 || IsSudsed(Player) ||
            Player.Boost > 0 || Player.MountUnavailable || Player.Distance - lastLineSample < 9) return;
        float distance = Player.Distance;
        if (Math.Abs(track.Curvature(distance)) < .0048f) return;
        float width = track.WidthAt(distance);
        if (width < 4 || Math.Abs(Player.Lane) > width * .82f) return;
        rivalLines.Observe(activeTown, LineSector(distance), Player.Lane / width, Player.Speed / GroundPace(Player));
        lastLineSample = distance;
    }

    private void ObservePlayerSwipe(MountedRacer attacker, int side)
    {
        if (racers.Count == 0 || attacker != Player) return;
        Vector3 origin = Player.CoursePosition(track);
        for (int i = 0; i < rivalDrivers.Length; i++)
        {
            var racer = racers[i + 1];
            var offset = racer.CoursePosition(track) - origin;
            if (racer.Finish >= 0 || racer.Flight is not null || Math.Abs(offset.Y) > 3 || offset.LengthSquared() > 18 * 18) continue;
            var driver = rivalDrivers[i];
            driver.PlayerSwipes = Math.Min(1, driver.PlayerSwipes + .26f);
            driver.PlayerSwipeSide = Mathf.Lerp(driver.PlayerSwipeSide, side, .32f);
        }
    }

    private RivalContact[] SenseRivals(MountedRacer racer)
    {
        Vector3 position = racer.CoursePosition(track), forward = racer.Heading(track), right = racer.Right(track);
        return racers.Where(other => other != racer && CanFrostboltHit(other))
            .Select(other => (Other: other, Offset: other.CoursePosition(track) - position))
            .Where(contact => Math.Abs(contact.Offset.Y) <= 4.5f && contact.Offset.LengthSquared() <= 72 * 72)
            .Select(contact => new RivalContact(contact.Other, contact.Offset.Dot(forward), contact.Offset.Dot(right), contact.Offset.Length()))
            .ToArray();
    }

    private List<RivalObstacle> SenseRivalObstacles(MountedRacer racer)
    {
        Vector3 position = racer.CoursePosition(track), forward = racer.Heading(track), right = racer.Right(track);
        var obstacles = new List<RivalObstacle>();
        void Add(Vector3 point, float radius)
        {
            var offset = point - position;
            if (Math.Abs(offset.Y) > 3 || offset.LengthSquared() > 42 * 42) return;
            float ahead = offset.Dot(forward);
            if (ahead > -.4f) obstacles.Add(new(racer.Lane + offset.Dot(right), ahead, radius));
        }
        foreach (var hazard in mud)
            if (hazard.Owner != racer && hazard.Expires > elapsed) Add(hazard.Node.Position, 1.4f);
        foreach (var prop in powerProps)
            if (prop.Owner != racer && prop.Item == RaceItem.SmithHammer && prop.EndAt > elapsed) Add(prop.Node.Position, 1.65f);
        foreach (var hazard in roadHazards)
            if (hazard.ActiveUntil > elapsed || hazard.PulseAt - elapsed is > 0 and < 1.25f) Add(hazard.Center, hazard.Radius);
        SenseEncounterObstacles(racer, obstacles);
        if (track.IsWaterCourse) SenseIslandGolems(racer, obstacles);
        if (track.IsVoid) SenseVoidBattleObstacles(racer, obstacles);
        return obstacles;
    }

    private float RivalPreferredLane(MountedRacer racer, RivalDriver driver, float look, float width)
    {
        float near = track.Curvature(racer.Distance + 6);
        float next = track.Curvature(racer.Distance + look);
        float outside = Math.Abs(near) < .004f ? Mathf.Clamp(next * 31, -.26f, .26f) : 0;
        float apex = Mathf.Clamp(near * 37, -.49f, .49f);
        float natural = driver.Style.Bias * .68f + apex - outside;
        if (rivalLines.Read(activeTown, LineSector(racer.Distance + look * .54f)) is { } learned)
        {
            float influence = Math.Min(.69f, (.14f + .105f * learned.Samples) * driver.Style.Focus);
            natural = Mathf.Lerp(natural, learned.Lane, influence);
        }
        return Mathf.Clamp(natural, -.82f, .82f) * width;
    }

    private float AheadOnCourse(float from, float to) => track.IsPointToPoint ? to - from : Mathf.PosMod(to - from, track.Length);

    private void PlanRival(MountedRacer racer, RivalDriver driver, int index)
    {
        float look = Mathf.Clamp(racer.Speed * .90f, 12, 105);
        float width = Math.Min(track.WidthAt(racer.Distance), track.WidthAt(racer.Distance + look));
        float edge = Math.Max(1, width - .62f);
        var contacts = SenseRivals(racer);
        var obstacles = SenseRivalObstacles(racer);
        float preferred = RivalPreferredLane(racer, driver, look, width);
        if (!racer.MountUnavailable && index % 2 == 1 && flyover is { Direct: true } summit &&
            AheadOnCourse(racer.Distance, summit.Start) is >= 0 and < 78)
        {
            driver.AimLane = 0;
            driver.Brake = 1;
            return;
        }

        var pickup = racer.Item != RaceItem.None ? null : pickups
            .Where(row => row.Available && AheadOnCourse(racer.Distance, row.Distance) is >= 2 and < 42)
            .MinBy(row => AheadOnCourse(racer.Distance, row.Distance) + Math.Abs(row.Lane - racer.Lane) * 1.8f);
        float? pad = racer.MountUnavailable ? null : pads.Where(row => AheadOnCourse(racer.Distance, row.Distance) is >= 2 and < 48)
            .OrderBy(row => AheadOnCourse(racer.Distance, row.Distance)).Select(row => (float?)row.Lane).FirstOrDefault();
        float[] choices = [preferred, driver.AimLane, racer.Lane, -.83f * edge, -.43f * edge, 0, .43f * edge, .83f * edge,
            pickup?.Lane ?? preferred, pad ?? preferred];

        float Score(float lane)
        {
            float score = Math.Abs(lane - preferred) * .53f + Math.Abs(lane - racer.Lane) * .20f +
                Math.Abs(lane - driver.AimLane) * .12f;
            foreach (var contact in contacts)
            {
                float gap = Math.Abs(lane - (racer.Lane + contact.Across));
                if (contact.Ahead is > -3.2f and < 23f)
                {
                    float approach = Mathf.Clamp((24f - Math.Max(0, contact.Ahead)) / 24, 0, 1);
                    float slower = .55f + Mathf.Clamp((racer.Speed - contact.Racer.Speed) * .12f, 0, 1f);
                    score += Math.Max(0, 2.8f - gap) * approach * slower * (1.85f - driver.Style.Nerve * .42f);
                }
                if (contact.Racer == Player && contact.Ahead is > -8 and < 13)
                {
                    float sided = .75f + .25f * Math.Sign(-contact.Across) * driver.PlayerSwipeSide;
                    float danger = driver.PlayerSwipes * sided * (1.12f - driver.Style.Nerve * .38f);
                    score += Math.Max(0, 4.8f - gap) * danger * 2.6f;
                }
            }
            foreach (var obstacle in obstacles)
            {
                float weight = obstacle.Crossing ? 10.7f * (1 - obstacle.Ahead / 95) : 6.2f * (1 - obstacle.Ahead / 53);
                score += Math.Max(0, obstacle.Radius + 1.26f - Math.Abs(lane - obstacle.Lane)) * weight;
            }
            if (pickup is not null) score -= Math.Max(0, 2 - Math.Abs(lane - pickup.Lane)) * (1.18f + driver.Style.Focus * .47f);
            if (pad is { } boostLane && racer.Boost < .6f)
                score -= Math.Max(0, 2.15f - Math.Abs(lane - boostLane)) * 1.56f;
            return score;
        }

        driver.AimLane = choices.Select(lane => Mathf.Clamp(lane, -edge, edge)).MinBy(Score);
        driver.Brake = 1;
        foreach (var contact in contacts)
            if (contact.Ahead is > 0 and < 10 && Math.Abs(driver.AimLane - racer.Lane - contact.Across) < 1.65f)
                driver.Brake = Math.Min(driver.Brake, .74f + contact.Ahead * .022f);
        if (obstacles.Any(row => row.Crossing && row.Ahead < 13 && Math.Abs(row.Lane - driver.AimLane) < row.Radius + .5f))
            driver.Brake = Math.Min(driver.Brake, .66f);

        DecideRivalGuard(racer, driver, contacts, obstacles);
        DecideRivalItem(racer, driver, contacts, obstacles);
        if (racer.Flight is not null || racer.Stability < 44 || racer.SwipeCooldown > 0 || racer.GuardWindow > 0) return;
        var target = contacts.Where(row => row.Range <= Math.Max(5, racer.Weapon.Reach) && !row.Racer.DodgesItems && row.Racer.ItemGrace <= 0)
            .OrderBy(row => row.Range).FirstOrDefault();
        if (target.Racer is not null && (driver.Style.Nerve > .5f || racer.Stability > target.Racer.Stability + 20) &&
            (target.Racer != Player || driver.PlayerSwipes < .6f || driver.Style.Nerve > .78f))
        {
            int side = Math.Sign(target.Across);
            if (FindSwipeTarget(racer, side) == target.Racer) TrySwipe(racer, side);
        }
    }

    private bool MoveRival(MountedRacer racer, int index, float dt)
    {
        var driver = rivalDrivers[index - 1];
        driver.PlayerSwipes = Mathf.MoveToward(driver.PlayerSwipes, 0, dt * .012f);
        if (elapsed >= driver.NextDecision)
        {
            driver.NextDecision = elapsed + driver.Style.Reaction;
            PlanRival(racer, driver, index);
        }
        if (racer.Flight is not null) return false;

        bool flipped = IsSudsed(racer);
        float curve = track.Curvature(racer.Distance + 3);
        float targetLane = flipped ? -driver.AimLane : driver.AimLane;
        float aim = (targetLane - racer.Lane) * 1.22f - driver.LaneVelocity * .24f;
        float steer = Mathf.Clamp((aim + curve * 14) / LaneSteeringRate(racer, sliding: false), -1, 1);
        bool sliding = !flipped && !racer.MountUnavailable && !racer.FreeDriving && elapsed >= driver.DriftReady && racer.Speed > 12 &&
            Math.Abs(curve) * driver.Style.Drift > .010f && Math.Abs(steer) > .17f && driver.DriftCharge < 1.05f;
        if (sliding) driver.DriftCharge += dt;
        if (!sliding && driver.Sliding)
        {
            ReleaseDriftBoost(racer, driver.DriftCharge, .98f, 1.12f);
            driver.DriftReady = elapsed + .9f + driver.Style.Reaction;
            driver.DriftCharge = 0;
        }
        driver.Sliding = sliding;

        float pace = SpeedLimit(racer, GroundPace(racer) * driver.Style.Pace) * driver.Brake;
        pace = TurnPace(racer, pace, sliding, Mathf.Lerp(.83f, .98f, driver.Style.Nerve));
        if (flipped && racer.SudsedUntil - elapsed > SudsDuration - driver.Style.Reaction * 2) pace = 0;
        MoveGroundRacer(racer, ref driver.LaneVelocity, dt, pace, PaceAcceleration(racer, pace), steer, sliding, allowOffRoad: false);
        return sliding;
    }
}
