using Godot;

public partial class GrandPrix
{
    private const float MountStrainLimit = 5, MountStrainWarning = 3.5f, MountRecoveryDuration = 20;
    private const int SkiDriftsBeforeThrow = 3;
    private const float SkiDriftChainWindow = 5;
    private const string JesusModeMessage = "JESUS MODE!";

    private bool TryThrowFromJetSki(MountedRacer racer)
    {
        if (!racer.UsesJetSki) return false;
        if (elapsed - racer.LastSkiDriftAt > SkiDriftChainWindow) racer.SkiDriftStreak = 0;
        racer.LastSkiDriftAt = elapsed;
        if (++racer.SkiDriftStreak <= SkiDriftsBeforeThrow) return false;

        racer.SkiDriftStreak = 0;
        racer.Boost = racer.SuperDriftUntil = racer.SwingTime = racer.Leap = 0;
        racer.GroundDrifting = false;
        racer.MountRecovery = MountedRacer.JetSkiRecoveryDuration;
        racer.Speed = Math.Min(racer.Speed, GroundPace(racer));
        ResetRivalMotion(racer);
        if (racer == Player)
        {
            drifting = false;
            charge = lateral = 0;
        }
        NotifyJesusMode(racer);
        return true;
    }

    private void NotifyJesusMode(MountedRacer racer)
    {
        ImpactRegattaWater(racer.CoursePosition(track), 2.7f, .29f);
        SplashJesusModeStart(racer);
        if (racer == Player) Callout(JesusModeMessage, 2.6f);
    }

    private void TickBoostStrain(MountedRacer racer, float dt)
    {
        if (racer.MountUnavailable || racer.UsesJetSki || racer.UsesSnowboard) return;
        bool pushingMount = racer.Boost > 0 && racer.Speed > GroundPace(racer) + .10f &&
            !racer.Falling && racer.Flight is null && racer.PowerForm is null && racer.EjectTime <= 0 && racer.ResetHold <= 0;
        if (!pushingMount)
        {
            racer.BoostStrain = Mathf.MoveToward(racer.BoostStrain, 0, dt * 1.35f);
            return;
        }
        racer.BoostStrain += Math.Min(dt, racer.Boost);
        if (racer.BoostStrain < MountStrainLimit) return;

        racer.BoostStrain = racer.Boost = racer.SuperDriftUntil = racer.SwingTime = 0;
        racer.GroundDrifting = false;
        racer.MountRecovery = MountRecoveryDuration;
        racer.Speed = Math.Min(racer.Speed, GroundPace(racer));
        Array.Clear(racer.DustContact);
        ResetRivalMotion(racer);
        if (racer != Player) return;
        drifting = false;
        charge = 0;
        Callout("YOUR MOUNT IS SPENT! RUN!", 2);
    }

    private void TickMountRecovery(MountedRacer racer, float dt)
    {
        if (racer.OnFootToFinish) return;
        if (racer.MountRecovery > 0)
        {
            racer.MountRecovery = Math.Max(0, racer.MountRecovery - dt);
            if (racer.MountRecovery == 0) racer.RemountTime = MountedRacer.RemountDuration;
            return;
        }
        if (racer.RemountTime <= 0 || !racer.CanRemount) return;
        racer.RemountTime = Math.Max(0, racer.RemountTime - dt);
        if (racer.RemountTime == 0 && racer == Player)
            Callout(racer.UsesJetSki ? "BACK ON YOUR SKI!" : "BACK IN THE SADDLE!", 1.5f);
    }
}
