using Godot;

public partial class GrandPrix
{
    private void MoveSnowboard(MountedRacer racer, float dt, bool pushing, bool braking, float steering, bool carving)
    {
        Vector3 before = racer.CoursePosition(track);
        if (racer.Frozen > 0 || racer.Slowed > 0) braking = true;
        float boost = 0;
        if (racer.Boost > 0 && !braking)
        {
            bool charged = HasSuperDrift(racer);
            float cap = BoostCap(racer, charged);
            boost = (charged ? 10 : 6.4f) * (1 - Mathf.SmoothStep(cap * .76f, cap, racer.Speed));
        }
        var landing = racer.StepSnowboardPhysics(track, dt, steering, pushing, braking, carving, boost, UnpackedSnow(before));
        if (!landing.Wipeout) return;

        racer.Draw(track, 0, true, steering);
        ClearRacerPowers(racer);
        racer.Eject(track, racer.SnowboardHeading, inheritedVelocity: racer.SnowboardVelocity);
        racer.Speed = racer.Boost = 0;
        ResetRivalMotion(racer);
        if (racer == Player)
        {
            drifting = false;
            charge = 0;
            Callout("WIPEOUT!", 1.35f);
        }
    }
}
