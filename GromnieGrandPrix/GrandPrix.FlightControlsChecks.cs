using Godot;

public partial class GrandPrix
{
    private FlightRide LaunchFlightControlsCheck()
    {
        var route = VoidFlyover(track);
        countdown = 0;
        Player.Distance = route.Start;
        Player.Lane = 0;
        Player.GatesCleared = 2;
        Player.Checkpoint = track.Gate(1);
        Player.Speed = 34;
        Require(LaunchFlight(Player, route), "the Singularity flight-control test launches the rider");
        return Player.Flight!;
    }

    private void CheckFlightControls()
    {
        StartVoidCheck();
        var ride = LaunchFlightControlsCheck();
        Advance(.28f, 0, dive: true);
        Require(ride.AirPitch == 0 && !ride.DiveReady, "holding W during takeoff keeps neutral cruise");

        Advance(.46f, 1, climb: true);
        Require(ride.DiveReady && ride.AirSteer > 18 && ride.Position.Y > CenterLine().Y + 11 && Player.Speed < 39,
            "free flight climbs and banks right with speed traded for height");
        Advance(.68f, -1, dive: true);
        Require(ride.AirSteer < -18 && ride.Position.Y < CenterLine().Y - 11 && Player.Speed > 47,
            "free flight banks left and dives with height traded for speed");
        Require(ride.TryRoll(-1), "free flight retains the barrel roll");
        Advance(1, -1, climb: true);
        Require(Player.Flight is null && !Player.Falling && Mathf.IsEqualApprox(Player.Distance, ride.End) &&
                Mathf.Abs(Player.Lane) < .01f && Player.GatesCleared == 2,
            "holding flight controls still lands on the marked deck with race progress intact");

        ResetRace();
        ride = LaunchFlightControlsCheck();
        Advance(.46f, 1, climb: true, free: false);
        Require(ride.AirPitch == 0 && ride.AirSteer is > 2.8f and <= 3.1f && Mathf.IsEqualApprox(ride.Position.Y, CenterLine().Y),
            "Guided flight retains the original small bank and automatic altitude");
        Player.Land();
        OpenStable();
        GD.Print("FLIGHT CONTROLS PASS | held-throttle cruise, wide bank, climb, dive, barrel roll and automatic landing.");

        float Progress() => (Player.Distance - ride.Start) / (ride.End - ride.Start);

        Vector3 CenterLine() => SummitPoint(track, ride.Start, ride.End, Progress(), ride.StartLane, ride.Height, 0, ride.CitadelFlyby);

        void Advance(float target, float steering, bool dive = false, bool climb = false, bool free = true)
        {
            for (int tick = 0; Player.Flight == ride && Progress() < target && tick < 1800; tick++)
                TickFlight(Player, ride, 1f / 60, steering, free, dive, climb);
        }
    }

    private async Task CaptureFlightControls(string folder, bool video)
    {
        StartVoidCheck();
        SetPhysicsProcess(false);
        var ride = LaunchFlightControlsCheck();
        const float step = 1f / 30;
        float previewStart = Mathf.Lerp(ride.Start, ride.End, .26f);
        for (int stepIndex = 0; stepIndex < 900 && Player.Flight == ride && Player.Distance < previewStart; stepIndex++)
            TickFlight(Player, ride, step, 0, true, true);
        Require(Player.Flight == ride && Player.Distance >= previewStart, "the flight preview reaches the open air");
        SetRaceCamera(1);
        for (int frame = 0; frame < 150 && Player.Flight == ride; frame++)
        {
            bool climbing = frame < 66;
            TickRace(step, !climbing, climbing, climbing ? 1 : -1, false, false);
            SetRaceCamera(1 - Mathf.Exp(-8.2f * step));
            UpdateRaceUi();
            if (!video && frame == 45)
            {
                await SaveFrame(Path.Combine(folder, "Free-Flight.png"));
                break;
            }
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        GD.Print("FLIGHT CONTROLS CAPTURE PASS");
    }
}
