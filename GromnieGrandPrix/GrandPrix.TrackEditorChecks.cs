using Godot;
using Disk = System.IO.File;

public partial class GrandPrix
{
    private static readonly Vector2[] ExampleTrackPoints =
    [
        new(-144, 120), new(-168, 48), new(-168, -48), new(-120, -144), new(-24, -168),
        new(72, -168), new(144, -120), new(168, -24), new(144, 48), new(72, 72),
        new(24, 120), new(-48, 168), new(-96, 168)
    ];

    private void SetUpExampleTrack()
    {
        OpenTrackEditor();
        Require(trackEditorUi.Visible && courseUi.Visible == false, "the stable opens the track editor");
        editorBlock.Text = "B271FFFF";
        editorSize.Select(1);
        ChangeEditorLocation();
        Require(editorLand?.Block.Id == 0xb271ffff && editorLand.Blocks.Count == 9, "a full landcell ID loads its original grid");
        editorName.Text = "Yanshi Foothills";
    }

    private void PlotExampleTrack()
    {
        editorTool.Select(0);
        foreach (var point in ExampleTrackPoints) ClickTrackPoint(point);
        ToggleTrackLoop();
        Require(editorClosed && sketch.Start is not null && sketch.Road.Length > editorPoints.Count,
            "placing points previews the closed spline and start line");
    }

    private void ClickTrackPoint(Vector2 point, MouseButton button = MouseButton.Left, bool insert = false, bool doubleClick = false)
    {
        using var input = new InputEventMouseButton { ButtonIndex = button, Pressed = true, AltPressed = insert, DoubleClick = doubleClick };
        TrackMapPressed(point, input);
        EndTrackDrag();
    }

    private void CheckTrackEditor(string folder)
    {
        string relativeTracks = Path.GetRelativePath(folder, ProjectSettings.GlobalizePath(TrackFolder));
        Require(relativeTracks.StartsWith("appdata" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "the track editor check uses an isolated save folder");
        SetUpExampleTrack();
        Require(editorWidth.Value == CustomTrack.DefaultWidth, "new drafts start at the wider road width");
        editorTool.Select(1);
        RotatePiece(1);
        ClickTrackPoint(new(-167, 98));
        Require(editorPoints.SequenceEqual([new Vector2(-168, 96), new(-120, 96), new(-72, 96)]),
            "grid snapping and a 90-degree piece rotation produce the expected controls");
        editorTool.Select(3);
        ClickTrackPoint(Vector2.Zero);
        Require(editorRotation == 2 && editorPoints[^1] == new Vector2(48, 216),
            "a sweeper connects to the previous tip and rotates the next piece");
        RecallShape(undoTrack, redoTrack);
        Require(editorPoints.Count == 3, "undo removes the whole piece");
        RecallShape(redoTrack, undoTrack);
        Require(editorPoints.Count == 7, "redo restores the complete piece");

        LoadTrackDraft(null);
        SetUpExampleTrack();
        editorTool.Select(0);
        foreach (var point in new Vector2[] { new(0, -96), new(96, -96), new(96, 0), Vector2.Zero })
            ClickTrackPoint(point);
        editorTool.Select(1);
        ClickTrackPoint(Vector2.Zero);
        Require(editorClosed && editorPoints.Count == 5 && editorPoints[^1] == new Vector2(0, -48),
            "a piece whose tip reaches the first point closes the loop without a duplicate control");
        RecallShape(undoTrack, redoTrack);
        Require(!editorClosed && editorPoints.Count == 4, "undo reopens the joined piece");

        LoadTrackDraft(null);
        SetUpExampleTrack();
        PlotExampleTrack();
        ClickTrackPoint(new(96, -144), insert: true);
        Require(editorPoints.Count == ExampleTrackPoints.Length + 1, "Alt-click inserts a spline point");
        ClickTrackPoint(new(96, -144), MouseButton.Right);
        Require(editorPoints.Count == ExampleTrackPoints.Length, "right-click removes the inserted point");
        ClickTrackPoint(ExampleTrackPoints[3]);
        SetTrackStart();
        Require(editorPoints[0] == ExampleTrackPoints[3], "a selected section becomes the start");

        int prior = selectedTown;
        TestCustomTrack();
        Require(racing && activeTown == CustomTrack.PreviewId && track.IsCustom &&
            track.IsRegional && track.Laps == 1 && track.NativeBlockCount == 9,
            "the draft loads in the normal race engine (" + editorStatus.Text + ")");
        CheckCustomDeck();
        CheckRaceViews();
        var (edge, side) = FindDownhillShoulder();
        CheckCustomRoadContainment(edge, side);
        CheckCustomMountClearance(edge);
        ResetRace();
        for (int i = 0; i < track.Length * 8 && !finished; i++)
        {
            bool flipped = IsSudsed(Player);
            float steer = Mathf.Clamp(Player.Lane * (flipped ? .29f : -.29f), -1, 1);
            TickRace(1f / 60, !flipped, flipped, steer, false, false);
        }
        Require(finished && Player.GatesCleared == track.GateCount + 1 && !bestByCourse.ContainsKey(CustomTrack.PreviewId),
            "a draft can complete every checkpoint without recording a permanent best");
        OpenStable();
        Require(trackEditorUi.Visible && selectedTown == prior && editorClosed && editorPreview is null,
            "leaving a test returns to the same draft and restores the prior race");

        SaveCustomTrack();
        int savedId = editorId;
        string saveFile = Path.Combine(ProjectSettings.GlobalizePath(TrackFolder), $"{savedId}.json");
        Require(savedId >= CustomTrack.FirstId && Disk.Exists(saveFile), "Save writes a custom track (" + editorStatus.Text + ")");
        testMode = false;
        try { LoadCustomTracks(); }
        finally { testMode = true; }
        PopulateCoursePicker("Yanshi Foothills");
        Require(courseList.ItemCount == 1 && courseList.GetItemMetadata(0).AsInt32() == savedId, "the saved track appears in the normal course picker");
        LoadTrackDraft(customTracks[savedId]);
        Require(editorClosed && editorPoints.Count == ExampleTrackPoints.Length && editorPoints[0] == ExampleTrackPoints[3],
            "the saved track reopens with the original points and start");
        HideTrackEditor();
        ChooseCourse(savedId);
        StartRace();
        Require(racing && activeTown == savedId && track.IsCustom, "the saved track launches from the course picker");
        OpenStable();

        LoadTrackDraft(null);
        editorBlock.Text = "B46D";
        editorSize.Select(0);
        ChangeEditorLocation();
        foreach (var point in new Vector2[] { new(-72, 0), new(-72, -72), new(0, -72), new(72, -72),
            new(72, 0), new(72, 72), new(0, 72), new(-72, 72) }) ClickTrackPoint(point);
        ClickTrackPoint(new(-72, 0), doubleClick: true);
        using var singleBlockSpline = MakeCustomTrack(CustomTrack.PreviewId).Build(editorLand!).Curve;
        Require(editorLand?.GridSide == 1 && singleBlockSpline.GetBakedLength() > 400, "a complete custom circuit fits on one native landblock");

        GD.Print("TRACK EDITOR PASS | Native 1×1 and 3×3, terrain-following dirt loop, verge continuity, mount and camera clearance, full draft lap, save and reload.");
    }

    private void CheckCustomDeck()
    {
        var deck = course.GetNode<Node3D>("Terrain-following road");
        var strip = deck.GetNode<MeshInstance3D>("Continuous dirt road").Mesh;
        var arrays = strip.SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var triangles = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var (rings, columns) = CheckClosedStrip(vertices, triangles, "continuous dirt road");
        Require(rings > 100 && columns >= 7, "the road has connected lanes around the loop");
        foreach (string side in new[] { "Left", "Right" })
        {
            var adjacent = vertices;
            int priorColumns = columns;
            int inner = side == "Left" ? 1 : 0;
            foreach (string stripName in new[] { "packed shoulder", "feathered verge" })
            {
                var surface = deck.GetNode<MeshInstance3D>($"{side} {stripName}").Mesh.SurfaceGetArrays(0);
                var indices = surface[(int)Mesh.ArrayType.Index].AsInt32Array();
                var stripVertices = surface[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var (stripRings, width) = CheckClosedStrip(stripVertices, indices, $"{side} {stripName}");
                Require(stripRings == rings && width == (stripName == "packed shoulder" ? 2 : 3),
                    $"{side} {stripName} matches the road segments");
                for (int i = 0; i < rings; i++)
                    Require(adjacent[i * priorColumns + (1 - inner) * (priorColumns - 1)] ==
                        stripVertices[i * width + inner * (width - 1)], $"{side} {stripName} shares its adjacent edge");
                if (width == 3)
                {
                    var weights = surface[(int)Mesh.ArrayType.Color].AsColorArray();
                    Require(weights[inner * 2].G > .99f && weights[1].G is > .3f and < .65f &&
                        weights[(1 - inner) * 2].G < .01f, "the verge fades all the way into native ground");
                }
                adjacent = stripVertices;
                priorColumns = width;
            }
            int outer = (1 - inner) * (priorColumns - 1);
            for (int row = 0; row < rings; row++)
            {
                var toe = adjacent[row * priorColumns + outer];
                var next = adjacent[(row + 1) % rings * priorColumns + outer];
                var middle = toe.Lerp(next, .5f);
                Require(track.NativeBounds.HasPoint(new Vector2(toe.X, toe.Z)) &&
                    toe.Y < track.FreeGround(toe).Y - .29f && middle.Y < track.FreeGround(middle).Y,
                    "the verge reaches native terrain around the entire loop");
            }
        }
        var dirt = (ShaderMaterial)deck.GetNode<MeshInstance3D>("Continuous dirt road").MaterialOverride;
        Require(dirt.GetShaderParameter("packed_dirt").AsGodotObject() is Texture2D &&
            dirt.GetShaderParameter("road_stone").AsGodotObject() is Texture2D,
            "the continuous road material blends both native packed dirt and native stone");
        for (int lane = -1; lane <= 1; lane++)
            Require(track.Surface(-.03f, lane * track.HalfWidth).DistanceTo(track.Surface(.03f, lane * track.HalfWidth)) < .15f,
                "the drivable deck continues across the seam at both edges");
        int sampled = 0, close = 0, banked = 0;
        for (float at = 0; at < track.Length; at += 1.25f)
        for (int x = -6; x <= 6; x++)
        {
            var point = track.Surface(at, track.HalfWidth * x / 6);
            float relief = point.Y - track.FreeGround(point).Y;
            Require(relief >= -.002f && Math.Abs(point.Y - track.GroundOrRoadHeight(point)) < .04f,
                "the driving surface follows the rendered dirt and native terrain");
            if (!track.NativeDry(point)) continue;
            sampled++;
            if (relief < .55f) close++;
            if (x == -6 && Math.Abs(point.Y - track.Surface(at, track.HalfWidth).Y) > 2) banked++;
        }
        Require(close >= sampled * .93f && banked > 15,
            $"the road stays near native terrain while following the cross slope ({close}/{sampled}, {banked} banked stations)");
        var (arch, _) = checkpointArches[0];
        camera.Position = arch.ToGlobal(new Vector3(0, .5f, -1));
        UpdateCheckpointArchVisibility();
        Require(!arch.Visible && checkpointBeacons[0].Left.Visible && checkpointBeacons[0].Right.Visible,
            "a camera next to the overhead bar still shows the checkpoint beacons");
        camera.Position = arch.ToGlobal(new Vector3(0, .5f, -12));
        UpdateCheckpointArchVisibility();
        Require(arch.Visible, "the overhead bar reappears once the camera clears it");
        SetRaceCamera(1);
    }

    private static (int Rings, int Columns) CheckClosedStrip(Vector3[] vertices, int[] indices, string strip)
    {
        Require(indices.Length >= 6 && indices[0] == 0 && indices[1] > 1, $"{strip} has indexed lanes");
        int columns = indices[1];
        int rowIndices = (columns - 1) * 6;
        int rings = indices.Length / rowIndices;
        bool duplicatedEnd = vertices.Length == (rings + 1) * columns;
        Require(rings >= 3 && indices.Length == rings * rowIndices &&
            (duplicatedEnd || vertices.Length == rings * columns), $"{strip} has a complete row for each segment");
        if (duplicatedEnd)
        {
            for (int column = 0; column < columns; column++)
                Require(vertices[column].DistanceSquaredTo(vertices[rings * columns + column]) < .000001f,
                    $"{strip} closes with coincident start and end vertices");
        }

        string connects = $"{strip} joins each lane to the next row, including across the seam";
        for (int row = 0; row < rings; row++)
        {
            int next = duplicatedEnd ? row + 1 : (row + 1) % rings;
            for (int column = 0; column < columns - 1; column++)
            {
                int at = row * rowIndices + column * 6;
                int a = row * columns + column, b = next * columns + column;
                Require(indices[at] == a && indices[at + 1] == b && indices[at + 2] == a + 1 &&
                    indices[at + 3] == a + 1 && indices[at + 4] == b && indices[at + 5] == b + 1, connects);
            }
        }
        return (rings, columns);
    }

    private void CheckCustomRoadContainment(float distance, int exposedSide)
    {
        float edge = track.WidthAt(distance) - RidgeRoad.CustomShoulder;
        foreach (var (racer, at, side) in new[] { (Player, distance, exposedSide), (racers[1], track.Length - 2, -exposedSide) })
        {
            racer.Speed = 24;
            racer.Distance = at;
            racer.Lane = side * (edge - .02f);
            float outward = side * 7;
            for (int frame = 0; frame < 30; frame++)
                MoveGroundRacer(racer, ref outward, 1f / 60, 24, 11, side, true, allowOffRoad: racer == Player);
            Require(!racer.FreeDriving && !racer.Falling && Math.Abs(racer.Lane) <= edge + .001f &&
                racer.Distance > at + 4 && racer.Speed > 22,
                "players and rivals steer along the shoulder across steep edges and the loop seam");
        }

        Player.Distance = distance;
        Player.Lane = edge * exposedSide;
        Player.Draw(track, 0, true);
        var underDeck = Player.CoursePosition(track) - Vector3.Up * 2;
        Require(float.IsFinite(track.RoadHeight(underDeck)) && track.GroundOrRoadHeight(underDeck) > underDeck.Y + 1.8f,
            "the camera can find the graded road from below");
        foreach (RaceView view in Enum.GetValues<RaceView>())
        {
            raceView = view;
            camera.Position = underDeck;
            SetRaceCamera(.10f);
            Require(camera.Position.Y > track.GroundOrRoadHeight(camera.Position) + .60f && camera.Basis.IsFinite(),
                $"{view} keeps the interpolated camera above the deck and terrain");
        }
        raceView = RaceView.Chase;
    }

    private void CheckCustomMountClearance(float slope)
    {
        foreach (var racer in racers)
        {
            racer.Speed = 25;
            for (int i = 0; i < 150; i++)
            {
                racer.Distance = i < 100 ? i * track.Length / 100 : slope + (i - 125) * .36f;
                racer.Lane = (i % 3 - 1) * (track.HalfWidth - RidgeRoad.CustomShoulder);
                racer.Lean = i % 2 == 0 ? -.85f : .85f;
                racer.Draw(track, 1f / 60, true, racer.Lean);
                Require(MountedRacer.RoadMeshLift(track, racer.Mount.BodyMeshes) < .004f &&
                    MountedRacer.RoadMeshLift(track, racer.Rider.BodyMeshes) < .004f,
                    "animated mounts and riders clear the deck at crests and through the lap seam");
            }
        }

        Vector3 deck = Player.CoursePosition(track);
        Player.Eject(track, Vector3.Zero, new Transform3D(Basis.Identity, deck - Vector3.Up * 3));
        Player.Tumble(track);
        Require(Player.Rider.GlobalPosition.Y > deck.Y && MountedRacer.RoadMeshLift(track, Player.Rider.BodyMeshes) < .004f,
            "a detached rider tumbles above the road instead of sinking to the ground underneath");
        Player.Reattach();
    }

    private (float Distance, int Side) FindDownhillShoulder()
    {
        (float Distance, int Side) deepest = default;
        float depth = float.NegativeInfinity;
        for (float at = BogCircuit.Grid + 12; at < track.Length; at += 3)
        foreach (int side in new[] { -1, 1 })
        {
            var edge = track.Surface(at, side * track.WidthAt(at));
            float gap = edge.Y - track.FreeGround(edge + track.Right(at) * (side * 1.65f)).Y;
            if (gap <= depth) continue;
            deepest = (at, side);
            depth = gap;
        }
        if (deepest.Side != 0) return deepest;
        throw new InvalidOperationException("The example track needs a hillside shoulder.");
    }

    private void CheckTrackFall(float distance, int side)
    {
        countdown = 0;
        Player.Distance = distance;
        Player.Lane = side * (track.WidthAt(distance) - .024f);
        Player.Speed = 21;
        lateral = side * 5;
        Player.Draw(track, 0, true);
        float checkpoint = Player.Checkpoint;
        int gates = Player.GatesCleared;
        const float step = 1f / 60;
        TickRace(step, true, false, side, false, false);
        Require(Player.Falling, "steering past the raised edge starts a fall");
        float height = Player.CoursePosition(track).Y;
        float initialTime = Player.FallTime;
        for (int frame = 0; frame < 14; frame++) TickRace(step, true, false, side, false, false);
        float age = Player.FallTime;
        float expectedDrop = .5f * RacePhysics.Gravity * (age * age - initialTime * initialTime);
        Require(Player.Falling && Math.Abs(height - Player.CoursePosition(track).Y - expectedDrop) < .025f &&
            Player.GatesCleared == gates, "the racer falls at Earth gravity before recall without passing a checkpoint");
        SetRaceCamera(1);
        Require(camera.Position.IsFinite() && camera.Basis.IsFinite(), "the camera remains stable during the fall");
        for (int frame = 0; frame < 90 && Player.Falling; frame++) TickRace(step, true, false, side, false, false);
        Require(!Player.Falling && !Player.FreeDriving && Player.ResetHold > 0 &&
            Mathf.IsEqualApprox(Player.Distance, checkpoint) && Player.GatesCleared == gates,
            "a fall restores the last checkpoint and keeps race progress");
    }

    private async Task CaptureTrackEditor(string folder)
    {
        SetUpExampleTrack();
        PlotExampleTrack();
        editorTool.Select(3);
        UpdateTrackPointer(new(-96, 168));
        await SaveFrame(Path.Combine(folder, "Dereth-Track-Editor.png"));
        TestCustomTrack();
        Require(racing && track.IsCustom, "the custom track starts for the deck capture");
        countdown = 0;
        var (distance, side) = FindDownhillShoulder();
        Player.Distance = distance;
        Player.Lane = side * (track.HalfWidth - 3);
        Player.Speed = 23;
        Player.Draw(track, .2f, true);
        raceView = RaceView.WideChase;
        SetRaceCamera(1);
        paused = true;
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Dereth-Custom-Track-Deck.png"));

        var shoulder = track.Surface(distance, side * track.HalfWidth);
        var sideward = track.Right(distance) * side;
        var forward = track.Forward(distance);
        raceUi.Hide();
        camera.Position = track.FreeGround(shoulder + sideward * 16 - forward * 9) + Vector3.Up * 4.5f;
        camera.Fov = 66;
        camera.LookAt(shoulder + forward * 5 - Vector3.Up);
        await SaveFrame(Path.Combine(folder, "Dereth-Custom-Track-Roadbed.png"));
    }
}
