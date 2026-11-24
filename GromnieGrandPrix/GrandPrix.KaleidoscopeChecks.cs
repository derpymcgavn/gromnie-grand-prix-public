using Godot;

public partial class GrandPrix
{
    private void StartKaleidoscopeCheck()
    {
        ChooseCourse(KaleidoscopeCourseId);
        StartRace();
        Require(racing && activeTown == KaleidoscopeCourseId && track.IsKaleidoscope && KaleidoscopeCourseReady(),
            "Kaleidoscope loads from the course picker: " + courseProblem);
    }

    private void CheckKaleidoscopeCourse()
    {
        PopulateCoursePicker(KaleidoscopeCourseName);
        Require(courseList.ItemCount == 1 && courseList.GetItemMetadata(0).AsInt32() == KaleidoscopeCourseId,
            "Kaleidoscope has its own course picker entry");
        StartKaleidoscopeCheck();
        float radius = track.TightestInsideRadius();
        Require(track.Length is > 3750 and < 4900 && track.Laps == 3 && track.HalfWidth >= 16 &&
            radius > BogCircuit.MinimumInsideRadius &&
            track.Point(0).DistanceTo(track.Point(track.Length - .04f)) < .10f,
            $"the enclosed race has a wide, closed three-lap circuit ({track.Length:F0} m, {radius:F1} m inside radius)");

        var scene = course.GetNode<Node3D>("Kaleidoscope");
        var deck = scene.GetNode<MeshInstance3D>("Prismatic race deck");
        var nativeEnergy = assets.Texture(RynthidEnergyTexture);
        Require(nativeEnergy is not null && deck.MaterialOverride is ShaderMaterial material &&
            material.GetShaderParameter("cloak_energy").AsGodotObject() == nativeEnergy,
            "the race surface uses the Rynthid Energy Field from the client DAT");
        var vertices = deck.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        foreach (int side in new[] { -1, 1 })
        {
            Vector3 edge = deck.ToLocal(track.Surface(0, side * track.HalfWidth));
            Require(vertices.Count(vertex => vertex.DistanceSquaredTo(edge) < .0025f) >= 2,
                $"the two ends of the deck meet at the {side:+0;-0} shoulder");
        }
        var sections = scene.GetNode<Node3D>("Mirrored tunnel").GetChildren().OfType<MeshInstance3D>().ToArray();
        Require(sections.Length >= 4 && sections.All(section => section.Mesh?.GetSurfaceCount() > 0),
            "the mirrored enclosure is built in cullable mesh sections");

        var rotor = scene.GetNode<Node3D>("Kaleidoscope prisms/Prism chamber 1/Rosette");
        Basis before = rotor.Basis;
        TickKaleidoscopePrisms(1.5f);
        Require(rotor.Basis != before && rotor.Basis.IsFinite(), "native crystal rosettes rotate while the racer stands still");

        ResetRace();
        int frames = Mathf.CeilToInt((track.Length / (RoadPace * .6f) + 35) * 60);
        for (int frame = 0; frame < frames && Player.LapCrossings.Count == 0; frame++)
        {
            bool sudsed = IsSudsed(Player);
            float steer = Mathf.Clamp(-Player.Lane * (sudsed ? -.45f : .45f), -1, 1);
            TickRace(1f / 60, !sudsed, sudsed, steer, false, false);
        }
        Require(Player.LapCrossings.Count == 1 && Player.GatesCleared >= track.GateCount + 1 && lap == 2,
            $"the rider completes a timed lap through every checkpoint ({Player.GatesCleared} gates, {elapsed:F1}s)");
        GD.Print($"KALEIDOSCOPE SMOKE PASSED: complete lap, moving DAT prisms, and native Energy Field on the closed {track.Length:F0} m race deck.");
    }

    private async Task CaptureKaleidoscopeCourse(string folder)
    {
        StartKaleidoscopeCheck();
        SetPhysicsProcess(false);
        countdown = 0;
        ClearSpeedBlur();
        raceUi.Hide();

        if (OS.GetCmdlineUserArgs().Contains("--kaleidoscope-video"))
        {
            // Godot's --write-movie and --fixed-fps 30 capture one image for each pair of physics ticks.
            await FilmSection(91, 120);
            await FilmSection(track.Length * .59f, 90);
        }
        else
        {
            StageRacers(105);
            PlaceKaleidoscopeCamera();
            await SaveFrame(Path.Combine(folder, "Kaleidoscope-Prism-Chamber.png"));
            using var before = GetViewport().GetTexture().GetImage();
            for (int step = 0; step < 20; step++)
            {
                TickKaleidoscopePrisms(.075f);
                await ToSignal(GetTree().CreateTimer(.075), SceneTreeTimer.SignalName.Timeout);
            }
            await SaveFrame(Path.Combine(folder, "Kaleidoscope-Prism-Chamber-Later.png"));
            using var after = GetViewport().GetTexture().GetImage();
            before.Resize(96, 54, Image.Interpolation.Bilinear);
            after.Resize(96, 54, Image.Interpolation.Bilinear);
            float change = 0;
            // The fixed upper crop contains only tunnel scenery.
            for (int y = 0; y < 26; y++)
            for (int x = 0; x < 96; x++)
            {
                Color a = before.GetPixel(x, y), b = after.GetPixel(x, y);
                change += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            }
            Require(change / (3 * 96 * 26) > .006f, "a stationary camera sees the mirrored tunnel changing");

            StageRacers(track.Length * .31f);
            PlaceKaleidoscopeCamera();
            await SaveFrame(Path.Combine(folder, "Kaleidoscope-Glass-Curve.png"));

            StageRacers(track.Length * .64f);
            PlaceKaleidoscopeCamera();
            await SaveFrame(Path.Combine(folder, "Kaleidoscope-Deep-Prism.png"));
        }
        GD.Print("KALEIDOSCOPE CAPTURE PASSED: animated glass enclosure and rotating prisms.");

        async Task FilmSection(float station, int frames)
        {
            StageRacers(station);
            for (int frame = 0; frame < frames; frame++)
            {
                for (int tick = 0; tick < 2; tick++)
                {
                    bool sudsed = IsSudsed(Player);
                    float steer = Mathf.Clamp(-Player.Lane * (sudsed ? -.45f : .45f), -1, 1);
                    TickRace(1f / 60, !sudsed, sudsed, steer, false, false);
                }
                PlaceKaleidoscopeCamera();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            Require(Player.Distance > station + 42 && !Player.Falling, "the moving preview races through the tunnel");
        }

        void StageRacers(float station)
        {
            for (int i = 0; i < racers.Count; i++)
            {
                var racer = racers[i];
                racer.Distance = station + i * 10;
                racer.Lane = i == 0 ? 0 : (i % 2 == 0 ? -1 : 1) * (3.1f + i);
                racer.Speed = RoadPace;
                racer.GatesCleared = Enumerable.Range(0, track.GateCount).Count(gate => track.Gate(gate) <= racer.Distance);
                racer.Checkpoint = racer.GatesCleared > 0 ? track.Gate(racer.GatesCleared - 1) : 0;
                racer.Draw(track, .08f, true);
            }
            HighlightCheckpoint();
        }

        void PlaceKaleidoscopeCamera()
        {
            float station = Player.Distance;
            camera.Projection = Camera3D.ProjectionType.Perspective;
            camera.Fov = 69;
            camera.GlobalPosition = track.Surface(station - 13, Player.Lane * .65f) + Vector3.Up * 5.6f;
            camera.LookAt(track.Point(station + 31) + Vector3.Up * 4.5f);
        }
    }
}
