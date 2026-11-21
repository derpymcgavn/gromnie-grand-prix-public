using Godot;

public partial class GrandPrix
{
    private void CheckSpanSprint()
    {
        var sprint = spanSprint ?? throw new InvalidOperationException("The Obsidian Span sprint is missing.");
        OpenStable();
        ChooseCourse(sprint.Id);
        StartRace();
        Require(activeTown == sprint.Id && track.IsSprint && track.NativeBlockCount == sprint.Blocks.Length &&
            track.Point(0).DistanceTo(track.Point(track.Length)) > 4000 && track.Length is > 6900 and < 8100,
            "Holtburg to Cragstone is a single, open point-to-point course");
        Require(track.Gate(track.GateCount) < track.Length && checkpointBeacons.Count == track.GateCount + 1,
            "the point-to-point course has its own finish line");
        var span = track.Project(new Vector3(sprint.West + 84, 0, sprint.RiverAxis), 0).Distance;
        Vector3 deck = track.Point(span);
        Require(track.IsSpan(span) && Math.Abs(deck.Z - sprint.RiverAxis) < .1f &&
            deck.Y - track.FreeGround(deck).Y > 33 && track.WidthAt(span) < 3,
            "the route climbs onto the narrow original bridge deck");
        Require(track.GroundOrRoadHeight(deck - Vector3.Up * 4) >= deck.Y - .02f,
            "the camera detects the original bridge deck above the river");
        Require(course.GetChildren().OfType<Node3D>().Count(block => block.Name.ToString().StartsWith("Landblock_")) == sprint.Blocks.Length,
            "all corridor landblocks have terrain and authored scenery");
        var gates = Enumerable.Range(0, track.GateCount + 1).Select(track.Gate).ToArray();
        Require(gates.SequenceEqual(gates.Order()) && track.IsSpan(gates[10]), "the original crossing has an enforced checkpoint");
        CheckTrackFall(span, 1);
        ResetRace();

        const float step = 1f / 60;
        bool crossedSpan = false;
        int frames = 0;
        for (; frames < 38000 && !finished; frames++)
        {
            bool flipped = IsSudsed(Player);
            float steer = Mathf.Clamp(-Player.Lane * (flipped ? -.42f : .42f), -1, 1);
            TickRace(step, !flipped, flipped, steer, false, false);
            crossedSpan |= Player.Distance > span && Player.GatesCleared > 10;
        }
        Require(crossedSpan && finished && Player.GatesCleared == gates.Length && bestByCourse.ContainsKey(sprint.Id),
            $"the full road sprint reaches the Cragstone result after all checkpoints ({Player.Distance:F0} m, gate {Player.GatesCleared}, {frames} frames)");
        GD.Print($"SPAN SPRINT PASS | {track.Length:F0} m, {track.NativeBlockCount} landblocks, crossing enforced, finish {Player.Finish:F1}s.");
        OpenStable();
    }

    private async Task CaptureSpanSprint(string folder)
    {
        var sprint = spanSprint ?? throw new InvalidOperationException("The Obsidian Span sprint is missing.");
        OpenStable();
        ChooseCourse(sprint.Id);
        StartRace();
        Require(activeTown == sprint.Id && track.IsSprint, "the sprint opens from the picker");
        countdown = 0;
        elapsed = 145;
        paused = true;
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            float x = sprint.West + 70 + i * 12;
            racer.Distance = track.Project(new Vector3(x, 0, sprint.RiverAxis), 0).Distance;
            racer.GatesCleared = 10;
            racer.Lane = i % 2 == 0 ? -.92f : 1.14f;
            racer.Speed = 22;
            racer.Draw(track, .36f + i * .042f, true);
        }
        ApplyWeather(WeatherPresets[4]);
        SetRaceCamera(1);
        UpdateSpanReflection();
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Holtburg-Cragstone-Sprint.png"));

        camera.Position = new Vector3(sprint.West + 144, riverLevel + 13.4f, sprint.RiverAxis + 112);
        camera.Fov = 62;
        camera.LookAt(new Vector3(sprint.West + 92, riverLevel + 19, sprint.RiverAxis - 6));
        UpdateSpanReflection();
        Require(riverReflection?.RenderTargetUpdateMode == SubViewport.UpdateMode.Always &&
            riverCamera!.Position.Y < riverLevel && riverCamera.CullMask == RiverMirrorLayer, "the alternate view reflects native bridge scenery in the river");
        raceUi.Hide();
        await SaveFrame(Path.Combine(folder, "Obsidian-Span-Reflection.png"));
        OpenStable();
    }
}
