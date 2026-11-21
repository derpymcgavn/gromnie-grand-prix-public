using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const uint LugianSetup = 0x02000A0B, LugianMotion = 0x09000006, QuarryPickaxe = 0x0200017D;
    private const uint QuarryBoulder = 0x020005D0;
    private const float QuarryFirstThrow = EncounterWarningTime + .72f, QuarryPeriod = 2.71f, QuarryTravel = 1.57f;
    private const int QuarryThrows = 3;
    private Aabb? quarryBoulderBounds;

    private float QuarryFaceLane(RaceEncounter site) => site.Side * site.Reach;
    private Vector3 QuarryFace(RaceEncounter site) => EncounterGround(site.Distance - 3.75f, QuarryFaceLane(site));
    private static float QuarryRockRadius(int index) => .74f + index * .06f;

    private static (float Distance, float Lane, float Height) QuarryLanding(RaceEncounter site, int index)
    {
        // Lay two stones side by side on the shoulder, then drop the last onto them.
        var (along, across, lift) = index switch
        {
            0 => (1.28f, -.46f, 0f),
            1 => (3.42f, .45f, 0f),
            _ => (2.39f, .06f, .35f)
        };
        return (site.Distance + along, -site.Side * (site.Reach + across), QuarryRockRadius(index) * .61f + lift);
    }

    private Vector3 QuarryPilePoint(RaceEncounter site, int index)
    {
        var (at, lane, height) = QuarryLanding(site, index);
        return EncounterGround(at, lane) + Vector3.Up * height;
    }

    private (int Index, float Phase) QuarryFlight(float age)
    {
        float time = age - QuarryFirstThrow;
        int index = Mathf.Clamp(Mathf.FloorToInt((time + .55f) / QuarryPeriod), 0, QuarryThrows - 1);
        return (index, time - index * QuarryPeriod);
    }

    private CrossingPose QuarryRockPose(RaceEncounter site, float age)
    {
        var (index, phase) = QuarryFlight(age);
        var landing = QuarryLanding(site, index);
        float progress = Mathf.Clamp(phase / QuarryTravel, 0, 1);
        float at = Mathf.Lerp(site.Distance + .75f, landing.Distance, progress);
        float lane = Mathf.Lerp(site.Side * (site.Reach - 1.05f), landing.Lane, progress);
        float rise = Mathf.Lerp(1.62f, landing.Height, progress);
        rise += Mathf.Sin(progress * Mathf.Pi) * 1.05f;
        var position = EncounterGround(at, lane) + Vector3.Up * rise;
        bool flying = phase is >= 0 and < QuarryTravel;
        return new(position, -site.Side * track.Right(at), phase is >= -.43f and < QuarryTravel, flying);
    }

    private void StageLugianQuarry(RaceEncounter site)
    {
        var stage = site.Stage!;
        float faceLane = QuarryFaceLane(site);
        Vector3 face = QuarryFace(site);
        Vector3 minerAt = EncounterGround(site.Distance - 1.85f, faceLane);
        Vector3 throwerAt = EncounterGround(site.Distance + .75f, faceLane);
        for (int i = 0; i < 2; i++)
        {
            var lugian = assets.CreateActor(LugianSetup, LugianMotion, i == 0 ? 1.18f : 1.26f);
            stage.AddChild(lugian);
            site.Cast.Add(lugian);
            lugian.Position = i == 0 ? minerAt : throwerAt;
            Vector3 heading = i == 0 ? face - minerAt : -site.Side * track.Right(site.Distance);
            lugian.Face(new(heading.X, 0, heading.Z));
            lugian.OnHook = (hook, _) => raceAudio?.PlayEmote(hook, lugian);
            if (i == 0) lugian.Hold(QuarryPickaxe, 1, scale: 1.25f);
        }
        var outcrop = AddQuarryBoulder(stage, "Quarry face");
        outcrop.Position = face + Vector3.Up * .97f;
        outcrop.Scale = new(1.83f, 1.55f, 1.72f);
        outcrop.Rotation = new(0, .32f, .10f);
        for (int i = 0; i < QuarryThrows; i++)
        {
            var rock = AddQuarryBoulder(stage, "Tossed stone in the pile");
            rock.Position = QuarryPilePoint(site, i);
            rock.Scale = Vector3.One * QuarryRockRadius(i);
            rock.Rotation = new(.78f * i, 1.4f * i, -.41f * i);
            rock.Visible = false;
            site.QuarryPile.Add(rock);
        }
        site.Projectile = AddQuarryBoulder(stage, "Flying quarry stone");
        site.Projectile.Visible = false;
        site.LastPick = -1;
        site.QuarryChipAt = float.PositiveInfinity;
    }

    private Node3D AddQuarryBoulder(Node3D parent, string name)
    {
        var prop = new Node3D { Name = name };
        var boulder = assets.CreateActor(QuarryBoulder, 0);
        prop.AddChild(boulder);
        parent.AddChild(prop);
        if (quarryBoulderBounds is null)
            quarryBoulderBounds = boulder.BodyMeshes.Select(mesh => mesh.GlobalTransform * mesh.Mesh.GetAabb()).Aggregate((a, b) => a.Merge(b));
        var bounds = quarryBoulderBounds.Value;
        float scale = 2 / Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
        boulder.Scale = Vector3.One * scale;
        boulder.Position = -bounds.GetCenter() * scale;
        return prop;
    }

    private void DrawLugianQuarry(RaceEncounter site, float age, float dt)
    {
        var miner = site.Cast[0];
        var thrower = site.Cast[1];
        var (index, phase) = QuarryFlight(age);
        if (dt > 0 && age < site.Duration)
        {
            int strike = Mathf.FloorToInt((age - QuarryFirstThrow + 1.37f) / QuarryPeriod);
            if (strike is >= 0 and < QuarryThrows && strike > site.LastPick)
            {
                site.LastPick = strike;
                if (!miner.AttackCreature(0x10000062, 1.17f)) miner.Attack();
                site.QuarryChipAt = age + Mathf.Clamp(miner.AttackImpactDelay, .18f, .58f);
            }
            if (age >= site.QuarryChipAt)
            {
                dust.StoneImpact(QuarryFace(site) + Vector3.Up * 1.15f, track.Right(site.Distance), .68f);
                site.QuarryChipAt = float.PositiveInfinity;
            }
            if (index > site.LastThrow && phase is >= -.43f and < .25f)
            {
                site.LastThrow = index;
                if (!thrower.AttackCreature(0x10000064, 1.22f)) thrower.Attack();
            }
            miner.Tick(dt, false, Vector3.Zero);
            thrower.Tick(dt, false, Vector3.Zero);
        }

        int landed = Mathf.Clamp(Mathf.FloorToInt((age - QuarryFirstThrow - QuarryTravel) / QuarryPeriod) + 1, 0, QuarryThrows);
        for (int i = site.LastCatch + 1; i < landed; i++)
        {
            site.QuarryPile[i].Visible = true;
            if (dt > 0 && age - (QuarryFirstThrow + i * QuarryPeriod + QuarryTravel) < .16f)
                dust.StoneImpact(site.QuarryPile[i].Position - Vector3.Up * .17f, -site.Side * track.Right(site.Distance), 1.1f);
            site.LastCatch = i;
        }

        var stone = site.Projectile!;
        var pose = QuarryRockPose(site, age);
        stone.Visible = pose.Visible;
        if (!pose.Visible) return;
        stone.Scale = Vector3.One * QuarryRockRadius(index);
        stone.Position = pose.Position;
        if (phase < 0 && thrower.Setup.Holdings.TryGetValue(1, out var grip))
            stone.Position = thrower.Part(grip.Part).GlobalTransform * AcAssets.Convert(grip.Pose).Origin;
        float roll = Mathf.Clamp(phase / QuarryTravel, 0, 1) * Mathf.Tau;
        stone.Rotation = new(roll * 1.2f, index * .58f + roll, roll * -.7f);
    }
}
