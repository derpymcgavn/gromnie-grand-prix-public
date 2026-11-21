using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private enum EncounterKind { OlthoiCrossing, TuskerToss, LugianQuarry, RollingDeath }

    private sealed class RaceEncounter(EncounterKind kind, float distance, Vector3 center, float reach, int side)
    {
        public readonly EncounterKind Kind = kind;
        public readonly float Distance = distance, Reach = reach;
        public readonly Vector3 Center = center;
        public readonly int Side = side;
        public readonly CrossingBody[] Bodies = Enumerable.Range(0, kind switch
        {
            EncounterKind.OlthoiCrossing => 7,
            EncounterKind.RollingDeath => 2,
            _ => 1
        }).Select(_ => new CrossingBody()).ToArray();
        public readonly List<MeshInstance3D> Lamps = [];
        public readonly List<AcActor> Cast = [];
        public readonly List<Node3D> QuarryPile = [];
        public Node3D? Stage, Projectile;
        public float Start = float.PositiveInfinity;
        public float QuarryChipAt = float.PositiveInfinity;
        public int HitMask, LastThrow = -1, LastCatch = -1, LastPick = -1, DeathRollElement;
        public bool Warned;
        public string Warning => Kind switch
        {
            EncounterKind.OlthoiCrossing => "OLTHOI CROSSING",
            EncounterKind.TuskerToss => "TUSKER TOSS",
            EncounterKind.LugianQuarry => "LUGIAN QUARRY",
            _ => "ROLLING DEATH"
        };
        public string Callout => Kind switch
        {
            EncounterKind.TuskerToss => "BARREL TOSS AHEAD!",
            _ => Warning + " AHEAD!"
        };
        public float Duration => Kind switch
        {
            EncounterKind.OlthoiCrossing => 7.8f + Reach * .16f,
            EncounterKind.TuskerToss => 9.2f,
            EncounterKind.LugianQuarry => QuarryFirstThrow + (QuarryThrows - 1) * QuarryPeriod + QuarryTravel + .85f,
            _ => DeathRollDuration
        };
    }

    private sealed class CrossingBody
    {
        public Vector3 Before, Position, Heading;
        public bool Dangerous;
    }

    private readonly record struct CrossingPose(Vector3 Position, Vector3 Heading, bool Visible, bool Dangerous);
    private readonly List<RaceEncounter> raceEncounters = [];
    private const float EncounterWarningTime = 1.7f, TossPeriod = 2.48f, TossTravel = 1.80f;
    private static readonly Color BarrelImpactTint = new("ffbe66"), SwarmImpactTint = new("d9eb87");

    private void ResetEncounters()
    {
        raceEncounters.Clear();
        if (track.IsVoid) return;
        // Stable fixtures for the race checks. Each live race rolls its own sites, cast order and directions.
        var random = onlinePlan is { } plan ? new Random(plan.Seed ^ 3497) : testMode ? new Random(3497 + activeTown) : Random.Shared;
        int count = Mathf.Clamp(Mathf.RoundToInt(track.Length / 485), 2, 13);
        float spacing = (track.Length - 214) / count;
        var kinds = Enum.GetValues<EncounterKind>();
        random.Shuffle(kinds);
        int nextKind = 0;
        for (int i = 0; i < count; i++)
        {
            bool placed = false;
            for (int attempt = 0; attempt < 18; attempt++)
            {
                float at = 133 + spacing * (i + .12f + random.NextSingle() * .71f);
                if ((raceEncounters.Count > 0 && at - raceEncounters[^1].Distance < 175) || !CanStageEncounter(at)) continue;
                var kind = kinds[nextKind];
                int side = random.Next(2) == 0 ? -1 : 1;
                if (kind == EncounterKind.RollingDeath && !CanStageRollingDeath(at, side))
                {
                    side = -side;
                    if (!CanStageRollingDeath(at, side)) continue;
                }
                var site = new RaceEncounter(kind, at, track.Point(at), track.WidthAt(at) + 3.5f, side)
                {
                    DeathRollElement = kind == EncounterKind.RollingDeath ? random.Next(RollingDeathSpells.Length) : 0
                };
                raceEncounters.Add(site);
                BuildEncounterSigns(site);
                placed = true;
                break;
            }
            // Rolling Death needs a longer stretch. Keep other encounters available when it cannot fit.
            if (!placed && kinds[nextKind] != EncounterKind.RollingDeath) continue;
            if (++nextKind == kinds.Length)
            {
                random.Shuffle(kinds);
                nextKind = 0;
            }
        }
    }

    private bool CanStageEncounter(float at)
    {
        if (flyover is { } flight && at > flight.Start - 120 && at < flight.End + 105) return false;
        if (Enumerable.Range(0, track.GateCount + 1).Any(gate => Math.Abs(at - track.Gate(gate)) < 23) ||
            pads.Any(row => Math.Abs(at - row.Distance) < 13) || pickups.Any(row => Math.Abs(at - row.Distance) < 13))
            return false;
        float width = track.WidthAt(at);
        if (width < 6 || Math.Abs(track.Curvature(at - 9)) > .016f || Math.Abs(track.Curvature(at + 6)) > .016f) return false;
        foreach (float run in new[] { -19f, -5f, 0, 5f })
        {
            float distance = at + run;
            if (track.IsSpan(distance) || track.OnHighTrestle(distance, 0) ||
                Math.Abs(track.Point(distance).Y - track.Point(at).Y) > Math.Max(1.3f, Math.Abs(run) * .18f)) return false;
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 edge = track.Surface(distance, side * track.WidthAt(distance));
                float margin = run == -19 ? 1.55f : 3.5f;
                if (!ClearBank(margin) || run == -5 && side == 1 && !ClearBank(1.65f)) return false;

                bool ClearBank(float shoulder)
                {
                    Vector3 bank = track.FreeGround(track.Surface(distance, side * (track.WidthAt(distance) + shoulder)));
                    return Math.Abs(edge.Y - bank.Y) <= 1.48f && track.NativeDry(bank) && track.CanStageSpectator(bank);
                }
            }
        }
        return true;
    }

    private void TickEncounters(float dt, Vector3 near)
    {
        if (countdown > 0) return;
        foreach (var site in raceEncounters)
        {
            bool visible = site.Center.DistanceSquaredTo(near) <= 141 * 141;
            if (float.IsPositiveInfinity(site.Start))
            {
                foreach (var racer in racers)
                {
                    if (racer.Finish >= 0 || racer.Flight is not null || racer.EjectTime > 0 || racer.Speed < 6) continue;
                    float ahead = AheadOnCourse(racer.Distance, site.Distance);
                    if (ahead < 3 || ahead > Mathf.Clamp(racer.Speed * 3.05f, 27, 84) ||
                        racer.CoursePosition(track).DistanceSquaredTo(site.Center) > 94 * 94) continue;
                    site.Start = elapsed;
                    break;
                }
            }
            float age = elapsed - site.Start;
            if (age < 0) continue;
            if (age >= site.Duration)
            {
                SetEncounterLights(site, false);
                // The finished pile remains in the world and can be seen again on a later lap.
                if (site.Kind == EncounterKind.LugianQuarry && visible)
                {
                    if (site.Stage is null)
                        StageEncounter(site);
                    if (site.LastCatch < QuarryThrows - 1)
                        DrawLugianQuarry(site, age, 0);
                }
                else if (site.Stage is not null) ReleaseEncounterStage(site);
                continue;
            }
            if (!site.Warned && Player.Flight is null && Player.EjectTime <= 0 &&
                AheadOnCourse(Player.Distance, site.Distance) is > 0 and < 90 && visible)
            {
                site.Warned = true;
                Callout(site.Callout, 1.85f);
            }
            SetEncounterLights(site, ((int)(age * 5) & 1) == 0);
            if (visible && site.Stage is null) StageEncounter(site);
            if (!visible && site.Stage is not null) ReleaseEncounterStage(site);
            for (int i = 0; i < site.Bodies.Length; i++)
            {
                var body = site.Bodies[i];
                var pose = EncounterPose(site, i, age);
                body.Before = body.Dangerous ? body.Position : pose.Position;
                body.Position = pose.Position;
                body.Heading = pose.Heading;
                body.Dangerous = pose.Dangerous;
                if (site.Stage is null || site.Kind != EncounterKind.OlthoiCrossing) continue;
                var actor = site.Cast[i];
                actor.Visible = pose.Visible;
                if (!pose.Visible) continue;
                actor.Position = pose.Position;
                actor.Tick(dt * 1.42f, true, pose.Heading);
            }
            if (site.Stage is null) continue;
            if (site.Kind == EncounterKind.TuskerToss) DrawTuskerToss(site, age, dt);
            if (site.Kind == EncounterKind.LugianQuarry) DrawLugianQuarry(site, age, dt);
            if (site.Kind == EncounterKind.RollingDeath) DrawRollingDeath(site, age, dt);
        }
    }

    private CrossingPose EncounterPose(RaceEncounter site, int index, float age)
    {
        if (site.Kind == EncounterKind.LugianQuarry) return QuarryRockPose(site, age);
        if (site.Kind == EncounterKind.RollingDeath) return RollingDeathPose(site, index, age);
        float motion = age - EncounterWarningTime;
        if (site.Kind == EncounterKind.OlthoiCrossing)
        {
            float stride = (motion - index * .56f) * (9.8f + index % 3 * .53f);
            float t = Mathf.Clamp(stride / (2 * site.Reach), 0, 1);
            float at = site.Distance + ((index % 3) - 1) * 2.15f + Mathf.Sin(t * Mathf.Pi) * ((index & 1) == 0 ? 2 : -2);
            float lane = Mathf.Lerp(site.Side * site.Reach, -site.Side * site.Reach, t);
            Vector3 heading = -site.Side * track.Right(at) + track.Forward(at) * ((index & 1) == 0 ? .14f : -.14f);
            Vector3 ground = EncounterGround(at, lane);
            bool running = stride >= 0 && stride < 2 * site.Reach;
            return new(ground, heading.Normalized(), running, running);
        }
        int pass = Math.Clamp((int)(Math.Max(0, motion) / TossPeriod), 0, 2);
        int side = pass % 2 == 0 ? site.Side : -site.Side;
        float phase = motion - pass * TossPeriod;
        float progress = Mathf.Clamp(phase / TossTravel, 0, 1);
        float laneAt = Mathf.Lerp(side * (site.Reach - 1.05f), -side * (site.Reach - 1.05f), progress);
        float across = site.Distance + Mathf.Lerp(-2.25f, 2.25f, progress) * side * site.Side;
        float rise = phase < 0 ? Mathf.Lerp(.83f, 1.26f, Mathf.SmoothStep(0, 1, Mathf.Clamp(1 + phase / .54f, 0, 1)))
            : Mathf.Lerp(1.26f, .83f, Mathf.Clamp((phase - TossTravel) * 2.2f, 0, 1)) + Mathf.Sin(progress * Mathf.Pi) * 1.03f;
        return new(EncounterGround(across, laneAt) + Vector3.Up * rise,
            -side * track.Right(across), true, phase is >= 0 and <= TossTravel);
    }

    private Vector3 EncounterGround(float at, float lane) => Math.Abs(lane) <= track.WidthAt(at) + .62f
        ? track.Surface(at, lane) : track.FreeGround(track.Surface(at, lane));

    private void CheckEncounterHits(MountedRacer racer, int index, Vector3 before, Vector3 after)
    {
        if (racer.EjectTime > 0 || racer.Finish >= 0) return;
        int bit = 1 << index;
        foreach (var site in raceEncounters)
        {
            float reach = site.Kind == EncounterKind.RollingDeath ? 61 : 49;
            if ((site.HitMask & bit) != 0 || elapsed - site.Start is < EncounterWarningTime ||
                elapsed - site.Start >= site.Duration || after.DistanceSquaredTo(site.Center) > reach * reach) continue;
            bool swarm = site.Kind == EncounterKind.OlthoiCrossing;
            for (int bodyIndex = 0; bodyIndex < site.Bodies.Length; bodyIndex++)
            {
                var body = site.Bodies[bodyIndex];
                if (!body.Dangerous) continue;
                float radius = site.Kind switch
                {
                    EncounterKind.TuskerToss => 1.36f,
                    EncounterKind.RollingDeath => DeathRollRadius + .55f,
                    _ => 1.6f
                };
                // Sweep in relative space so an oncoming racer and a moving obstacle can meet between frames.
                var offset = before - body.Before;
                var travel = after - body.Position - offset;
                var planarOffset = new Vector2(offset.X, offset.Z);
                var planarTravel = new Vector2(travel.X, travel.Z);
                float t = Mathf.Clamp(-planarOffset.Dot(planarTravel) / Math.Max(.00001f, planarTravel.LengthSquared()), 0, 1);
                float height = -offset.Y - travel.Y * t;
                if ((planarOffset + planarTravel * t).LengthSquared() > radius * radius ||
                    height < -.64f || height > (swarm ? 1.85f : 2.82f)) continue;
                if (!AcceptHit(racer))
                {
                    if (racer.ItemGrace <= 0 && !racer.DodgesItems) site.HitMask |= bit;
                    continue;
                }
                site.HitMask |= bit;
                ClearRacerPowers(racer);
                racer.Stability = 0;
                racer.Draw(track, 0, false);
                racer.Eject(track, body.Heading);
                racer.Speed = racer.Boost = 0;
                if (after.DistanceSquaredTo(Player.CoursePosition(track)) < 104 * 104)
                {
                    if (site.Kind == EncounterKind.RollingDeath)
                    {
                        var spell = DeathRollElement(site, bodyIndex);
                        itemEffects?.PlayAt(spell.Effects, 5, after + Vector3.Up);
                        Wave(after + Vector3.Up * .48f, spell.Tint, 2.8f, .57f);
                    }
                    else if (site.Kind == EncounterKind.LugianQuarry)
                        dust.StoneImpact(after + Vector3.Up * .45f, body.Heading, 1.2f);
                    else Wave(after + Vector3.Up * .65f, swarm ? SwarmImpactTint : BarrelImpactTint, 2.8f, .57f);
                }
                if (racer == Player) Callout(site.Kind switch
                {
                    EncounterKind.OlthoiCrossing => "SWEPT UP BY THE SWARM!",
                    EncounterKind.TuskerToss => "BARRELED OFF YOUR MOUNT!",
                    EncounterKind.RollingDeath => "UNSEATED BY ROLLING DEATH!",
                    _ => "KNOCKED OFF BY A BOULDER!"
                }, 1.65f);
                return;
            }
        }
    }

    private void SenseEncounterObstacles(MountedRacer racer, List<RivalObstacle> obstacles)
    {
        foreach (var site in raceEncounters)
        {
            if (!float.IsFinite(site.Start) || elapsed - site.Start >= site.Duration) continue;
            if (site.Kind == EncounterKind.RollingDeath)
            {
                SenseRollingDeath(site, racer, obstacles);
                continue;
            }
            float ahead = AheadOnCourse(racer.Distance, site.Distance);
            if (ahead < 0 || ahead > 73) continue;
            float age = elapsed - site.Start + ahead / Math.Max(10, racer.Speed);
            for (int i = 0; i < site.Bodies.Length; i++)
            {
                var pose = EncounterPose(site, i, age);
                if (pose.Dangerous)
                    obstacles.Add(new((pose.Position - site.Center).Dot(track.Right(site.Distance)), ahead, 1.95f, Crossing: true));
            }
        }
    }
}
