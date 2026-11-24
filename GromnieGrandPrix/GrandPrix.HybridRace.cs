using Godot;

public partial class GrandPrix
{
    private bool SyncHybridTravel(MountedRacer racer, bool replica = false, bool quiet = false)
    {
        if (!track.HasHybridTravel || racer.Flight is not null || racer.Rider.TopLevel || racer.Falling) return false;
        var section = track.TravelSection(racer.Distance);
        bool water = section.Mode == TrackTravelMode.JetSki;
        bool takeoff = !replica && section.Mode == TrackTravelMode.Flight && racer.Finish < 0 &&
            racer.EjectTime <= 0 && racer.ResetHold <= 0;
        bool swap = racer.SetJetSkiActive(water);
        if (replica || !swap && !takeoff) return false;

        // Recovery belongs to the vehicle we left. A course transition gives us the next rig.
        ClearRacerPowers(racer);
        racer.BoostStrain = racer.MountRecovery = racer.RemountTime = 0;
        racer.Boost = racer.SuperDriftUntil = racer.SkiDriftStreak = 0;
        racer.GroundDrifting = false;
        racer.FreeDriving = false;
        racer.Lane = Mathf.Clamp(racer.Lane, -track.WidthAt(racer.Distance) + 1, track.WidthAt(racer.Distance) - 1);
        racer.SetMountVisible(true);
        ResetRivalMotion(racer);
        if (racer == Player)
        {
            charge = lateral = 0;
            drifting = false;
        }
        else if (OnlineHost && RemotePilot(racers.IndexOf(racer)) is { } pilot)
        {
            pilot.Charge = pilot.Lateral = 0;
            pilot.Sliding = false;
        }
        if (takeoff)
        {
            float height = HybridFlightHeight(section.End - section.Start);
            return LaunchFlight(racer, new Flyover("TAKE FLIGHT!", section.Start, section.End, height, 0),
                courseFlight: true, quiet: quiet);
        }
        if (water) TickJetSkiWater(racer, 0, 0, false);
        else racer.Speed = Math.Min(racer.Speed, DriftSpeedCap);
        if (racer == Player && !quiet) Callout(water ? "JET SKI!" : "BACK ON LAND!", 1.2f);
        return false;
    }

    private static float HybridFlightHeight(float length) =>
        Mathf.Clamp(9 + MathF.Sqrt(length) * 1.85f, 21, 64);

    private static Vector3 HybridFlightCenter(BogCircuit route, TrackTravelSection section, float station) =>
        HybridFlightPoint(route, section.Start, section.End, station, 0, HybridFlightHeight(section.End - section.Start), 0);

    private static Vector3 HybridFlightPoint(BogCircuit route, float start, float end, float station, float lane,
        float height, float steer)
    {
        float length = Math.Max(1, end - start);
        float t = Mathf.Clamp((station - start) / length, 0, 1);
        float climb = Mathf.Clamp(length * .28f, 14, 100);
        float flightWeight = Mathf.SmoothStep(0, climb, station - start) * Mathf.SmoothStep(0, climb, end - station);
        float limit = route.WidthAt(station) - .8f;
        float offset = Mathf.Clamp(lane + steer * Mathf.Sin(Mathf.Pi * t), -limit, limit);
        Vector3 at = route.Surface(station, offset);
        at.Y += .4f + height * flightWeight;
        at.Y = Math.Max(at.Y, route.FreeGround(at).Y + .4f + 2.8f * flightWeight);
        return at;
    }
}
