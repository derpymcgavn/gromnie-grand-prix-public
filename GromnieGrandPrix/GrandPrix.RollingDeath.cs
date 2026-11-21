using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private readonly record struct RollingDeathSpell(uint Id, uint Setup, uint Effects, Color Tint);

    // War Magic records 2791–2794 and their projectile setups and physics script tables.
    private static readonly RollingDeathSpell[] RollingDeathSpells =
    [
        new(2791, 0x02000DDC, 0x3400009F, new("a6ed45")),
        new(2792, 0x02000DDD, 0x340000A0, new("ff713c")),
        new(2793, 0x02000DDE, 0x3400009E, new("a1dcff")),
        new(2794, 0x02000DDF, 0x340000A1, new("b3a6ff"))
    ];
    private const float DeathRollRelease = 1.85f, DeathRollInterval = 1.37f;
    private const float DeathRollAlongTime = 5.02f, DeathRollAcrossTime = 3.44f;
    private const float DeathRollEntry = 21, DeathRollExit = 43, DeathRollCrossing = 6.5f;
    private const float DeathRollRadius = 1.60f, DeathRollHeight = 1.67f;
    private const float DeathRollDuration = DeathRollRelease + DeathRollAlongTime + .75f;

    private static float DeathRollStart(int index) => DeathRollRelease + index * DeathRollInterval;
    private static float DeathRollTravel(int index) => index == 0 ? DeathRollAlongTime : DeathRollAcrossTime;
    private static float DeathRollProgress(int index, float age) =>
        Mathf.Clamp((age - DeathRollStart(index)) / DeathRollTravel(index), 0, 1);
    private static float DeathRollOffset(int index, float progress) =>
        index == 0 ? Mathf.Lerp(DeathRollEntry, -DeathRollExit, progress) : DeathRollCrossing;

    private float DeathRollLane(float at, int side, int index, float progress)
    {
        float width = track.WidthAt(at);
        return side * (index == 0 ? width * .26f : (width + 2.5f) * (1 - 2 * progress));
    }

    private Vector3 DeathRollGround(float distance, int side, int index, float progress)
    {
        float at = distance + DeathRollOffset(index, progress);
        return EncounterGround(at, DeathRollLane(at, side, index, progress));
    }

    private bool CanStageRollingDeath(float at, int side)
    {
        float from = at - DeathRollExit, to = at + DeathRollEntry;
        if (from < BogCircuit.Grid + 47 || to > track.Length - 22 ||
            Enumerable.Range(0, track.GateCount + 1).Any(gate => track.Gate(gate) >= from - 7 && track.Gate(gate) <= to + 8))
            return false;
        if (flyover is { } flight && from < flight.End + 75 && to > flight.Start - 110) return false;
        for (int index = 0; index < 2; index++)
        {
            Vector3 previous = DeathRollGround(at, side, index, 0);
            for (int step = 0; step <= 12; step++)
            {
                float progress = step / 12f;
                float station = at + DeathRollOffset(index, progress);
                float lane = DeathRollLane(station, side, index, progress);
                Vector3 ground = DeathRollGround(at, side, index, progress);
                if (track.WidthAt(station) < 8 || track.IsSpan(station) || track.OnHighTrestle(station, lane) ||
                    Math.Abs(ground.Y - previous.Y) > 2.95f || !track.NativeDry(ground) || !track.CanStageSpectator(ground))
                    return false;
                previous = ground;
            }
        }
        return true;
    }

    private CrossingPose RollingDeathPose(RaceEncounter site, int index, float age)
    {
        float progress = DeathRollProgress(index, age);
        float at = site.Distance + DeathRollOffset(index, progress);
        Vector3 heading = index == 0 ? -track.Forward(at) : -site.Side * track.Right(at);
        Vector3 ground = DeathRollGround(site.Distance, site.Side, index, progress);
        float phase = age - DeathRollStart(index);
        bool active = phase >= 0 && phase < DeathRollTravel(index);
        return new(ground + Vector3.Up * DeathRollHeight, heading, active, active);
    }

    private static RollingDeathSpell DeathRollElement(RaceEncounter site, int index) =>
        RollingDeathSpells[(site.DeathRollElement + index) % RollingDeathSpells.Length];

    private void StageRollingDeath(RaceEncounter site)
    {
        for (int i = 0; i < site.Bodies.Length; i++)
        {
            var spell = DeathRollElement(site, i);
            var actor = assets.CreateActor(spell.Setup, 0, assets.NativeLaunchScale(spell.Effects, 2));
            actor.Visible = false;
            site.Stage!.AddChild(actor);
            site.Cast.Add(actor);
        }
    }

    private void DrawRollingDeath(RaceEncounter site, float age, float dt)
    {
        for (int i = 0; i < site.Bodies.Length; i++)
        {
            var body = site.Bodies[i];
            var actor = site.Cast[i];
            actor.Position = body.Position;
            if (body.Dangerous && !actor.Visible)
            {
                actor.Face(body.Heading);
                itemEffects?.PlayEffect(DeathRollElement(site, i).Effects, 4, actor,
                    Math.Max(dt, DeathRollStart(i) + DeathRollTravel(i) - age));
            }
            else if (!body.Dangerous && actor.Visible)
                itemEffects?.PlayAt(DeathRollElement(site, i).Effects, 5, body.Position);
            actor.Visible = body.Dangerous;
            if (actor.Visible)
            {
                actor.Face(body.Heading);
                actor.TickCurrentMotion(dt);
            }
        }
    }

    private void SenseRollingDeath(RaceEncounter site, MountedRacer racer, List<RivalObstacle> obstacles)
    {
        float ahead = site.Distance - racer.Distance;
        if (!track.IsSprint) ahead = Mathf.Wrap(ahead, -track.Length * .5f, track.Length * .5f);
        if (ahead is < -26 or > 109) return;
        float age = elapsed - site.Start;
        float speed = Math.Max(10, racer.Speed);
        for (int index = 0; index < site.Bodies.Length; index++)
        {
            float delay = DeathRollStart(index) - age;
            float first = Math.Max(0, delay), last = Math.Min(73 / speed, delay + DeathRollTravel(index));
            if (first > last || Gap(first) < -DeathRollRadius || Gap(last) > DeathRollRadius) continue;
            // Solve for when the racer reaches the shot in the direction of the road.
            for (int step = 0; step < 8; step++)
            {
                float middle = (first + last) * .5f;
                if (Gap(middle) > 0) first = middle;
                else last = middle;
            }
            float eta = (first + last) * .5f;
            float progress = DeathRollProgress(index, age + eta);
            float at = site.Distance + DeathRollOffset(index, progress);
            obstacles.Add(new(DeathRollLane(at, site.Side, index, progress), speed * eta, DeathRollRadius + .55f, Crossing: true));

            float Gap(float time) => ahead + DeathRollOffset(index, DeathRollProgress(index, age + time)) - speed * time;
        }
    }
}
