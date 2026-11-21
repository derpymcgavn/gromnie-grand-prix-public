using Godot;
using System.Text.Json;

public partial class GrandPrix
{
    private void CheckRivalLearning()
    {
        ResetRace();
        var sample = Enumerable.Range(5, (int)(track.Length / 32) - 6)
            .Select(i => i * 32f + 12).First(at => Mathf.Abs(track.Curvature(at)) is > .006f and < .025f);
        var watcher = racers[2];
        var driver = rivalDrivers[1];
        const float look = 20;
        const float lane = .73f;
        watcher.Distance = sample - look * .54f;
        float width = track.WidthAt(sample);
        float before = RivalPreferredLane(watcher, driver, look, width);
        for (int sampleIndex = 0; sampleIndex < 3; sampleIndex++)
        {
            Player.Distance = sample + sampleIndex * 9.01f;
            Player.Lane = track.WidthAt(Player.Distance) * lane;
            Player.Speed = GroundPace(Player) * .97f;
            ObservePlayerLine(gas: true, brake: false);
        }
        int sector = LineSector(sample);
        var learned = rivalLines.Read(activeTown, sector);
        float after = RivalPreferredLane(watcher, driver, look, width);
        Require(learned is { Samples: >= 1 } && Math.Abs(after - width * lane) < Math.Abs(before - width * lane),
            "a rival steers closer to a quick cornering line it learned from the player");
        var data = JsonSerializer.Deserialize<Dictionary<int, Dictionary<int, RivalLineMemory.Line>>>(
            JsonSerializer.Serialize(rivalLines.SavedLines));
        var reloaded = new RivalLineMemory();
        reloaded.Restore(data, id => id == activeTown);
        Require(reloaded.Read(activeTown, sector) == learned && reloaded.Read(activeTown + 5000, sector) is null,
            "learned lines round-trip in the local save and belong to their course");
        ResetRace();
        Require(rivalLines.Read(activeTown, sector) == learned, "retry keeps the learned racing line");
    }

    private void CheckRivalTactics()
    {
        ResetRace();
        countdown = 0;
        float near = Enumerable.Range(12, 38).Select(i => i * 3f).OrderBy(at => Math.Abs(track.Curvature(at + 9))).First();
        for (int i = 0; i < racers.Count; i++)
        {
            racers[i].Distance = near + (i < 2 ? i * 12 : 125 + i * 12);
            racers[i].Lane = 0;
            racers[i].Speed = i == 0 ? 18 : 13;
        }
        var rival = racers[1];
        var driver = rivalDrivers[0];
        HoldItem(rival, RaceItem.PortalWard);
        PlanRival(rival, driver, 1);
        Require(rival.Ward == 0 && rival.Item == RaceItem.PortalWard, "rival holds its ward with no incoming threat");
        ThrowItem(Player, RaceItem.Frostbolt);
        PlanRival(rival, driver, 1);
        Require(rival.Ward > 0 && rival.Item == RaceItem.None, "rival reacts to an incoming Frostbolt");
        ClearItemVisuals();

        var cautious = racers[2];
        cautious.Distance = Player.Distance;
        cautious.Lane = 2;
        rival.Distance = Player.Distance + 100;
        var observer = rivalDrivers[1];
        for (int swipe = 0; swipe < 3; swipe++)
        {
            Player.SwipeCooldown = 0;
            TrySwipe(Player, 1);
        }
        Require(observer.PlayerSwipes > .7f && observer.PlayerSwipeSide > .6f && driver.PlayerSwipes == 0,
            "nearby rivals learn the player's favored swipe side from actual attempts");

        cautious.Distance = near;
        cautious.Lane = 0;
        cautious.Speed = 18;
        Player.Distance = near + 8;
        Player.Speed = 4;
        PlanRival(cautious, observer, 2);
        Require(Math.Abs(observer.AimLane) > 1.5f, "rival moves over to pass a slower racer");
        ResetRace();
    }
}
