using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private MountedRacer StageUlgrimFolly()
    {
        ResetRace();
        countdown = 0;
        const float distance = 77;
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            racer.GatesCleared = 1;
            racer.Distance = i switch { 0 => distance, 1 => distance - 4.5f, _ => distance + i * 35 };
            racer.Lane = i == 1 ? -2.6f : 0;
            racer.Speed = 16;
            racer.Draw(track, .1f, true);
        }
        return racers[1];
    }

    private void CheckUlgrimFolly()
    {
        var rival = StageUlgrimFolly();
        const float step = 1f / 60;
        Player.Ward = 5;
        HoldItem(rival, RaceItem.UlgrimFolly);
        UseItem(rival);
        var pour = steinPours.Single();
        Require(pour.Target == Player && pour.Stein is AcActor stein && stein.Setup.Id == CastingSteinSetup
            && stein.BodyMeshes.Any(mesh => mesh.Mesh?.GetSurfaceCount() > 0) && ItemIcon(RaceItem.UlgrimFolly) is not null,
            "Ulgrim's Folly targets the closest rival with the native Casting Stein and icon");
        while (steinPours.Count > 0) TickUlgrimEffects(step);
        Require(Player.Ward == 0 && !IsSudsed(Player) && beerSuds.Count == 0, "Portal Ward stops the beer splash");

        HoldItem(rival, RaceItem.UlgrimFolly);
        UseItem(rival);
        while (steinPours.Count > 0) TickUlgrimEffects(step);
        Require(Player.SudsedUntil == elapsed + 10 && beerSuds.TryGetValue(Player, out var suds) && suds.Bubbles.Length > 0
            && DrivingInputs(Player, true, false, .75f) == (false, true, -.75f)
            && DrivingInputs(rival, true, false, .75f) == (true, false, .75f),
            "a hit coats only the target in suds and flips both driving axes for ten seconds");
        float before = Player.Speed;
        TickRace(step, true, false, 1, false, false);
        Require(Player.Speed < before && lateral < 0, "accelerate and right produce braking and left steering while sudsed");
        before = Player.Speed;
        TickRace(step, false, true, -1, false, false);
        Require(Player.Speed > before, "the brake key accelerates while sudsed");

        elapsed = Player.SudsedUntil - .02f;
        TickUlgrimEffects(step);
        Require(IsSudsed(Player) && beerSuds.ContainsKey(Player), "the foam and reversed controls last through the final fraction of the timer");
        elapsed += .02f;
        TickUlgrimEffects(step);
        Require(!IsSudsed(Player) && beerSuds.Count == 0 && DrivingInputs(Player, true, false, .75f) == (true, false, .75f),
            "the ten-second timer restores normal inputs and clears the foam");
        ResetRace();
    }

    private async Task CaptureUlgrimFolly(string folder)
    {
        ChooseCourse(0);
        StartRace();
        var rival = StageUlgrimFolly();
        paused = true;
        HoldItem(rival, RaceItem.UlgrimFolly);
        UseItem(rival);
        const float step = 1f / 60;
        for (int frame = 0; frame < 70 && !IsSudsed(Player); frame++)
        {
            elapsed += step;
            TickItemEffects(step);
        }
        Require(IsSudsed(Player), "the in-game capture shows a real hit from Ulgrim's Folly");
        TickUlgrimEffects(.13f);
        Vector3 center = Player.CoursePosition(track);
        Vector3 forward = Player.Heading(track);
        camera.Position = center + forward * 7 + Player.Right(track) * 4.8f + Vector3.Up * 3.55f;
        camera.Fov = 55;
        camera.LookAt(center + Vector3.Up * 1.5f);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Ulgrims-Folly.png"));
        OpenStable();
    }
}
