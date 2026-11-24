using Godot;
using DerethSurvivors;
using GromnieOnline;

public partial class GrandPrix
{
    private const float StateInterval = .05f, InputInterval = 1f / 30;
    private float onlineFrameTimer, onlineInputTimer;
    private readonly Dictionary<ulong, (int Kind, Node3D Node)> visibleOnlineProps = [];

    private MountedRacer CreateOnlineRacer(OnlineLoadout loadout)
    {
        var kind = (RaceMountKind)loadout.Mount;
        var mount = RaceMount.Get(Enum.IsDefined(kind) ? kind : RaceMountKind.Gromnie);
        var look = creation.Resolve(creation.Validate(loadout.Look, out _) ? loadout.Look : new CharacterAppearance());
        if (loadout.Imported is { } imported)
        {
            try { look = imported.Resolve(assets); }
            catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or KeyNotFoundException)
            { GD.PushWarning("An online rider uses their basic look on this DAT set."); }
        }
        int color = ClampMountColor(mount.Kind, loadout.Skin);
        return new MountedRacer(assets, mount, GromnieLook(mount.Kind == RaceMountKind.Gromnie ? color : 0), look,
            OnlineRiderName(loadout), RaceWeapon.All[Math.Clamp(loadout.Weapon, 0, RaceWeapon.All.Length - 1)],
            RaceShield.All[Math.Clamp(loadout.Shield, 0, RaceShield.All.Length - 1)], color,
            jetSki: track.IsWaterCourse, supportsJetSki: track.HasWaterSections);
    }

    private HumanPilot? RemotePilot(int index)
    {
        int peer = onlinePlan!.Slots[index].Peer;
        return peer > 1 && lobby!.Members.Any(member => member.Id == peer) ? remotePilots.GetValueOrDefault(peer) : null;
    }

    private static void ExpireInput(HumanPilot pilot)
    {
        if (Time.GetTicksMsec() - pilot.LastInput <= 1000) return;
        pilot.Held = pilot.Actions = 0;
        pilot.Steer = 0;
    }

    private void MarkRemoteStarts(bool go)
    {
        for (int i = 1; i < racers.Count; i++)
        {
            if (RemotePilot(i) is not { } pilot) continue;
            ExpireInput(pilot);
            bool gas = (pilot.Held & GasBit) != 0;
            if (go)
            {
                if (pilot.StartBoost && gas) GrantBoost(racers[i], 1.35f);
                pilot.Actions = 0;
            }
            else
            {
                if (countdown <= .68f && gas && !pilot.PreviousGas) pilot.StartBoost = true;
                pilot.PreviousGas = gas;
            }
        }
    }

    private float ApplyRemoteActions(MountedRacer racer, HumanPilot pilot)
    {
        ExpireInput(pilot);
        var (_, _, steer) = DrivingInputs(racer, false, false, pilot.Steer);
        int actions = pilot.Actions;
        pilot.Actions = 0;
        if ((actions & GuardBit) != 0) TryGuard(racer);
        if ((actions & LeftBit) != 0) TrySwipe(racer, -1);
        else if ((actions & RightBit) != 0) TrySwipe(racer, 1);
        if ((actions & ItemBit) != 0 && racer.Flight is null && racer.ResetHold <= 0 && racer.EjectTime <= 0)
            UseItem(racer);
        if ((actions & RollBit) != 0 && racer.Flight is { } flight) flight.TryRoll(steer);
        if (racer.Flight is not null || racer.EjectTime > 0 || racer.ResetHold > 0)
        {
            pilot.Lateral = pilot.Charge = 0;
            pilot.Sliding = false;
        }
        return steer;
    }

    private bool MoveRemote(MountedRacer rider, HumanPilot pilot, float dt, float steer)
    {
        var (gas, brake, _) = DrivingInputs(rider, (pilot.Held & GasBit) != 0, (pilot.Held & BrakeBit) != 0, 0);
        bool sliding = WantsGroundDrift(rider, (pilot.Held & DriftBit) != 0, steer, pilot.Sliding);
        if (sliding && !rider.SnowboardAirborne) pilot.Charge = Math.Min(1.5f, pilot.Charge + dt);
        else if (pilot.Sliding)
        {
            ReleaseDriftBoost(rider, pilot.Charge);
            pilot.Charge = 0;
        }
        pilot.Sliding = sliding;
        MoveHuman(rider, ref pilot.Lateral, dt, gas, brake, steer, sliding);
        return sliding;
    }

    private void SendOnlineState(float dt)
    {
        if (!OnlineHost || onlinePlan is not { } plan) return;
        onlineFrameTimer += dt;
        if (!finished && onlineFrameTimer < StateInterval) return;
        onlineFrameTimer = 0;
        var state = new OnlineState(plan.Epoch, ++onlineFrame, Math.Max(0, countdown), elapsed, finished,
            racers.Select(RacerState).ToArray(), pickups.Where(pickup => pickup.ReadyAt > elapsed).Select(pickup => pickup.Index).ToArray(),
            onlineWaves.ToArray(), OnlineProps(), track.IsVoidBattle ? CurrentVoidBattlePattern() : 0);
        onlineWaves.Clear();
        SendOnline(Wire.Packet("state", state));
    }

    private OnlineRacer RacerState(MountedRacer racer, int index)
    {
        var flight = racer.Flight is { } air ? new OnlineFlight(air.Flyer.SeatsRider ? 2 : 1,
            new(air.Position), BogCircuit.Yaw(air.Heading), air.Age, air.Pitch, air.Bank, air.RollAngle, air.RollCooldown) : null;
        var eject = racer.EjectTime > 0 ? new OnlineEjection(new(racer.Rider.GlobalPosition), new(racer.Rider.GlobalRotation),
            racer.Rider.GlobalBasis.Scale.Y) : null;
        var board = racer.HasSnowboardPhysics ? new OnlineSnowboard(new(racer.SnowboardPosition), new(racer.SnowboardVelocity),
            new(racer.SnowboardHeading), racer.SnowboardPitch, racer.SnowboardRoll, racer.SnowboardAirborne) : null;
        return new(racer.Distance, racer.Lane, racer.Speed, racer.Lean, racer.Finish, racer.Checkpoint, racer.GatesCleared,
            racer.Stability, (int)racer.Item, racer.ItemCharges, racer.Pyreals, racer.Boost, racer.Frozen, racer.Ward,
            racer.GuardWindow, racer.GuardCooldown, racer.ResetHold, racer.Leap, racer.SpinTime, racer.SwingTime, racer.SwingSide,
            racer.Might, racer.Phased > 0 ? 2 : racer.Hexed > 0 ? 1 : 0, racer.FreeDriving, new(racer.FreePosition),
            racer.FreeYaw, index == 0 ? charge : RemotePilot(index)?.Charge ?? 0, IsSudsed(racer), flight, eject, racer.FallTime,
            racer.GroundDrifting, HasSuperDrift(racer) ? racer.SuperDriftUntil - elapsed : 0,
            racer.LapCrossings.Count == 0 ? null : racer.LapCrossings.ToArray(),
            racer.BoostStrain, racer.MountRecovery, racer.RemountTime, racer.OnFootToFinish,
            racer.HasSkiWaterState ? new OnlineSki(racer.SkiHeave, racer.SkiPitch, racer.SkiRoll, racer.SkiSternContact, racer.SkiAirborne) : null,
            board);
    }

    private void TickOnlineGuest(float dt)
    {
        if (!finished && Player.Finish < 0) SendGuestControls(dt);
        if (!racing) return;
        if (pendingOnlineState is { } state)
        {
            pendingOnlineState = null;
            ApplyOnlineState(state);
        }
        if (finished) return;
        if (track.HasWaterSections)
        {
            TickRegattaFluid(dt);
            UpdateRegattaSurface();
        }
        float weight = 1 - Mathf.Exp(-24 * dt);
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            Vector3 previous = racer.Root.Position;
            float oldYaw = racer.Root.Rotation.Y;
            racer.Draw(track, dt, !racer.Falling && racer.Speed > .4f, racer.UsesJetSki ? racer.Lean : 0);
            float blendRange = Math.Max(9, racer.Speed * .14f);
            if (racer.ResetHold <= 0 && previous.DistanceSquaredTo(racer.Root.Position) < blendRange * blendRange)
            {
                Vector3 position = previous.Lerp(racer.Root.Position, weight);
                if (racer.HasSnowboardPhysics && !racer.SnowboardAirborne)
                    position.Y = track.FreeGround(position).Y + .06f;
                racer.Root.Position = position;
                var angles = racer.Root.Rotation;
                angles.Y = Mathf.LerpAngle(oldYaw, angles.Y, weight);
                racer.Root.Rotation = angles;
            }
            racer.PowerForm?.Tick(dt, racer.Hexed > 0 && racer.Speed > .4f, Vector3.Zero);
            if (!racer.UsesJetSki && !racer.UsesSnowboard && (!YanshiPerformanceProfile || racer == Player) && !racer.Falling && racer.Flight is null &&
                racer.LeapHeight <= .10f && racer.PowerForm is null && !racer.Rider.TopLevel)
                dust.SampleFeet(racer, track, racer.GroundDrifting, superDrift: HasSuperDrift(racer));
        }
        notification = Math.Max(0, notification - dt);
        TickRoadside(dt);
        TickTrackWeather(dt);
        if (track.IsVoidBattle) TickVoidBattle(dt);
        UpdatePickupVisuals(elapsed);
        TickSnowLayer(dt);
        dust.Tick(dt);
        TickItemEffects(dt);
        raceAudio?.Tick();
        SetRaceCamera(1 - Mathf.Exp(-8.2f * dt));
        UpdateSpanReflection();
        UpdateSpeedBlur(dt);
        UpdateRaceUi();
    }

    private void SendGuestControls(float dt)
    {
        onlineInputTimer += dt;
        int actions = (useItem ? ItemBit : 0) | (pendingSwipe < 0 ? LeftBit : 0) | (pendingSwipe > 0 ? RightBit : 0) |
            (requestGuard ? GuardBit : 0) | (requestRoll ? RollBit : 0);
        if (onlineInputTimer < InputInterval && actions == 0) return;
        onlineInputTimer = 0;
        int held = paused ? (Player.Flight is null ? BrakeBit : 0) : (Has(Key.W, Key.Up) ? GasBit : 0) | (Has(Key.S, Key.Down) ? BrakeBit : 0) |
            (IsHeld(Key.Space) ? DriftBit : 0);
        if (FreeFlightControls) held |= FreeFlightBit;
        if (!paused && (held & GasBit) == 0 && Player.Flight is { } flight) flight.DiveReady = true;
        float steer = paused ? 0 : LocalSteering;
        useItem = requestGuard = requestRoll = false;
        pendingSwipe = 0;
        SendOnline(Wire.Packet("input", new OnlineInput(onlinePlan!.Epoch, ++onlineInputSequence, Math.Sign(steer), held, paused ? 0 : actions, steer)));
    }

    private void ApplyOnlineState(OnlineState state)
    {
        if (!float.IsFinite(state.Time) || state.Time < 0 || !float.IsFinite(state.Countdown) || state.Countdown is < 0 or > 3)
            return;
        onlineFrame = state.Frame;
        countdown = state.Countdown;
        elapsed = state.Time;
        for (int i = 0; i < racers.Count; i++)
        {
            var row = state.Racers[i];
            if (row is null || !float.IsFinite(row.Distance) || Math.Abs(row.Distance) > track.Length * (track.Laps + 2) + 100 ||
                !float.IsFinite(row.Lane) || Math.Abs(row.Lane) > track.NativeBounds.Size.X + 100 ||
                !float.IsFinite(row.Speed) || row.Speed is < 0 or > 150 ||
                row.Gates < 0 || row.Gates > track.GateCount * track.Laps + 1) continue;
            ApplyOnlineRacer(racers[i], row);
        }
        charge = state.Racers[localRacerIndex]?.Drift ?? 0;
        place = RacerPlace(Player);
        lap = Mathf.Clamp((Player.GatesCleared - 1) / track.GateCount + 1, 1, track.Laps);
        if (track.IsVoidBattle)
        {
            ApplyVoidBattlePattern(state.BattlePattern);
            if (!finished) UpdateVoidRaceProgress();
        }
        HighlightCheckpoint();
        var hidden = state.HiddenPickups.Take(pickups.Count).ToHashSet();
        foreach (var pickup in pickups) pickup.ReadyAt = hidden.Contains(pickup.Index) ? float.MaxValue : 0;
        foreach (var wave in state.Waves.Take(64))
            if (wave is not null && wave.Radius is > 0 and <= 30 && wave.Duration is > 0 and <= 4 && wave.Position.Vector.IsFinite())
                Wave(wave.Position.Vector, new Color(wave.Tint), wave.Radius, wave.Duration, wave.Tilt.Vector);
        SyncOnlineProps(state.Props);
        if (state.Complete && !finished)
        {
            finished = true;
            if (paused) SetPaused(false);
            ShowResults(false);
        }
    }

    private void ApplyOnlineRacer(MountedRacer racer, OnlineRacer row)
    {
        bool beganFootLeg = track.IsDismountSprint && row.OnFootToFinish && !racer.OnFootToFinish;
        racer.OnFootToFinish = track.IsDismountSprint && row.OnFootToFinish;
        OnlineFlightPose(racer, racer.OnFootToFinish ? null : row.Flight);
        if (track.IsJester && racer == Player && row.Distance - racer.Distance > track.Length * .07f)
            jesterCameraSnap = true;
        racer.Distance = row.Distance;
        racer.Lane = row.Lane;
        racer.Speed = row.Speed;
        racer.Lean = Mathf.Clamp(row.Lean, -1, 1);
        if (row.Gates == track.GateCount * track.Laps + 1 && row.Finish >= 0 && row.Finish <= elapsed)
            racer.Finish = row.Finish;
        racer.Checkpoint = row.Checkpoint;
        racer.GatesCleared = row.Gates;
        if (row.LapCrossings is { } times && times.Length == Math.Max(0, (row.Gates - 1) / track.GateCount))
        {
            for (int i = racer.LapCrossings.Count; i < times.Length; i++)
            {
                float last = i > 0 ? racer.LapCrossings[i - 1] : 0;
                if (!(times[i] > last && times[i] <= elapsed)) break;
                racer.LapCrossings.Add(times[i]);
            }
        }
        racer.Stability = Math.Clamp(row.Stability, 0, 100);
        racer.Item = Enum.IsDefined((RaceItem)row.Item) ? (RaceItem)row.Item : RaceItem.None;
        racer.ItemCharges = row.Charges;
        racer.Pyreals = row.Pyreals;
        racer.Boost = row.Boost;
        racer.BoostStrain = float.IsFinite(row.BoostStrain) ? Mathf.Clamp(row.BoostStrain, 0, MountStrainLimit) : 0;
        bool wasOnWaterFoot = racer.UsesJetSki && racer.MountRecovery > 0;
        float recoveryDuration = racer.UsesJetSki ? MountedRacer.JetSkiRecoveryDuration : MountRecoveryDuration;
        racer.MountRecovery = float.IsFinite(row.MountRecovery) ? Mathf.Clamp(row.MountRecovery, 0, recoveryDuration) : 0;
        racer.RemountTime = float.IsFinite(row.RemountTime) ? Mathf.Clamp(row.RemountTime, 0, MountedRacer.RemountDuration) : 0;
        racer.GroundDrifting = row.GroundDrift;
        racer.SuperDriftUntil = float.IsFinite(row.SuperDrift) && row.SuperDrift > 0 ? elapsed + Math.Min(1.45f, row.SuperDrift) : 0;
        racer.Frozen = row.Frost;
        racer.Ward = row.Ward;
        racer.GuardWindow = row.Guard;
        racer.GuardCooldown = row.ShieldRecharge;
        racer.ResetHold = row.Reset;
        racer.Leap = row.Leap;
        racer.SpinTime = row.Spin;
        racer.SwingTime = row.Swing;
        racer.SwingSide = Math.Clamp(row.SwingSide, -1, 1);
        racer.Might = row.Might;
        racer.Root.Scale = Vector3.One * (row.Might > 0 ? 1.52f : 1);
        racer.FreeDriving = row.OffRoad && row.Position.Vector.IsFinite();
        racer.FallTime = racer.FreeDriving && float.IsFinite(row.Fall) ? Mathf.Clamp(row.Fall, 0, RacePhysics.TrackFallRescueTime) : 0;
        racer.FreePosition = row.Position.Vector;
        racer.FreeYaw = row.Yaw;
        racer.SudsedUntil = row.Sudsed ? elapsed + 1 : 0;
        int form = row.Form is 1 or 2 ? row.Form : 0;
        int oldForm = racer.Phased > 0 ? 2 : racer.Hexed > 0 ? 1 : 0;
        if (oldForm != form)
        {
            RemovePowerForm(racer);
            if (form == 1)
            {
                var rabbit = assets.CreateActor(WhiteRabbitSetup, WhiteRabbitMotion, 2.4f);
                rabbit.ApplyNativeAppearance(RabbitWhiteFur);
                SetPowerForm(racer, rabbit);
            }
            else if (form == 2)
            {
                var wisp = assets.CreateActor(WispSetup, WispMotion, .9f);
                wisp.Position = Vector3.Up * 1.05f;
                SetPowerForm(racer, wisp);
            }
        }
        racer.Phased = form == 2 ? 1 : 0;
        racer.Hexed = form == 1 ? 1 : 0;
        Transform3D? ejection = row.Ejected is { } eject && eject.Position.Vector.IsFinite() && eject.Rotation.Vector.IsFinite() && float.IsFinite(eject.Scale)
            ? new Transform3D(Basis.FromEuler(eject.Rotation.Vector).Scaled(Vector3.One * Mathf.Clamp(eject.Scale, .05f, 10)),
                eject.Position.Vector) : null;
        if (beganFootLeg)
        {
            if (ejection is not null) racer.PrepareReplicaDismountThrow();
            NotifyDismountSprint(racer);
        }
        racer.ShowReplicaEjection(ejection);
        SyncHybridTravel(racer, replica: true);
        if (racer.UsesSnowboard)
        {
            if (row.Board is { } board && board.Position.Vector.IsFinite() && board.Velocity.Vector.IsFinite() &&
                board.Heading.Vector.IsFinite() && float.IsFinite(board.Pitch) && float.IsFinite(board.Roll) &&
                track.NativeBounds.Grow(20).HasPoint(new Vector2(board.Position.X, board.Position.Z)))
                racer.ApplyReplicaSnowboardPhysics(board.Position.Vector, board.Velocity.Vector, board.Heading.Vector,
                    board.Pitch, board.Roll, board.Airborne);
            else racer.ResetSnowboardPhysics();
        }
        if (racer.UsesJetSki)
        {
            if (!wasOnWaterFoot && racer.MountRecovery > 0) NotifyJesusMode(racer);
            if (row.Ski is { } ski)
            {
                float impact = !racer.MountUnavailable && racer.SkiAirborne && !ski.Airborne && !racer.Rider.TopLevel && racer.ResetHold <= 0
                    ? (racer.SkiHeave - ski.Heave) / StateInterval : 0;
                racer.ApplyReplicaJetSkiWater(ski.Heave, ski.Pitch, ski.Roll, ski.Contact, ski.Airborne, RegattaWaterTime, regattaFluid);
                if (impact > 2 && float.IsFinite(impact)) ApplyJetSkiLanding(racer, racer.CoursePosition(track), impact);
            }
            else racer.StepJetSkiWater(racer.CoursePosition(track), racer.Heading(track), RegattaWaterTime, StateInterval, racer.Lean, racer.GroundDrifting, regattaFluid);
        }
    }

    private void OnlineFlightPose(MountedRacer racer, OnlineFlight? state)
    {
        if (state is not { Kind: 1 or 2 } || !state.Position.Vector.IsFinite())
        {
            racer.Land();
            return;
        }
        bool olthoi = state.Kind == 2 && creation.OlthoiSpitter is not null;
        if (racer.Flight is not { } flight || flight.Flyer.SeatsRider != olthoi)
        {
            racer.Land();
            if (!olthoi) racer.PrepareFallingPose();
            FlightMount model = olthoi ? new OlthoiSpitter(assets, creation.OlthoiSpitter!) : new Gromnatross(assets, skins[2]);
            flight = new FlightRide(model, 0, 1, 0, 0, 0);
            racer.TakeFlight(flight);
        }
        flight.Position = state.Position.Vector;
        flight.Heading = Vector3.Forward.Rotated(Vector3.Up, state.Yaw);
        flight.Age = state.Age;
        flight.Pitch = state.Pitch;
        flight.Bank = state.Bank;
        flight.SetReplicaRoll(state.Roll, state.Recharge);
    }

    private OnlineProp[] OnlineProps()
    {
        var props = new List<OnlineProp>();
        foreach (var shot in shots)
            Add(shot.Node, shot.Kind == RaceItem.TuskerPus ? 2 : 1, shot.Node.Rotation.Y);
        foreach (var shot in sideShots)
            Add(shot.Node, shot.Impact == ShieldImpact.Spell ? 3 : 4, shot.Node.Rotation.Y);
        foreach (var rabbit in rabbits)
            Add(rabbit.Root, 5, rabbit.Root.Rotation.Y);
        return props.Take(64).ToArray();

        void Add(Node3D node, int kind, float yaw) =>
            props.Add(new OnlineProp(node.GetInstanceId(), kind, new OnlinePoint(node.GlobalPosition), yaw));
    }

    private void SyncOnlineProps(OnlineProp[] props)
    {
        var visible = new HashSet<ulong>();
        foreach (var prop in props.Take(64))
        {
            if (prop is null || prop.Kind is < 1 or > 5 || !prop.Position.Vector.IsFinite()) continue;
            visible.Add(prop.Id);
            if (!visibleOnlineProps.TryGetValue(prop.Id, out var entry) || entry.Kind != prop.Kind)
            {
                entry.Node?.QueueFree();
                uint setup = prop.Kind switch
                {
                    1 => AcCatalog.Projectiles["frost"].Setup,
                    2 => AcCatalog.Projectiles["acid"].Setup,
                    3 => AcCatalog.Projectiles["lightning"].Setup,
                    4 => RaceWeapon.All[5].Setup,
                    _ => WhiteRabbitSetup
                };
                var actor = assets.CreateActor(setup, prop.Kind == 5 ? WhiteRabbitMotion : 0, prop.Kind == 5 ? 1.2f : .72f);
                if (prop.Kind == 5) actor.ApplyNativeAppearance(RabbitWhiteFur);
                itemFxRoot.AddChild(actor);
                if (prop.Kind is 1 or 3)
                    BogCircuit.Box(actor, new Vector3(0, 0, .32f), new Vector3(.09f, .09f, 1.4f),
                        Glow(new Color(prop.Kind == 1 ? "8ceaff" : "a9edff"))).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                visibleOnlineProps[prop.Id] = entry = (prop.Kind, actor);
            }
            entry.Node.Position = prop.Position.Vector;
            entry.Node.Rotation = new Vector3(prop.Kind == 4 ? 1.25f : 0, prop.Yaw, 0);
            if (prop.Kind == 5 && entry.Node is AcActor rabbit) rabbit.Tick(StateInterval, true, Vector3.Zero);
        }
        foreach (ulong id in visibleOnlineProps.Keys.Where(id => !visible.Contains(id)).ToArray())
        {
            visibleOnlineProps[id].Node.QueueFree();
            visibleOnlineProps.Remove(id);
        }
    }

    private void ClearOnlineProps()
    {
        foreach (var entry in visibleOnlineProps.Values)
            if (GodotObject.IsInstanceValid(entry.Node)) entry.Node.QueueFree();
        visibleOnlineProps.Clear();
    }
}
