using Godot;

public sealed partial class MountedRacer
{
    private const float SkiBowStation = 1.42f, SkiSternStation = 1.47f, SkiChineStation = .62f;
    private const float SkiBowKeel = .14f, SkiSternKeel = .23f, SkiChineKeel = .19f;
    private bool skiHasPhysicsHistory, skiBowInWater;
    private Vector2 skiPreviousPosition;
    private float skiPreviousWater, skiPreviousSpeed, skiAirTime, skiSplashCooldown;
    private float skiPitchRate, skiRollRate;
    private RegattaFluidField? skiFluid;
    internal int SkiDriftStreak;
    internal float LastSkiDriftAt;

    internal bool HasSkiWaterState { get; private set; }
    internal float SkiHeave { get; private set; }
    internal float SkiPitch { get; private set; }
    internal float SkiRoll { get; private set; }
    internal float SkiWaterSpeed { get; private set; }
    internal float SkiWaterTime { get; private set; }
    internal float SkiWaterContact { get; private set; } = 1;
    internal float SkiSternContact { get; private set; } = 1;
    internal bool SkiAirborne { get; private set; }

    internal void ResetJetSkiWater()
    {
        skiHasPhysicsHistory = HasSkiWaterState = SkiAirborne = skiBowInWater = false;
        SkiHeave = SkiPitch = SkiRoll = SkiWaterSpeed = SkiWaterTime = 0;
        skiPitchRate = skiRollRate = skiAirTime = skiSplashCooldown = 0;
        skiFluid = null;
        SkiWaterContact = SkiSternContact = 1;
    }

    internal void ApplyReplicaJetSkiWater(float heave, float pitch, float roll, float contact, bool airborne, float time,
        RegattaFluidField? fluid = null)
    {
        if (!UsesJetSki || !float.IsFinite(time)) return;
        HasSkiWaterState = true;
        skiHasPhysicsHistory = false;
        SkiHeave = float.IsFinite(heave) ? Mathf.Clamp(heave, -.90f, 1.48f) : 0;
        SkiPitch = float.IsFinite(pitch) ? Mathf.Clamp(pitch, -.42f, .45f) : 0;
        SkiRoll = float.IsFinite(roll) ? Mathf.Clamp(roll, -.51f, .51f) : 0;
        SkiWaterContact = !airborne && float.IsFinite(contact) ? Mathf.Clamp(contact, 0, 1) : 0;
        SkiSternContact = SkiWaterContact;
        SkiWaterSpeed = 0;
        SkiWaterTime = time;
        skiFluid = fluid;
        SkiAirborne = airborne;
    }

    // The four wetted stations support the hull independently. The water can
    // push on the keel, and gravity takes over as soon as it clears the crest.
    // The return value is the water-entry speed for the pooled impact splash.
    internal float StepJetSkiWater(Vector3 surface, Vector3 heading, float time, float dt, float steer, bool sliding,
        RegattaFluidField? fluid = null)
    {
        if (!UsesJetSki || dt < 0) return 0;
        Vector2 center = new(surface.X, surface.Z);
        Vector2 forward = new Vector2(heading.X, heading.Z).Normalized();
        Vector2 right = new(-forward.Y, forward.X);
        float bow = RegattaSurface.HeightOffset(center + forward * SkiBowStation, time, fluid);
        float stern = RegattaSurface.HeightOffset(center - forward * SkiSternStation, time, fluid);
        float port = RegattaSurface.HeightOffset(center - right * SkiChineStation, time, fluid);
        float starboard = RegattaSurface.HeightOffset(center + right * SkiChineStation, time, fluid);
        float average = (bow + stern + port + starboard) * .25f;
        float pitchSlope = Mathf.Atan2(bow - stern, SkiBowStation + SkiSternStation);
        float rollSlope = Mathf.Atan2(starboard - port, 2 * SkiChineStation);
        float planing = Mathf.SmoothStep(9, 51, Speed);
        if (!skiHasPhysicsHistory || center.DistanceSquaredTo(skiPreviousPosition) > 64 ||
            time < SkiWaterTime || time - SkiWaterTime > .5f)
        {
            ResetJetSkiWater();
            skiHasPhysicsHistory = HasSkiWaterState = skiBowInWater = true;
            SkiHeave = average - .045f + planing * .11f;
            SkiPitch = pitchSlope + planing * .075f;
            SkiRoll = rollSlope;
            skiPreviousWater = average;
            skiPreviousSpeed = Speed;
        }

        float previousWater = skiPreviousWater, previousSpeed = skiPreviousSpeed;
        skiPreviousWater = average;
        skiPreviousPosition = center;
        skiPreviousSpeed = Speed;
        SkiWaterTime = time;
        skiFluid = fluid;
        if (dt == 0) return 0;

        float waterRise = Mathf.Clamp((average - previousWater) / dt, -6, 6);
        float acceleration = (Speed - previousSpeed) / dt;
        float foreLift = Mathf.Sin(SkiPitch), edgeLift = Mathf.Sin(SkiRoll);
        float bowDepth = Math.Max(0, bow - SkiHeave - SkiBowStation * foreLift + SkiBowKeel);
        float sternDepth = Math.Max(0, stern - SkiHeave + SkiSternStation * foreLift + SkiSternKeel);
        float portDepth = Math.Max(0, port - SkiHeave + SkiChineStation * edgeLift + SkiChineKeel);
        float starboardDepth = Math.Max(0, starboard - SkiHeave - SkiChineStation * edgeLift + SkiChineKeel);
        float displacement = (bowDepth + sternDepth + portDepth + starboardDepth) * .25f;
        float deepest = Math.Max(Math.Max(bowDepth, sternDepth), Math.Max(portDepth, starboardDepth));
        float support = Mathf.SmoothStep(0, .12f, deepest);
        float closingSpeed = waterRise - SkiWaterSpeed;
        float buoyancy = displacement * Mathf.Lerp(43.2f, 78.5f, planing) +
            Mathf.Clamp(closingSpeed, -5, 6) * Mathf.Lerp(5.5f, 6f, planing) * support;
        SkiWaterSpeed = Mathf.Clamp(SkiWaterSpeed + (Mathf.Clamp(buoyancy, 0, 52) - RacePhysics.Gravity) * dt, -5.2f, 3.25f);
        SkiHeave = Math.Min(SkiHeave + SkiWaterSpeed * dt, 1.48f);
        // Let the bow bite into a wave without burying the rider on hard entries.
        float bottomOfTravel = Math.Max(-.90f, average - .36f);
        if (SkiHeave < bottomOfTravel)
        {
            SkiHeave = bottomOfTravel;
            SkiWaterSpeed = Math.Max(0, SkiWaterSpeed);
        }

        bool wasAirborne = SkiAirborne;
        SkiAirborne = deepest < (wasAirborne ? .024f : .010f);
        SkiWaterContact = SkiAirborne ? 0 : Mathf.SmoothStep(.006f, .125f, displacement + deepest * .18f);
        SkiSternContact = SkiAirborne ? 0 : Mathf.SmoothStep(.008f, .105f, sternDepth);
        bool bowInWater = bowDepth > (skiBowInWater ? .012f : .045f);
        skiSplashCooldown = Math.Max(0, skiSplashCooldown - dt);
        bool touchdown = wasAirborne && !SkiAirborne && skiAirTime > .045f && closingSpeed > 2.1f;
        bool bowSlap = !skiBowInWater && bowInWater && closingSpeed > 3.4f && Speed > 14;
        float impact = 0;
        if (skiSplashCooldown <= 0 && (touchdown || bowSlap))
        {
            impact = Mathf.Clamp(closingSpeed, 0, 8);
            skiSplashCooldown = .22f;
        }
        skiBowInWater = bowInWater;
        skiAirTime = SkiAirborne ? skiAirTime + dt : 0;

        float poweredTrim = planing * .095f + Mathf.Clamp(acceleration * .0045f, -.115f, .085f);
        float turnBank = steer * Mathf.SmoothStep(1.5f, 20, Speed) * (sliding ? .38f : .29f);
        float pitchTarget = Mathf.Lerp(-.16f, pitchSlope + poweredTrim, support);
        float rollTarget = (rollSlope - turnBank) * support;
        skiPitchRate += ((pitchTarget - SkiPitch) * Mathf.Lerp(14, 92, support) - skiPitchRate * 11) * dt;
        skiRollRate += ((rollTarget - SkiRoll) * Mathf.Lerp(11, 95, support) - skiRollRate * 12) * dt;
        SkiPitch = Mathf.Clamp(SkiPitch + skiPitchRate * dt, -.42f, .45f);
        SkiRoll = Mathf.Clamp(SkiRoll + skiRollRate * dt, -.51f, .51f);
        return impact;
    }
}
