using Godot;

public partial class GrandPrix
{
    private bool TryDismountSprint(MountedRacer racer)
    {
        if (!track.IsDismountSprint || racer.OnFootToFinish || racer.Distance < track.DismountLine ||
            racer.Finish >= 0 || racer.Falling || racer.EjectTime > 0) return false;

        bool wasRunning = racer.AlreadyRunningForDismountSprint;
        bool parkMount = racer.Flight is null && racer.PowerForm is null;
        racer.Draw(track, 0, racer.Speed > .4f);
        Transform3D takeoff = racer.Rider.GlobalTransform;
        racer.Land();
        ClearRacerPowers(racer);
        racer.Boost = racer.BoostStrain = racer.MountRecovery = racer.RemountTime = racer.SwingTime = 0;
        racer.GroundDrifting = false;
        racer.SuperDriftUntil = 0;
        racer.FreeDriving = false;
        racer.OnFootToFinish = true;
        if (IsMountedPickup(racer.Item)) HoldItem(racer, RaceItem.PortalWard);
        ClampDismountSprintLane(racer);
        ResetRivalMotion(racer);
        if (racer == Player)
        {
            lateral = charge = 0;
            drifting = false;
        }

        if (wasRunning)
        {
            racer.Speed = Math.Min(racer.Speed, GroundPace(racer));
            racer.Draw(track, 0, racer.Speed > .4f);
        }
        else
            racer.BeginDismountSprintThrow(track, takeoff, parkMount);
        NotifyDismountSprint(racer);
        return true;
    }

    private bool TickDismountSprintTransition(MountedRacer racer, float dt)
    {
        if (!racer.DismountSprintThrowing) return false;

        float airborneStep = Math.Min(racer.EjectTime, dt);
        Vector3 before = racer.CoursePosition(track);
        float footPace = GroundPace(racer);
        if (racer.Speed > footPace)
            racer.Speed = Mathf.MoveToward(racer.Speed, footPace, TurnBraking * airborneStep);
        AdvanceAlongLane(racer, airborneStep, 0);
        racer.Distance = Math.Min(racer.Distance, track.Length);
        ClampDismountSprintLane(racer);
        RecordGate(racer, before, racer.CoursePosition(track), airborneStep);
        if (dt >= racer.EjectTime) racer.Speed = Math.Min(racer.Speed, footPace);
        racer.UpdateDismountSprintThrow(track, airborneStep);
        return true;
    }

    private void ClampDismountSprintLane(MountedRacer racer)
    {
        float room = Math.Max(0, track.WidthAt(racer.Distance) - .42f);
        racer.Lane = Mathf.Clamp(racer.Lane, -room, room);
    }

    private void NotifyDismountSprint(MountedRacer racer)
    {
        if (racer == Player) Callout("HIT THE GROUND RUNNING!", 2.0f);
    }
}
