using Godot;

public partial class GrandPrix
{
    private float voidFinaleAge = -1;
    private float voidSealAt = -1;
    private bool voidFinaleCamera;
    private int voidGalaxyLap;

    private void ResetVoidProgress()
    {
        voidFinaleAge = voidSealAt = -1;
        voidFinaleCamera = false;
    }

    private void UpdateVoidRaceProgress()
    {
        if (voidSealAt < 0)
        {
            float firstFinish = float.PositiveInfinity;
            foreach (var racer in racers)
                if (racer.Finish >= 0 && racer.Finish <= elapsed) firstFinish = Math.Min(firstFinish, racer.Finish);
            if (float.IsFinite(firstFinish)) StartVoidSealing(firstFinish);
        }
        if (voidSealAt >= 0)
        {
            if (voidFinaleAge < VoidFinaleDuration) DrawVoidSealing(Math.Clamp(elapsed - voidSealAt, 0, VoidFinaleDuration));
            return;
        }
        int stage = VoidBattleLap();
        if (stage == voidGalaxyLap) return;
        voidGalaxyLap = stage;
        SetVoidGalaxyProgress((stage - 1f) / track.Laps);
    }

    private void StartVoidSealing(float clock)
    {
        voidSealAt = clock;
        QuietVoidBattleForFinale();
        SetVoidGalaxyProgress(1);
        DrawVoidSealing(0);
    }

    private bool BeginVoidFinaleCamera()
    {
        if (voidFinaleAge < 0 || voidFinaleAge >= VoidFinaleDuration) return false;
        voidFinaleCamera = true;
        raceUi.Hide();
        resultsUi.Hide();
        PlayMusic(ClashMusic);
        ClearSpeedBlur();
        AimVoidFinale(voidFinaleAge);
        return true;
    }

    private void TickVoidFinale(float dt)
    {
        if (!racing || !track.IsVoid)
        {
            ResetVoidProgress();
            return;
        }
        DrawVoidSealing(Math.Min(voidFinaleAge + dt, VoidFinaleDuration));
        AimVoidFinale(voidFinaleAge);
        if (voidFinaleAge < VoidFinaleDuration) return;
        voidFinaleCamera = false;
        PlayRaceResultsMusic();
        resultsUi.Show();
    }

    private void DrawVoidSealing(float age)
    {
        voidFinaleAge = age;
        float clock = voidSealAt + age;
        float pulse = 1 - Mathf.SmoothStep(2.6f, 4.8f, voidFinaleAge);
        DrawVoidAsheron(pulse * .73f, pulse * .55f, clock, VoidPoseInterval(camera.Position, VoidAsheronPosition));
        DrawVoidAerbax(pulse * .31f, pulse * .20f, clock, VoidPoseInterval(camera.Position, VoidAerbaxPosition));
        DrawVoidFinale(age);
    }

    private void AimVoidFinale(float age)
    {
        float approach = Mathf.SmoothStep(2.35f, 5.9f, age);
        float sweep = Mathf.Lerp(-.28f, .27f, age / VoidFinaleDuration);
        Vector3 outward = VoidCasterHeading.Rotated(Vector3.Up, sweep);
        camera.Projection = Camera3D.ProjectionType.Perspective;
        camera.Near = .1f;
        camera.Far = 2900;
        camera.Fov = Mathf.Lerp(51, 38, approach);
        camera.Position = outward * Mathf.Lerp(267, 123, approach) + Vector3.Up * Mathf.Lerp(193, 107, approach);
        camera.LookAt((Vector3.Up * 112).Lerp(VoidFinaleShardFocus, approach));
    }
}
