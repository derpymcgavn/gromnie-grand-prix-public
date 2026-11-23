using Godot;

public partial class GrandPrix
{
    private const float MountStrainLimit = 5, MountStrainWarning = 3.5f, MountRecoveryDuration = 20;

    private void TickBoostStrain(MountedRacer racer, float dt)
    {
        if (racer.MountUnavailable) return;
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
        if (racer.MountRecovery > 0)
        {
            racer.MountRecovery = Math.Max(0, racer.MountRecovery - dt);
            if (racer.MountRecovery == 0) racer.RemountTime = MountedRacer.RemountDuration;
            return;
        }
        if (racer.RemountTime <= 0 || !racer.CanRemount) return;
        racer.RemountTime = Math.Max(0, racer.RemountTime - dt);
        if (racer.RemountTime == 0 && racer == Player) Callout("BACK IN THE SADDLE!", 1.5f);
    }
}
