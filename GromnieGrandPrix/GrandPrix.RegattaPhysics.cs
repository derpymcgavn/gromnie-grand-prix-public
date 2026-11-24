using Godot;

public partial class GrandPrix
{
    private readonly RegattaFluidField regattaFluid = new();

    private void TickJetSkiWater(MountedRacer racer, float dt, float steering, bool sliding)
    {
        if (!racer.UsesJetSki) return;
        Vector3 point = racer.CoursePosition(track);
        float impact = racer.StepJetSkiWater(point, racer.Heading(track), RegattaWaterTime, dt, steering, sliding, regattaFluid);
        if (impact < 1.8f || racer.MountUnavailable || racer.EjectTime > 0 || racer.LeapHeight > .1f) return;
        ApplyJetSkiLanding(racer, point, impact);
        // A heavy slap eats a little momentum. The throttle can pull it back as the hull settles.
        racer.Speed = Math.Max(0, racer.Speed - Mathf.Clamp((impact - 3.2f) * .55f, 0, 2.1f));
    }

    private void ApplyJetSkiLanding(MountedRacer racer, Vector3 point, float impact)
    {
        ImpactRegattaWater(point, 2.45f, Mathf.Lerp(.10f, .31f, Mathf.SmoothStep(2, 8, impact)));
        SplashJetSkiLanding(racer, point, impact);
    }

    private void UpdateRegattaSurface(bool refreshFluid = false)
    {
        float time = RegattaWaterTime;
        SetRegattaWaterTime(time);
        Vector3 playerPosition = Player.CoursePosition(track);
        SetRegattaFluidSurface(regattaFluid, playerPosition, refreshFluid);
        foreach (var (pad, anchor, heading) in regattaPads)
        {
            if (anchor.DistanceSquaredTo(playerPosition) > 175 * 175) continue;
            Vector3 right = heading.Cross(Vector3.Up);
            pad.Position = anchor + Vector3.Up * (WaveAt(anchor, time) + .065f);
            float pitch = MathF.Atan((WaveAt(anchor + heading, time) - WaveAt(anchor - heading, time)) * .5f);
            float bank = MathF.Atan((WaveAt(anchor + right, time) - WaveAt(anchor - right, time)) * .5f);
            pad.Rotation = new Vector3(pitch, BogCircuit.Yaw(heading), bank);
        }
    }

    private float WaveAt(Vector3 point, float time) =>
        RegattaSurface.HeightOffset(new Vector2(point.X, point.Z), time, regattaFluid);

    private void ImpactRegattaWater(Vector3 point, float radius, float strength) =>
        regattaFluid.Disturb(new Vector2(point.X, point.Z), radius, -strength);
}
