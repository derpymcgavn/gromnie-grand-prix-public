using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const int MaxPyreals = 10;
    private const uint PyrealSetup = 0x02000b77, HammerSetup = 0x0200014e;
    private const uint WispSetup = 0x0200059a, WispMotion = 0x09000031;
    private static readonly Color PyrealTint = new("ffc763"), HexTint = new("dcaaff"),
        MightTint = new("f7ac69"), ForceTint = new("adf3ff");

    private void UseRacePower(MountedRacer racer, RaceItem item)
    {
        switch (item)
        {
            case RaceItem.FrostVolley:
                ThrowItem(racer, item);
                break;
            case RaceItem.PyrealSling:
            case RaceItem.SmithHammer:
                LobPower(racer, item);
                break;
            case RaceItem.PyrealCache:
                CreditPyreal(racer);
                GrantBoost(racer, .55f);
                Wave(RiderTarget(racer), PyrealTint, 1.55f, .45f);
                break;
            case RaceItem.LeapingDraught:
                if (racer.UsesSnowboard) racer.LaunchSnowboardJump(MountedRacer.LeapLaunchSpeed);
                else racer.Leap = MountedRacer.LeapDuration;
                Array.Clear(racer.DustContact);
                Wave(racer.CoursePosition(track) + Vector3.Up * .18f, new("a3f5bf"), 2.1f, .48f);
                break;
            case RaceItem.ZefirHex:
                CastZefirHex(racer);
                break;
            case RaceItem.TuskerMight:
                racer.Might = 4.2f;
                Empower(racer, racer.Might, MightTint);
                break;
            case RaceItem.Invulnerability:
                racer.Invulnerable = 4.8f;
                Empower(racer, racer.Invulnerable, new("fff294"));
                break;
            case RaceItem.ForceBurst:
                CastForceBurst(racer);
                break;
            case RaceItem.WispThief:
                Pickpocket(racer);
                break;
        }
    }

    private void Empower(MountedRacer racer, float duration, Color tint)
    {
        racer.Frozen = racer.Slowed = racer.SpinTime = 0;
        GrantBoost(racer, duration);
        Wave(RiderTarget(racer), tint, 3f, .76f, new(.18f, 0, 0));
    }

    private void CastZefirHex(MountedRacer caster)
    {
        float progress = RaceProgress(caster);
        var rivals = racers.Where(rival => rival != caster && CanFrostboltHit(rival))
            .OrderBy(rival => RaceProgress(rival)).ToArray();
        var targets = rivals.Where(rival => RaceProgress(rival) > progress).Take(2).ToArray();
        if (targets.Length == 0) targets = rivals.TakeLast(1).ToArray();
        foreach (var target in targets)
        {
            if (!AcceptHit(target, impact: ShieldImpact.Spell)) continue;
            RemovePowerForm(target);
            target.Hexed = 3.4f;
            target.SwingTime = 0;
            var rabbit = assets.CreateActor(WhiteRabbitSetup, WhiteRabbitMotion, 2.4f);
            rabbit.ApplyNativeAppearance(RabbitWhiteFur);
            SetPowerForm(target, rabbit);
            Wave(RiderTarget(target), HexTint, 2.75f, .62f, new(.31f, 0, .16f));
            if (target == Player) Callout("ZEFIR HEX! YOU'RE A RABBIT", 1.6f);
        }
    }

    private void Pickpocket(MountedRacer thief)
    {
        thief.Phased = 2.6f;
        thief.Hexed = 0;
        thief.Frozen = thief.Slowed = thief.SpinTime = 0;
        thief.SwingTime = 0;
        RemovePowerForm(thief);
        var wisp = assets.CreateActor(WispSetup, WispMotion, .9f);
        wisp.Position = Vector3.Up * 1.05f;
        SetPowerForm(thief, wisp);
        Wave(RiderTarget(thief), HexTint, 2.35f, .65f);

        var victim = racers.Where(rival => rival != thief && rival.Finish < 0 && !rival.DodgesItems
                && rival.Item != RaceItem.None)
            .MinBy(rival => RivalDistanceSquared(thief, rival));
        if (victim is null) return;
        HoldItem(thief, victim.Item);
        if (victim.ItemCharges > 0) thief.ItemCharges = victim.ItemCharges;
        HoldItem(victim, RaceItem.None);
        thief.UseHeldItemAt = elapsed + .8f;
        if (thief == Player) Callout("STOLEN · " + ItemName(thief.Item).ToUpperInvariant());
        else if (victim == Player) Callout("A WISP STOLE YOUR ITEM!");
    }

    private float RivalDistanceSquared(MountedRacer first, MountedRacer second) =>
        first.CoursePosition(track).DistanceSquaredTo(second.CoursePosition(track));

    private static void SetPowerForm(MountedRacer racer, AcActor form)
    {
        racer.PowerForm = form;
        racer.Root.AddChild(form);
        racer.SetMountVisible(false);
    }

    private static void RemovePowerForm(MountedRacer racer)
    {
        if (racer.PowerForm is not { } form) return;
        form.Hide();
        form.QueueFree();
        racer.PowerForm = null;
        racer.SetMountVisible(racer.Flight is null);
    }

    private static void ClearRacerPowers(MountedRacer racer)
    {
        RemovePowerForm(racer);
        racer.Leap = racer.Might = racer.Invulnerable = racer.Phased = racer.Hexed = racer.SpinTime = racer.PowerEffectAt = 0;
        racer.Root.Scale = Vector3.One;
    }

    private void TickRacerPowers(MountedRacer racer, float dt)
    {
        racer.Leap = Mathf.MoveToward(racer.Leap, 0, dt);
        racer.Might = Mathf.MoveToward(racer.Might, 0, dt);
        racer.Invulnerable = Mathf.MoveToward(racer.Invulnerable, 0, dt);
        racer.Phased = Mathf.MoveToward(racer.Phased, 0, dt);
        racer.Hexed = Mathf.MoveToward(racer.Hexed, 0, dt);
        racer.SpinTime = Mathf.MoveToward(racer.SpinTime, 0, dt);
        float size = Mathf.MoveToward(racer.Root.Scale.Y, racer.Might > 0 ? 1.52f : 1, dt * 3.8f);
        racer.Root.Scale = Vector3.One * size;
        if (racer.PowerForm is { } form)
        {
            if (racer.Phased == 0 && racer.Hexed == 0) RemovePowerForm(racer);
            else form.Tick(dt, racer.Hexed > 0 && racer.Speed > .4f, Vector3.Zero);
        }
    }

    private void TickPowerContacts()
    {
        foreach (var racer in racers)
        {
            if (racer.Finish >= 0 || racer.Flight is not null || racer.Might <= 0 && racer.Invulnerable <= 0) continue;
            if (elapsed >= racer.PowerEffectAt)
            {
                racer.PowerEffectAt = elapsed + .24f;
                Wave(racer.CoursePosition(track) + Vector3.Up * .35f,
                    racer.Might > 0 ? MightTint : new("fff294"), racer.Might > 0 ? 2.8f : 1.95f, .45f);
            }
            float reach = racer.Might > 0 ? 2.45f : 1.65f;
            foreach (var rival in racers)
                if (rival != racer && RivalDistanceSquared(racer, rival) < reach * reach)
                    PowerImpact(rival, racer.Might > 0 ? MightTint : new("fff294"), 1.35f, .60f);
        }
    }

    private void PowerImpact(MountedRacer target, Color tint, float slow, float speedFactor)
    {
        if (!AcceptHit(target)) return;
        target.Slowed = Math.Max(target.Slowed, slow);
        target.Speed *= speedFactor;
        target.SpinTime = MountedRacer.SpinDuration;
        target.ItemGrace = .64f;
        Wave(RiderTarget(target), tint, 1.8f, .46f);
        if (target == Player) Callout("KNOCKED BACK!");
    }

    private static void CreditPyreal(MountedRacer racer) => racer.Pyreals = Math.Min(MaxPyreals, racer.Pyreals + 1);
}
