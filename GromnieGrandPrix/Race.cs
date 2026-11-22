using Godot;

public partial class GrandPrix
{
    private const int RacerCount = 5;
    private readonly BogCircuit muckTrack = new();
    private BogCircuit track = null!;
    private Node3D courseItems = null!;
    private RaceDust dust = null!;
    private readonly List<MountedRacer> racers = [];
    private readonly List<(Node3D Node, float Expires, MountedRacer Owner)> mud = [];
    private readonly List<(float Distance, float Lane)> pads = [];
    private int pendingSwipe;
    private bool startBoostReady, previousGas;
    private float elapsed, countdown, lateral, charge, pickupLock, notification;
    private float finishDeadline;
    private int place = RacerCount, lap = 1;
    private bool drifting, racing, paused, finished;
    private string message = "";
    private MountedRacer Player => racers[localRacerIndex];

    private void BuildCourseItems()
    {
        pads.Clear();
        courseItems = new Node3D { Name = "Race pickups and pads" };
        course.AddChild(courseItems);
        ConfigureItemVisuals();
        dust = new RaceDust(courseItems, assets);
        BuildCheckpointBeacons();
        BuildPickupRows();
        BuildFlyover();
        BuildCitadelBattle();
        BuildRoadside();
        var padBase = track.IsVoid ? Glow(new Color("8c285c")) : BogCircuit.Paint("d87d32");
        var padStripe = track.IsVoid ? Glow(new Color("ffd0f3")) : BogCircuit.Paint("ffe098");
        int boostPads = track.IsRegional ? Mathf.CeilToInt(track.Length / 320) : 3;
        for (int i = 0; i < boostPads; i++)
        {
            float at = BogCircuit.Grid + 58 + i * track.Length / boostPads;
            if (track.IsSpan(at) || track.IsSprint && at >= track.Gate(track.GateCount) - 20) continue;
            float lane = track.WidthAt(at) * .47f * (i % 2 == 1 ? -1 : 1);
            pads.Add((at, lane));
            track.Ribbon(courseItems, lane - 1.14f, lane + 1.14f, .052f, padBase, at, at + 3.6f);
            for (int stripe = 0; stripe < 3; stripe++)
                track.Ribbon(courseItems, lane - .98f, lane + .98f, .056f, padStripe, at + stripe * 1.1f + .38f, at + stripe * 1.1f + .66f);
        }
    }

    private void ResetRace()
    {
        ClearItemVisuals();
        ResetCitadelBattle();
        raceAudio?.Clear();
        ClearSpeedBlur();
        dust.Clear();
        foreach (var pickup in pickups) pickup.ReadyAt = 0;
        UpdatePickupVisuals(0);
        pendingSwipe = 0;
        requestRoll = false;
        requestGuard = false;
        startBoostReady = previousGas = false;
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            int gridPlace = OnlineRace ? i : i == 0 ? RacerCount - 1 : i - 1;
            racer.Distance = BogCircuit.Grid - 3.1f - (gridPlace / 3) * 5.6f;
            float laneFraction = gridPlace < 3 ? (gridPlace - 1) * .65f : (gridPlace == 3 ? -.34f : .34f);
            racer.Lane = track.HalfWidth * laneFraction;
            racer.Speed = racer.Boost = racer.Slowed = racer.Lean = 0;
            racer.SuperDriftUntil = 0;
            racer.GroundDrifting = false;
            racer.NextBoostSound = 0;
            racer.Checkpoint = racer.Distance;
            racer.GatesCleared = 0;
            racer.LapCrossings.Clear();
            racer.FreeDriving = false;
            racer.FallTime = 0;
            Array.Clear(racer.DustContact);
            racer.Stability = 100;
            racer.Land();
            racer.Reattach();
            ClearRacerPowers(racer);
            racer.Frozen = racer.ResetHold = racer.ItemGrace = racer.Ward = racer.SwingTime = racer.SwipeCooldown = 0;
            racer.GuardWindow = racer.GuardCooldown = racer.GuardFlash = 0;
            racer.Finish = -1;
            HoldItem(racer, RaceItem.None);
            racer.Pyreals = 0;
            racer.Draw(track, 0, false);
        }
        elapsed = lateral = charge = pickupLock = notification = 0;
        ResetRivalDrivers();
        ResetRoadside();
        place = RacerCount;
        lap = 1;
        ResetVoidProgress();
        voidGalaxyLap = 0;
        finishDeadline = 0;
        countdown = 3;
        drifting = paused = finished = false;
        racing = true;
        raceTouchUi?.Show();
        message = "";
        if (track.IsVoid)
        {
            ResetVoidFinaleVisuals();
            SetVoidGalaxyProgress(0);
            ResetVoidBattle();
        }
        PlayMusic(RaceMusic(), restart: true);
        HighlightCheckpoint();
    }

    private void TickRace(float dt, bool gas, bool brake, float steer, bool drift, bool useItem)
    {
        TickTrackWeather(dt);
        if (countdown > 0)
        {
            TickRoadside(dt);
            if (track.IsVoid) TickVoidBattle(dt);
            if (countdown <= .68f && gas && !previousGas) startBoostReady = true;
            previousGas = gas;
            if (OnlineHost) MarkRemoteStarts(false);
            countdown -= dt;
            if (countdown <= 0)
            {
                if (startBoostReady && gas) GrantBoost(Player, 1.35f);
                if (OnlineHost) MarkRemoteStarts(true);
                Callout(Player.Boost > 0 ? "ROCKET START!" : "GO!", .75f);
            }
            foreach (var racer in racers) racer.Draw(track, dt, false);
            return;
        }

        elapsed += dt;
        TickShields(dt);
        if (track.IsVoid) TickVoidBattle(dt);
        TickRoadside(dt);
        TickCitadelBattle(dt);
        notification = Math.Max(notification - dt, 0);
        pickupLock = Math.Max(pickupLock - dt, 0);
        bool localActive = Player.Finish < 0;
        if (localActive && useItem && !Player.Falling && Player.Flight is null && Player.ResetHold <= 0 && Player.EjectTime <= 0) UseItem(Player);
        if (localActive && pendingSwipe != 0 && !Player.Falling) TrySwipe(Player, pendingSwipe);
        pendingSwipe = 0;

        (gas, brake, steer) = DrivingInputs(Player, gas, brake, steer);
        if (localActive && requestRoll && Player.Flight is { } ride) ride.TryRoll(steer);
        requestRoll = false;
        bool sliding = localActive && WantsGroundDrift(Player, drift, steer, drifting);
        if (sliding) charge = Math.Min(charge + dt, 1.5f);
        if (!sliding && drifting)
        {
            if (localActive) ReleaseDriftBoost(Player, charge);
            charge = 0;
        }
        drifting = sliding;
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            racer.GroundDrifting = false;
            if (racer.Finish >= 0) continue;
            var remote = OnlineHost ? RemotePilot(i) : null;
            if (racer.Falling)
            {
                if (remote is not null)
                {
                    remote.Actions = 0;
                    remote.Lateral = remote.Charge = 0;
                    remote.Sliding = false;
                }
                TickTrackFall(racer, dt);
                continue;
            }
            var steeringInput = remote is null ? 0 : ApplyRemoteActions(racer, remote);
            TickRacerPowers(racer, dt);
            racer.ItemGrace = Math.Max(0, racer.ItemGrace - dt);
            racer.Ward = Math.Max(0, racer.Ward - dt);
            racer.Frozen = Math.Max(0, racer.Frozen - dt);
            if (racer.Flight is { } flight)
            {
                racer.Slowed = Math.Max(0, racer.Slowed - dt);
                if (i == 0) TickFlight(racer, flight, dt, steer, FreeFlightControls, !paused && gas, !paused && brake);
                else if (remote is not null)
                {
                    var (dive, climb, _) = DrivingInputs(racer, (remote.Held & GasBit) != 0, (remote.Held & BrakeBit) != 0, 0);
                    TickFlight(racer, flight, dt, steeringInput, (remote.Held & FreeFlightBit) != 0, dive, climb);
                }
                else TickFlight(racer, flight, dt, CitadelRivalBank(racer, flight, i));
                continue;
            }
            if (racer.EjectTime > 0)
            {
                racer.EjectTime = Math.Max(0, racer.EjectTime - dt);
                if (racer.EjectTime > 0)
                {
                    racer.Draw(track, dt, false);
                    racer.Tumble(track);
                }
                else RecallRacer(racer, "BACK IN THE SADDLE");
                continue;
            }
            if (racer.ResetHold > 0)
            {
                racer.ResetHold = Math.Max(0, racer.ResetHold - dt);
                racer.Speed = 0;
                racer.Draw(track, dt, false);
                continue;
            }
            TickRacerSwipe(racer, dt);
            Vector3 from = racer.CoursePosition(track);
            float before = racer.Distance;
            bool driftingNow = sliding;
            if (i == 0)
            {
                MovePlayer(dt, gas, brake, steer, sliding);
                if (!OnlineRace) ObservePlayerLine(gas, brake);
            }
            else if (remote is not null) driftingNow = MoveRemote(racer, remote, dt, steeringInput);
            else driftingNow = MoveRival(racer, i, dt);
            if (racer.Flight is not null || racer.Falling) continue;
            racer.Boost = Math.Max(0, racer.Boost - dt);
            racer.Slowed = Math.Max(0, racer.Slowed - dt);
            if (!racer.FreeDriving) CheckCourse(racer, before, i);
            Vector3 after = racer.CoursePosition(track);
            RecordGate(racer, from, after, dt);
            CheckRoadHazards(racer, i, from, after);
            CheckEncounterHits(racer, i, from, after);
            if (track.IsVoid) CheckVoidBattleHits(racer, i, from, after);
            if (racer.EjectTime > 0) continue;
            CheckMudHits(racer, from, after);
            racer.GroundDrifting = driftingNow;
            float steering = (i == 0 ? lateral : remote?.Lateral ?? rivalDrivers[i - 1].LaneVelocity) / (7 * SteeringPace(racer.Speed)) *
                (driftingNow ? 1 : .52f);
            racer.Draw(track, dt, racer.Speed > .4f, steering);
            if ((!YanshiPerformanceProfile || racer == Player) && racer.Flight is null && racer.LeapHeight <= .10f && racer.PowerForm is null)
                dust.SampleFeet(racer, track, driftingNow, superDrift: HasSuperDrift(racer));
        }

        for (int i = mud.Count - 1; i >= 0; i--)
        {
            var hazard = mud[i];
            if (hazard.Expires > elapsed) continue;
            hazard.Node.QueueFree();
            mud.RemoveAt(i);
        }

        UpdatePickupVisuals(elapsed);
        dust.Tick(dt);
        TickItemEffects(dt);
        raceAudio?.Tick();
        place = RacerPlace(Player);
        int nextLap = Mathf.Clamp((Player.GatesCleared - 1) / track.GateCount + 1, 1, track.Laps);
        if (nextLap > lap) Callout(nextLap == track.Laps ? "FINAL LAP!" : $"LAP {nextLap}", 1.6f);
        lap = nextLap;
        if (track.IsVoid) UpdateVoidRaceProgress();
        if (RaceComplete())
        {
            finished = true;
            if (paused) SetPaused(false);
            bool record = !OnlineRace && editorPreview is null && (best < 0 || Player.Finish < best);
            if (record) { best = Player.Finish; bestByCourse[activeTown] = best; }
            if (record || rivalLines.Dirty) SaveGarage();
            ShowResults(record);
        }
    }

    private bool RaceComplete()
    {
        if (!track.IsVoid && !OnlineRace) return Player.Finish >= 0;

        bool waiting = false;
        for (int i = 0; i < racers.Count; i++)
        {
            bool human = i == 0 || OnlineRace && RemotePilot(i) is not null;
            if (!human && !track.IsVoid) continue;
            var racer = racers[i];
            if (racer.Finish < 0) waiting = true;
            else if (human && finishDeadline == 0)
                finishDeadline = elapsed + (track.IsVoid ? Math.Max(60, track.Length / OffRoadPace) : 60);
        }
        return !waiting || finishDeadline > 0 && elapsed >= finishDeadline;
    }

    private int RacerPlace(MountedRacer racer) => 1 + racers.Count(other => other != racer && (racer.Finish >= 0
        ? other.Finish >= 0 && other.Finish < racer.Finish
        : other.Finish >= 0 || RaceProgress(other) > RaceProgress(racer)));

    private bool HasSuperDrift(MountedRacer racer) => racer.Boost > 0 && racer.SuperDriftUntil > elapsed;

    private static bool WantsGroundDrift(MountedRacer racer, bool held, float steer, bool sliding) =>
        held && (sliding || Math.Abs(steer) > .1f) && racer.Speed > 8 && !racer.Falling && racer.Flight is null &&
        racer.EjectTime <= 0 && racer.ResetHold <= 0;

    private void ReleaseDriftBoost(MountedRacer racer, float driftCharge, float superThreshold = 1.18f, float superDuration = 1.45f)
    {
        if (driftCharge <= .58f || racer.Flight is not null || racer.Falling || racer.EjectTime > 0 || racer.ResetHold > 0) return;
        bool super = driftCharge > superThreshold;
        if (super) racer.SuperDriftUntil = elapsed + superDuration;
        GrantBoost(racer, super ? superDuration : .82f);
        if (racer == Player) Callout(super ? "SUPER DRIFT BOOST!" : "DRIFT BOOST!");
    }

    private float SpeedLimit(MountedRacer racer, float normal)
    {
        float pace = normal + racer.Pyreals * .20f;
        if (racer.Slowed > 0 || racer.Frozen > 0) return pace * .54f;
        if (racer.Hexed > 0) return pace * .76f;
        if (racer.Boost <= 0) return pace;
        float cap = HasSuperDrift(racer) ? DriftSpeedCap : BoostSpeedCap;
        return Math.Min(pace + (cap - RoadPace), cap);
    }

    private void CheckCourse(MountedRacer racer, float before, int index)
    {
        if (TryFlyover(racer, before)) return;
        foreach (var pad in pads)
            if (Crossed(before, racer.Distance, pad.Distance) && Mathf.Abs(racer.Lane - pad.Lane) < 1.24f)
            {
                GrantBoost(racer, 1.14f);
                if (racer == Player) Callout("BOOST PAD!");
            }

        foreach (var pickup in pickups)
        {
            if (!pickup.Available || !Crossed(before, racer.Distance, pickup.Distance) || Mathf.Abs(racer.Lane - pickup.Lane) > 1.12f) continue;
            if (index == 0 && pickupLock == 0 && racer.Item == RaceItem.None)
            {
                HoldItem(racer, pickup.Displayed);
                pickupLock = .4f;
                pickup.ReadyAt = elapsed + 3.2f;
                pickup.SetAvailable(false);
                Callout(ItemName(racer.Item).ToUpperInvariant() + " READY");
            }
            else if (index > 0 && racer.Item == RaceItem.None)
            {
                HoldItem(racer, pickup.Displayed);
                racer.UseHeldItemAt = elapsed + .74f + rivalDrivers[index - 1].Style.Reaction;
                rivalDrivers[index - 1].ItemSince = elapsed;
                pickup.ReadyAt = elapsed + 3.2f;
                pickup.SetAvailable(false);
            }
            break;
        }
    }

    private bool Crossed(float before, float after, float milestone) =>
        track.IsSprint ? before < milestone && after >= milestone
            : Mathf.Floor((after - milestone) / track.Length) > Mathf.Floor((before - milestone) / track.Length);

    private static void HoldItem(MountedRacer racer, RaceItem item)
    {
        racer.Item = item;
        racer.ItemCharges = RacePower.Find(item)?.Charges ?? (item == RaceItem.None ? 0 : 1);
        racer.UseHeldItemAt = 0;
    }

    private void UseItem(MountedRacer racer)
    {
        var item = racer.Item;
        if (item == RaceItem.None) return;
        if (item is RaceItem.Gromnatross or RaceItem.OlthoiLift)
        {
            if (LaunchFlight(racer, olthoi: item == RaceItem.OlthoiLift)) HoldItem(racer, RaceItem.None);
            else if (racer == Player) Callout(racer.FreeDriving ? "REJOIN THE ROAD TO TAKE OFF" : "CAN'T TAKE OFF HERE", 1.25f);
            return;
        }
        if (item == RaceItem.UlgrimFolly)
        {
            if (PourUlgrimFolly(racer)) HoldItem(racer, RaceItem.None);
            return;
        }
        if (RacePower.Find(item) is { } power)
        {
            if (racer.ItemCharges <= 0) racer.ItemCharges = power.Charges;
            if (--racer.ItemCharges == 0) HoldItem(racer, RaceItem.None);
            if (racer == Player) Callout(power.Name.ToUpperInvariant() + "!");
            UseRacePower(racer, item);
            return;
        }
        HoldItem(racer, RaceItem.None);
        if (racer == Player) Callout(ItemName(item).ToUpperInvariant() + "!");
        if (item == RaceItem.WhiteRabbit)
        {
            SummonWhiteRabbit(racer);
            return;
        }
        if (item is RaceItem.Frostbolt or RaceItem.TuskerPus)
        {
            ThrowItem(racer, item);
            return;
        }
        if (item == RaceItem.PortalWard)
        {
            racer.Ward = 12;
            Wave(RiderTarget(racer), WardTint, 2.0f, .85f, new(.32f, 0, 0));
            return;
        }
        if (item == RaceItem.Pepper) GrantBoost(racer, 2.25f);
        if (item == RaceItem.MudPie)
        {
            Vector3 position = racer.FreeDriving
                ? track.FreeGround(racer.FreePosition - racer.Heading(track) * 2.7f)
                : track.Surface(racer.Distance - 2.7f, racer.Lane);
            var puddle = BogCircuit.MeshAt(itemFxRoot, new CylinderMesh { BottomRadius = 1.05f, TopRadius = 1.05f, Height = .04f, RadialSegments = 9 },
                position + Vector3.Up * .052f, Vector3.One, BogCircuit.Paint("51352c", .42f));
            mud.Add((puddle, elapsed + 18, racer));
        }
        if (item == RaceItem.Burp)
        {
            foreach (var other in racers)
                if (other != racer && RiderTarget(other).DistanceSquaredTo(RiderTarget(racer)) < 81 && AcceptHit(other))
                    other.Slowed = 1.4f;
            if (racer != Player && Player.Slowed > 0) Callout("BURPED ON!");
        }
    }

    private void Callout(string text, float duration = 1.2f)
    {
        message = text;
        notification = duration;
    }

    private static string ItemName(RaceItem item) => item switch
    {
        RaceItem.Pepper => "Bog Pepper",
        RaceItem.MudPie => "Mud Pie",
        RaceItem.Burp => "Battle Burp",
        RaceItem.Frostbolt => "Frostbolt",
        RaceItem.TuskerPus => "Tusker Pus",
        RaceItem.PortalWard => "Portal Ward",
        RaceItem.WhiteRabbit => "White Rabbit",
        RaceItem.Gromnatross => "Gromnatross Lift",
        RaceItem.OlthoiLift => "Olthoi Lift",
        _ => RacePower.Find(item)?.Name ?? "Empty"
    };
}









