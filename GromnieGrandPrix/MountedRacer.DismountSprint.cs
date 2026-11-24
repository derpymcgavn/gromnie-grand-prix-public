using Godot;

public sealed partial class MountedRacer
{
    private const float DismountThrowDuration = .78f;
    private Transform3D dismountTakeoff;
    private Vector3 dismountOffset;

    internal bool OnFootToFinish;
    internal bool DismountSprintThrowing { get; private set; }
    internal bool AlreadyRunningForDismountSprint => overboostDetached && RemountTime <= 0;

    internal void ResetDismountSprint()
    {
        OnFootToFinish = DismountSprintThrowing = false;
    }

    internal void BeginDismountSprintThrow(BogCircuit track, Transform3D takeoff, bool parkMount)
    {
        dismountTakeoff = takeoff;
        dismountOffset = takeoff.Origin - CoursePosition(track);
        if (!overboostDetached) DetachForOverboost();
        if (parkMount)
        {
            mountDeathRemaining = StartMountDeath(Mount);
            if (CompanionSkate is { } companion)
                mountDeathRemaining = Math.Max(mountDeathRemaining, StartMountDeath(companion));
        }

        seatedTransform = Rider.Transform;
        Rider.TopLevel = true;
        Rider.GlobalTransform = takeoff;
        SetDismountFallingPose();
        EjectTime = DismountThrowDuration;
        DismountSprintThrowing = true;
    }

    internal void PrepareReplicaDismountThrow()
    {
        Reattach();
        if (!overboostDetached) DetachForOverboost();
        SetDismountFallingPose();
    }

    internal void UpdateDismountSprintThrow(BogCircuit track, float dt)
    {
        EjectTime = Mathf.MoveToward(EjectTime, 0, dt);
        if (EjectTime <= 0 || Finish >= 0)
        {
            DismountSprintThrowing = false;
            Reattach();
            Draw(track, dt, Speed > .4f);
            return;
        }

        Draw(track, dt, false);
        Rider.TickCurrentMotion(dt);
        float age = 1 - EjectTime / DismountThrowDuration;
        float blend = Mathf.SmoothStep(0, 1, age);
        Vector3 landing = Root.ToGlobal(Vector3.Up * runningFootOffset);
        Vector3 midair = CoursePosition(track) + dismountOffset;
        Vector3 origin = midair.Lerp(landing, blend) + Vector3.Up * (1.6f * Mathf.Sin(Mathf.Pi * age));
        Quaternion from = dismountTakeoff.Basis.Orthonormalized().GetRotationQuaternion().Normalized();
        Quaternion to = Root.GlobalBasis.Orthonormalized().GetRotationQuaternion().Normalized();
        var stance = new Basis(from.Slerp(to, blend).Normalized());
        var flip = new Basis(Vector3.Right, -Mathf.Tau * blend);
        Vector3 scale = dismountTakeoff.Basis.Scale.Lerp(seatedTransform.Basis.Scale, blend);
        Rider.GlobalTransform = new Transform3D(stance * flip * Basis.FromScale(scale), origin);
        Rider.GlobalPosition += Vector3.Up * RoadMeshLift(track, riderRoadContacts);
    }

    private void SetDismountFallingPose()
    {
        Rider.HoldFinalPose = true;
        PrepareFallingPose();
        Rider.TickCurrentMotion(0);
        overboostJumpPose = true;
    }
}
