using Godot;
using DerethSurvivors;

public sealed partial class MountedRacer
{
    internal float BoostStrain, MountRecovery, RemountTime;
    internal bool MountUnavailable => MountRecovery > 0 || RemountTime > 0;
    internal bool CanRemount => !Falling && Flight is null && !Rider.TopLevel &&
        EjectTime <= 0 && ResetHold <= 0 && PowerForm is null && Leap <= 0;
    internal const float RemountDuration = .65f;

    private const uint MountDeathMotion = 0x40000011, NativeReady = 0x41000003, NativeRun = 0x44000007;
    private bool overboostDetached, overboostJumpPose, overboostRemountPose;
    private bool overboostRiderVisible;
    private float mountDeathRemaining, runningFootOffset;
    private Transform3D overboostSeatPose, overboostMountPose, overboostCompanionPose;

    private bool DrawOverboost(BogCircuit track, float dt, bool moving, float steering)
    {
        if (!MountUnavailable && !Rider.TopLevel)
        {
            if (overboostDetached) RestoreOverboostPose(reseat: true);
            return false;
        }
        if (!overboostDetached)
        {
            if (Rider.TopLevel) return false;
            DetachForOverboost();
        }

        bool remounting = RemountTime > 0 && CanRemount;
        if (remounting)
        {
            if (!overboostRemountPose)
            {
                RestoreOverboostMount();
                StartOverboostJump();
                overboostRemountPose = true;
            }
            SetRigVisible(true);
            DrawMountRig(dt, moving, steering, poseRiderArms: false);
            float progress = 1 - Mathf.Clamp(RemountTime / RemountDuration, 0, 1);
            Vector3 seatTarget = Root.ToLocal(seat.ToGlobal(overboostSeatPose.Origin));
            Rider.Position = (Vector3.Up * runningFootOffset).Lerp(seatTarget, Mathf.SmoothStep(0, 1, progress))
                + Vector3.Up * (.85f * Mathf.Sin(Mathf.Pi * progress));
            Rider.TickCurrentMotion(dt);
        }
        else
        {
            overboostRemountPose = false;
            DrawOverboostMountDeath(dt);
            if (!Rider.TopLevel)
            {
                Rider.Position = Vector3.Up * runningFootOffset;
                if (Leap > 0 || Falling)
                {
                    if (!overboostJumpPose) StartOverboostJump(Falling);
                    Rider.TickCurrentMotion(dt);
                }
                else
                {
                    if (overboostJumpPose)
                    {
                        Rider.HoldFinalPose = false;
                        Rider.Play(moving ? NativeRun : NativeReady, false);
                        overboostJumpPose = false;
                    }
                    Rider.Tick(dt * (moving ? Mathf.Clamp(Speed / 8, .75f, 1.7f) : 1), moving, Vector3.Zero);
                }
            }
        }
        if (!Rider.TopLevel) Rider.Visible = overboostRiderVisible && PowerForm is null;
        ApplyRoadPoseLift(track, dt, mounted: remounting);
        return true;
    }

    private void DetachForOverboost()
    {
        overboostDetached = true;
        overboostSeatPose = Rider.Transform;
        overboostRiderVisible = Rider.Visible;
        Rider.Reparent(Root, false);
        float runningScale = flightRiderScale * RaceMount.Get(RaceMountKind.Gromnie).Scale;
        Rider.Transform = new Transform3D(Basis.FromScale(Vector3.One * runningScale), Vector3.Zero);
        Rider.HoldFinalPose = false;
        Rider.Play(NativeReady, false);
        Rider.TickCurrentMotion(0);
        runningFootOffset = -BodyBounds(Rider, Basis.Identity).Position.Y * runningScale;
        Array.Clear(DustContact);
        roadPoseLift = 0;

        overboostMountPose = Mount.Transform;
        overboostCompanionPose = CompanionSkate?.Transform ?? Transform3D.Identity;
        SetRigVisible(false);
        if (MountRecovery <= 0 || PowerForm is not null) return;
        float duration = StartMountDeath(Mount);
        if (CompanionSkate is { } companion) duration = Math.Max(duration, StartMountDeath(companion));
        mountDeathRemaining = duration;
    }

    private static float StartMountDeath(AcActor actor)
    {
        if (!actor.Play(MountDeathMotion, true)) return 0;
        Transform3D position = actor.GlobalTransform;
        actor.TopLevel = true;
        actor.GlobalTransform = position;
        actor.HoldFinalPose = true;
        actor.TickCurrentMotion(0);
        actor.Show();
        return Mathf.Clamp(actor.CurrentMotionDuration, .28f, 1.35f);
    }

    private void DrawOverboostMountDeath(float dt)
    {
        mountDeathRemaining = Math.Max(0, mountDeathRemaining - dt);
        bool visible = mountDeathRemaining > 0 && PowerForm is null;
        Mount.Visible = visible && Mount.TopLevel;
        if (CompanionSkate is { } companion) companion.Visible = visible && companion.TopLevel;
        if (!visible) return;
        if (Mount.Visible) Mount.TickCurrentMotion(dt);
        if (CompanionSkate is { Visible: true } other) other.TickCurrentMotion(dt);
    }

    private void StartOverboostJump(bool falling = false)
    {
        overboostJumpPose = true;
        Rider.HoldFinalPose = true;
        if (falling || !Rider.Play(0x2500003b, true) && !Rider.Play(0x1000004b, true) && !Rider.Play(0x1000004a, true))
            PrepareFallingPose();
    }

    private void RestoreOverboostPose(bool reseat)
    {
        RestoreOverboostMount();
        overboostDetached = overboostJumpPose = overboostRemountPose = false;
        Rider.Visible = overboostRiderVisible;
        Rider.HoldFinalPose = false;
        if (reseat)
        {
            Rider.Reparent(seat, false);
            Rider.Scale = Vector3.One * riderScale;
            PoseMounted();
        }
        SetMountVisible(PowerForm is null && Flight is null);
    }

    private void RestoreOverboostMount()
    {
        RestoreMountActor(Mount, overboostMountPose);
        if (CompanionSkate is { } companion) RestoreMountActor(companion, overboostCompanionPose);
        mountDeathRemaining = 0;

        static void RestoreMountActor(AcActor actor, Transform3D resting)
        {
            if (!actor.TopLevel) return;
            actor.TopLevel = false;
            actor.Transform = resting;
            actor.HoldFinalPose = false;
            actor.Play(NativeReady, false);
            actor.TickCurrentMotion(0);
        }
    }
}
