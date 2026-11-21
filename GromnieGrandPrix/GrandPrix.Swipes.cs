using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private int weaponIndex;
    private sealed record SideShot(MountedRacer Owner, Node3D Node, Vector3 Velocity, Color Tint,
        float Radius, float Lifetime, ShieldImpact Impact)
    {
        public float Time;
    }
    private readonly List<SideShot> sideShots = [];

    private MountedRacer? FindSwipeTarget(MountedRacer attacker, int side)
    {
        if (side == 0) return null;
        Vector3 origin = attacker.CoursePosition(track);
        Vector3 forward = attacker.Heading(track), right = attacker.Right(track);
        return racers.Where(target =>
        {
            if (target == attacker || target.Finish >= 0 || target.ResetHold > 0 || target.EjectTime > 0) return false;
            Vector3 offset = target.CoursePosition(track) - origin;
            float alongside = offset.Dot(right) * side;
            return Math.Abs(offset.Y) <= 2 && alongside >= .28f && alongside <= attacker.Weapon.Reach
                && Math.Abs(offset.Dot(forward)) <= attacker.Weapon.Lengthwise;
        }).MinBy(target => target.CoursePosition(track).DistanceSquaredTo(origin));
    }
    private void TrySwipe(MountedRacer attacker, int side)
    {
        if (side == 0 || attacker.Flight is not null || attacker.Hexed > 0 || attacker.Phased > 0 || attacker.LeapHeight > .32f ||
            attacker.SwipeCooldown > 0 || attacker.GuardWindow > 0 || attacker.ResetHold > 0 || attacker.EjectTime > 0 || attacker.Finish >= 0) return;
        attacker.SwingSide = side;
        attacker.SwingTime = RaceWeapon.SwingDuration;
        attacker.SwingLanded = false;
        attacker.SwipeCooldown = attacker.Weapon.Cooldown;
        ObservePlayerSwipe(attacker, side);
        Vector3 at = RiderTarget(attacker) + attacker.Right(track) * side * 1.25f;
        Wave(at, attacker.Weapon.Shot == SideShotKind.Lightning ? new("b3f3ff") : new("ffe7a0"),
            1.62f, .29f, new(.27f, 0, side * .53f));
    }

    private void TickRacerSwipe(MountedRacer attacker, float dt)
    {
        attacker.SwipeCooldown = Math.Max(0, attacker.SwipeCooldown - dt);
        if (attacker.SwingTime <= 0) return;
        attacker.SwingTime = Math.Max(0, attacker.SwingTime - dt);
        if (attacker.SwingLanded || attacker.SwingTime > RaceWeapon.SwingDuration - attacker.Weapon.Windup) return;
        attacker.SwingLanded = true;
        if (attacker.Weapon.Shot != SideShotKind.None)
        {
            LaunchSideShot(attacker);
            return;
        }
        var target = FindSwipeTarget(attacker, attacker.SwingSide);
        if (target is not null)
            ApplyWeaponImpact(attacker, target, attacker.Right(track) * attacker.SwingSide, ShieldImpact.Swipe, new("ffbe62"));
    }

    private void LaunchSideShot(MountedRacer shooter)
    {
        var side = shooter.Right(track) * shooter.SwingSide;
        var launch = RiderTarget(shooter) + side * .82f;
        bool lightning = shooter.Weapon.Shot == SideShotKind.Lightning;
        var spell = AcCatalog.Projectiles["lightning"];
        Color tint = lightning ? new("a9edff") : new("ffe1a3");
        Vector3 velocity = side * (lightning ? 34 : 46) + shooter.Heading(track) * shooter.Speed;
        var node = new Node3D { Position = launch, Basis = Basis.LookingAt(velocity.Normalized()) };
        itemFxRoot.AddChild(node);
        float life = (shooter.Weapon.Reach - .82f) / (lightning ? 34 : 46);
        var model = assets.CreateActor(lightning ? spell.Setup : shooter.Weapon.Setup, 0, lightning ? .65f : .74f);
        node.AddChild(model);
        if (!lightning) model.RotationDegrees = new Vector3(72, 0, 0);
        var streak = BogCircuit.Box(node, new(0, 0, .38f), new(lightning ? .14f : .045f, .06f, lightning ? 1.76f : 1.3f), Glow(tint));
        streak.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        if (lightning) itemEffects?.PlayEffect(spell.Effects, 4, node, life);
        sideShots.Add(new(shooter, node, velocity, tint, lightning ? 1.03f : .71f, life,
            lightning ? ShieldImpact.Spell : ShieldImpact.Collision));
    }

    private void TickSideShots(float dt)
    {
        for (int i = sideShots.Count - 1; i >= 0; i--)
        {
            var shot = sideShots[i];
            float step = Math.Min(dt, shot.Lifetime - shot.Time);
            Vector3 before = shot.Node.Position, travel = shot.Velocity * step;
            shot.Node.Position += travel;
            shot.Time += step;
            MountedRacer? hit = null;
            float best = float.PositiveInfinity;
            foreach (var racer in racers)
            {
                if (racer == shot.Owner || racer.Finish >= 0 || racer.Flight is not null || racer.EjectTime > 0 ||
                    racer.ResetHold > 0 || racer.ItemGrace > 0 || racer.DodgesItems) continue;
                Vector3 offset = RiderTarget(racer) - before;
                float fraction = Mathf.Clamp(offset.Dot(travel) / Math.Max(travel.LengthSquared(), .00001f), 0, 1);
                if (fraction >= best || (offset - travel * fraction).LengthSquared() > shot.Radius * shot.Radius) continue;
                hit = racer;
                best = fraction;
            }
            if (hit is null && shot.Time < shot.Lifetime) continue;
            if (hit is not null)
                ApplyWeaponImpact(shot.Owner, hit, shot.Velocity.Normalized(), shot.Impact, shot.Tint);
            shot.Node.QueueFree();
            sideShots.RemoveAt(i);
        }
    }

    private void ApplyWeaponImpact(MountedRacer attacker, MountedRacer target, Vector3 push, ShieldImpact impact, Color tint)
    {
        if (!AcceptHit(target, impact: impact)) return;

        target.Speed *= .97f;
        if (impact == ShieldImpact.Spell) target.Boost = Math.Min(target.Boost, .18f);
        target.Stability = Math.Max(0, target.Stability - attacker.Weapon.BalanceLoss);
        target.ItemGrace = .42f;
        Wave(RiderTarget(target), tint, 1.42f, .36f);
        if (target.Stability == 0)
        {
            ClearRacerPowers(target);
            target.Eject(track, push);
            target.Speed = target.Boost = 0;
            if (attacker == Player) Callout($"UNSEATED {target.Name.ToUpperInvariant()}!", 1.3f);
            if (target == Player) Callout("KNOCKED OFF!", 1.25f);
            return;
        }
        if (attacker == Player) Callout($"{target.Name.ToUpperInvariant()} · {target.Stability}% BALANCE", .85f);
        if (target == Player) Callout($"SIDESWIPED · {target.Stability}% BALANCE", .88f);
    }
}
