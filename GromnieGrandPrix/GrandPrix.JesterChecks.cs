using Godot;

public partial class GrandPrix
{
    private void StartJesterCheck()
    {
        ChooseCourse(JesterCourseId);
        StartRace();
        Require(racing && activeTown == JesterCourseId && track.IsJester && JesterCourseReady(),
            "Jester’s Descent loads from the race picker: " + courseProblem);
    }

    private void CheckJesterDescent()
    {
        PopulateCoursePicker(JesterCourseName);
        Require(courseList.ItemCount == 1 && courseList.GetItemMetadata(0).AsInt32() == JesterCourseId,
            "Jester’s Descent has a course picker entry");
        StartJesterCheck();
        float entry = track.Length * JesterPortalEntry, exit = track.Length * JesterPortalExit;
        Require(track.Laps == 3 && entry > 3900 && track.TightestInsideRadius() > BogCircuit.MinimumInsideRadius &&
            track.Point(0).Y - track.Point(entry).Y > 200 && track.Point(exit).Y - track.Point(entry).Y > 195,
            $"the continuous circuit keeps wide turns and a deep descent ({track.Length:F0} m, {track.TightestInsideRadius():F1} m radius)");
        var deck = jesterRoot!.GetNode<MeshInstance3D>("Continuous stone ribbon");
        var vertices = deck.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Require(vertices[0] == vertices[^2] && vertices[1] == vertices[^1] &&
            jesterMirrors.Count == 5 && jesterCastStages.Count == 4 &&
            jesterCastStages.All(stage => stage.Jester.NativeAppearanceId == GraveyardJesterLook.Id),
            "the track closes and the four DAT jesters and five mirrors load");

        for (int lapIndex = 0; lapIndex < track.Laps; lapIndex++)
        {
            float offset = lapIndex * track.Length;
            int nextGate = lapIndex * track.GateCount + track.GateCount - 1;
            Player.Distance = offset + entry + .42f;
            Player.Lane = -6.2f;
            Player.Speed = 22;
            Player.GatesCleared = nextGate - 1;
            Require(!TryJesterPortal(Player, offset + entry - .12f), "a missed checkpoint cannot be skipped");
            Player.GatesCleared = nextGate;
            Require(TryJesterPortal(Player, offset + entry - .12f) && Player.GatesCleared == nextGate &&
                Mathf.IsEqualApprox(Player.Distance, offset + exit + .42f) && Player.Lane == -6.2f && Player.Speed == 22,
                "the last door preserves pace, lane, and the next checkpoint on every lap");
        }

        ResetRace();
        float lastPosition = Player.Distance;
        int folds = 0;
        for (int frame = 0; frame < 39000 && !finished; frame++)
        {
            bool sudsed = IsSudsed(Player);
            float steer = Mathf.Clamp(-Player.Lane * (sudsed ? -.45f : .45f), -1, 1);
            TickRace(1f / 60, true, false, steer, false, false);
            if (Player.Distance - lastPosition > track.Length * .07f) folds++;
            lastPosition = Player.Distance;
        }
        Require(finished && Player.LapCrossings.Count == 3 && folds == 3,
            $"a race records three laps through the last door ({Player.GatesCleared} gates, {folds} folds, {elapsed:F1}s)");
        GD.Print("JESTER SMOKE PASSED: full race, three portal crossings, native cast, and continuous road.");
    }

    private async Task CaptureJesterDescent(string folder)
    {
        StartJesterCheck();
        paused = true;
        countdown = 0;
        raceUi.Hide();
        float station = track.Length * JesterGalleryFraction;
        Vector3 target = track.Point(station) - track.Right(station) * (track.HalfWidth + 8) + Vector3.Up * 3.8f;
        PlaceCamera(station - 30, -4, 5.2f, target, 0, 1.3f);
        await Settle();
        await SaveFrame(Path.Combine(folder, "Jester-Gallery.png"));

        station = 76;
        var meltingClock = jesterRoot!.GetNode<Node3D>("Melting clocks").GetChild<Node3D>(0);
        PlaceCamera(station - 36, 3, 7.5f, meltingClock.GlobalPosition - Vector3.Up * 2.8f, 0, 1.9f);
        await Settle();
        await SaveFrame(Path.Combine(folder, "Jester-Melting-Clock.png"));

        station = track.Length * .305f;
        target = track.Point(station) - track.Right(station) * 17 + Vector3.Up * 9;
        PlaceCamera(station - 54, 1, 8.3f, target, 1, 2.7f);
        await Settle();
        Require(jesterActiveMirror is { Portal: false }, "the roadside mirror renders an active planar view");
        await SaveFrame(Path.Combine(folder, "Jester-Wrong-Side.png"));

        station = track.Length * .618f;
        target = track.Point(station) - track.Right(station) * 43 + Vector3.Up * 18;
        PlaceCamera(station - 60, -1, 9.5f, target, 2, 4.4f);
        await Settle();
        await SaveFrame(Path.Combine(folder, "Jester-Broken-Hour.png"));

        station = track.Length * JesterPortalEntry;
        target = track.Point(station) + Vector3.Up * 9;
        PlaceCamera(station - 50, 1, 7.5f, target, 3, 5.7f);
        await Settle();
        Require(jesterActiveMirror is { Portal: true }, "the last door renders a view of its exit");
        await SaveFrame(Path.Combine(folder, "Jester-Last-Door.png"));
        GD.Print("JESTER CAPTURE PASSED: live mirror and portal rendered.");

        async Task Settle()
        {
            for (int i = 0; i < 12; i++)
            {
                elapsed += 1f / 30;
                TickJesterCast(1f / 30);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }

        void PlaceCamera(float at, float lane, float height, Vector3 look, int phase, float time)
        {
            elapsed = time;
            Player.Distance = at + 12;
            Player.Lane = lane;
            Player.Draw(track, 0, false);
            ApplyJesterChapter(phase, 2);
            camera.Fov = 69;
            camera.GlobalPosition = track.Surface(at, lane) + Vector3.Up * height;
            camera.LookAt(look);
            UpdateJesterMirrors();
        }
    }
}
