using Godot;

public partial class GrandPrix
{
    private void CheckHighSpeedPickups()
    {
        ResetRace();
        var shooter = Player;
        var target = racers[1];
        foreach (var rival in racers.Skip(2)) rival.Finish = 0;
        Vector3 origin = track.FreeGround(track.Point(track.Gate(0)));
        Vector3 heading = Vector3.Forward;
        shooter.FreeDriving = target.FreeDriving = true;
        shooter.FreePosition = origin;
        shooter.FreeYaw = target.FreeYaw = BogCircuit.Yaw(heading);

        shooter.Speed = target.Speed = DriftSpeedCap;
        target.FreePosition = origin + heading * 8;
        ThrowItem(shooter, RaceItem.FrostVolley);
        Require(shots.Single().Target is null, "Frost Volley stays straight at racing speed");
        RunMovingTarget();
        Require(target.Frozen > 0 && target.SpinTime > 0, "Frost Volley catches a racer at the super drift limit");

        target.Frozen = target.SpinTime = 0;
        shooter.Speed = RoadPace;
        target.Speed = DriftSpeedCap;
        target.FreePosition = origin + heading * 22 + Vector3.Right * 3;
        ThrowItem(shooter, RaceItem.Frostbolt);
        Require(shots.Single().Target == target, "Frostbolt locks a faster racer off the launch line");
        RunMovingTarget();
        Require(target.Frozen > 0, "Frostbolt closes on a 100 mph target from cruise speed");

        HoldItem(shooter, RaceItem.MudPie);
        UseItem(shooter);
        Vector3 center = mud.Single().Node.Position;
        Vector3 grazing = center + Vector3.Right * 1.32f;
        Vector3 halfStep = heading * (DriftSpeedCap / 120);
        Vector3 before = grazing - halfStep, after = grazing + halfStep;
        target.EjectTime = 1;
        CheckMudHits(target, before, after);
        Require(mud.Count == 1, "an ejected rider does not consume the mud hazard");
        target.EjectTime = 0;
        CheckMudHits(target, before, after);
        Require(mud.Count == 0 && target.Slowed > 0, "a grazing fast pass registers mud between physics samples");
        ResetRace();

        void RunMovingTarget()
        {
            const float step = 1f / 60;
            for (int frame = 0; frame < 132 && shots.Count > 0; frame++)
            {
                target.FreePosition = track.FreeGround(target.FreePosition + heading * (target.Speed * step));
                TickItemEffects(step);
            }
        }
    }

    private async Task CaptureRacePower(string folder)
    {
        ChooseCourse(0);
        StartRace();
        paused = true;
        countdown = 0;
        const float at = 77;
        float[] lanes = [0, -2.9f, 3.3f, -5.4f, 5.9f];
        for (int i = 0; i < racers.Count; i++)
        {
            racers[i].GatesCleared = 1;
            racers[i].Distance = at + i * 1.35f - (i == 0 ? 0 : .85f);
            racers[i].Lane = lanes[i];
            racers[i].Speed = 20;
        }
        HoldItem(Player, RaceItem.ZefirHex);
        UseItem(Player);
        HoldItem(Player, RaceItem.TuskerMight);
        UseItem(Player);
        for (int i = 0; i < racers.Count; i++)
        {
            TickRacerPowers(racers[i], .26f);
            racers[i].Draw(track, .30f + .06f * i, true);
        }
        TickItemEffects(.19f);
        Vector3 center = track.Point(at + 1.35f);
        Vector3 forward = track.Forward(at);
        camera.Position = center + forward * 12 + track.Right(at) * 5.8f + Vector3.Up * 5.1f;
        camera.Fov = 55;
        camera.LookAt(center + Vector3.Up * 1.45f);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Dereth-Race-Powers.png"));
        OpenStable();
    }

    private void CheckRacePowers()
    {
        ResetRace();
        countdown = 0;
        var player = Player;
        var rival = racers[1];
        float at = track.Gate(0) + 35;
        Vector3 origin = track.Point(at);
        Vector3 forward = track.Forward(at);
        foreach (var racer in racers)
        {
            racer.GatesCleared = 1;
            racer.Distance = at + (racer == player ? 0 : 80 + racers.IndexOf(racer) * 11);
            racer.Lane = 0;
            racer.Draw(track, 0, false);
        }

        HoldItem(player, RaceItem.FrostVolley);
        UseItem(player);
        Require(player.ItemCharges == 2, "Frost Volley keeps its remaining shots");
        UseItem(player);
        UseItem(player);
        Require(player.Item == RaceItem.None && shots.Count == 3 && shots.All(shot => shot.Target is null && shot.IceTrail is not null),
            "Frost Volley fires three straight shots with native ice trails");
        rival.FreeDriving = true;
        rival.FreePosition = track.FreeGround(origin + forward * 7);
        for (int frame = 0; frame < 25; frame++) TickItemEffects(1f / 60);
        Require(rival.Frozen > 0 && rival.SpinTime > 0, "a straight Frost Volley freezes and spins a rival");
        rival.FreeDriving = false;
        rival.Frozen = 0;
        ClearItemVisuals();

        HoldItem(player, RaceItem.PyrealSling);
        UseItem(player);
        for (int frame = 0; frame < 50; frame++) TickItemEffects(1f / 60);
        Require(powerProps.Count == 5 && powerTosses.Count == 0, "Pyreal Sling lands five collectable coins");
        rival.FreeDriving = true;
        rival.FreePosition = track.FreeGround(powerProps[0].Node.Position);
        TickItemEffects(1f / 60);
        Require(rival.Pyreals == 1 && powerProps.Count == 4 && SpeedLimit(rival, 20) > 20,
            "a rival can collect a slung Pyreal for a lasting pace increase");
        rival.FreeDriving = false;
        HoldItem(player, RaceItem.PyrealCache);
        for (int tap = 0; tap < 3; tap++) UseItem(player);
        Require(player.Pyreals == 3 && player.Boost > 0 && player.Item == RaceItem.None,
            "Pyreal Cache pays out a coin and a boost for each of three charges");
        ClearPowerProps();

        HoldItem(player, RaceItem.SmithHammer);
        UseItem(player);
        for (int frame = 0; frame < 50; frame++) TickItemEffects(1f / 60);
        Require(powerProps.Count == 1 && powerProps[0].Item == RaceItem.SmithHammer, "the hammer embeds as a timed track hazard");
        rival.FreeDriving = true;
        rival.FreePosition = track.FreeGround(powerProps[0].Node.Position);
        TickItemEffects(1f / 60);
        Require(rival.Slowed > 0 && powerProps.Count == 0, "the grounded hammer can hit a passing racer");
        rival.FreeDriving = false;
        rival.Slowed = rival.ItemGrace = 0;

        HoldItem(player, RaceItem.LeapingDraught);
        UseItem(player);
        TickRacerPowers(player, .43f);
        player.Draw(track, 0, false);
        Require(player.Root.Position.Y > origin.Y + 2 && !AcceptHit(player), "the leaping draught visibly dodges ground hazards");
        TickRacerPowers(player, .60f);
        Require(player.Leap == 0 && AcceptHit(player), "landing ends the jump protection");

        HoldItem(player, RaceItem.ZefirHex);
        UseItem(player);
        Require(rival.Hexed > 0 && rival.PowerForm?.Setup.Id == WhiteRabbitSetup && !rival.Mount.Visible && SpeedLimit(rival, 20) < 20,
            "Zefir Hex turns a rival into a slower native rabbit");
        TickRacerPowers(rival, 4);
        Require(rival.Hexed == 0 && rival.Mount.Visible && rival.PowerForm is null, "the hex restores the selected mount");

        rival.Distance = at + 1.6f;
        rival.Speed = 16;
        HoldItem(player, RaceItem.TuskerMight);
        UseItem(player);
        TickRacerPowers(player, .26f);
        TickPowerContacts();
        Require(player.Root.Scale.Y > 1.45f && rival.Speed < 12 && !AcceptHit(player),
            "Tusker Might enlarges the racer and bowls over nearby competitors");
        TickRacerPowers(player, 5);
        Require(player.Root.Scale.IsEqualApprox(Vector3.One), "Tusker Might restores normal size");

        HoldItem(player, RaceItem.Invulnerability);
        player.Frozen = 2;
        UseItem(player);
        Require(player.Frozen == 0 && player.Invulnerable > 4 && player.Boost > 4 && !AcceptHit(player),
            "Invulnerability clears slowing, boosts, and wards off attacks");
        TickRacerPowers(player, 5);
        Require(AcceptHit(player), "invulnerability expires");

        rival.ItemGrace = rival.Slowed = 0;
        ThrowItem(rival, RaceItem.FrostVolley);
        HoldItem(player, RaceItem.ForceBurst);
        UseItem(player);
        Require(shots.Count == 0 && rival.Slowed > 1, "Force Burst clears an incoming shot and knocks back a nearby racer");

        HoldItem(rival, RaceItem.FrostVolley);
        rival.ItemCharges = 2;
        HoldItem(player, RaceItem.WispThief);
        UseItem(player);
        Require(player.Phased > 0 && player.PowerForm?.Setup.Id == WispSetup && player.Item == RaceItem.FrostVolley
            && player.ItemCharges == 2 && rival.Item == RaceItem.None && !AcceptHit(player),
            "Wisp Thief shifts into a native wisp and transfers a rival's remaining item charges");
        TickRacerPowers(player, 3);
        Require(player.Mount.Visible && player.PowerForm is null && AcceptHit(player), "the wisp phase expires cleanly");

        ResetRace();
        Require(racers.All(racer => racer.Item == RaceItem.None && racer.Pyreals == 0 && racer.PowerForm is null)
            && powerProps.Count == 0 && powerTosses.Count == 0, "retry resets the new items and track props");
    }
}
