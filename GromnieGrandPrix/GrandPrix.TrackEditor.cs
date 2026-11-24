using System.Globalization;
using System.Text.Json;
using Godot;
using DerethSurvivors;
using Disk = System.IO.File;

public partial class GrandPrix
{
    private const string TrackFolder = "user://tracks";
    private readonly Dictionary<int, CustomTrack> customTracks = [];
    private CustomTrack? editorPreview, activeCustomCourse;
    private int editorReturnCourse;

    private Control trackEditorUi = null!;
    private TrackSketch sketch = null!;
    private Label editorStatus = null!, editorStats = null!, editorHeading = null!, editorArea = null!;
    private LineEdit editorName = null!, editorBlock = null!;
    private OptionButton editorSaved = null!, editorSize = null!, editorPlaces = null!, editorTool = null!,
        editorTravelType = null!;
    private SpinBox editorWidth = null!;
    private Button editorLoop = null!, editorStart = null!, editorSave = null!, editorTest = null!,
        editorUndo = null!, editorRedo = null!;
    private ConfirmationDialog discardTrackDraft = null!;
    private System.Action? editorDiscardAction;
    private AcLandArena? editorLand;
    private readonly List<Vector2> editorPoints = [];
    private readonly List<TrackTravelMode> editorModes = [];
    private readonly List<EditorTrackShape> undoTrack = [], redoTrack = [];
    private TrackTravelMode editorTravelMode;
    private int editorId, editorSelected = -1, editorRotation, editorSnap = 24;
    private bool editorClosed, editorDirty, editorLoading, editorDrag;
    private Vector2[]? editorDragBefore;
    private Vector2? editorPointer;
    private readonly record struct EditorTrackShape(Vector2[] Points, TrackTravelMode[] Modes, bool Closed);
    private static readonly int[] TrackSnaps = [0, 8, 12, 16, 24, 32, 48];
    private static readonly string[] Headings = ["N", "E", "S", "W"];
    private static readonly Vector2[][] TrackPieces =
    [
        [],
        [new(0, -2), new(0, -4)],
        [new(0, -2), new(-1, -4), new(-3, -5), new(-5, -5)],
        [new(0, -2), new(1, -4), new(3, -5), new(5, -5)],
        [new(0, -2), new(1, -4), new(3, -6), new(4, -8), new(4, -10)]
    ];

    private CustomTrack? CustomCourse(int id) => id switch
    {
        CustomTrack.PreviewId => editorPreview,
        OnlineCourseId => onlinePlan?.Route,
        _ => customTracks.GetValueOrDefault(id)
    };

    private void LoadCustomTracks()
    {
        customTracks.Clear();
        if (testMode) return;
        string folder = ProjectSettings.GlobalizePath(TrackFolder);
        string[] files;
        try { files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : []; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning("Couldn't open saved tracks. " + error.Message);
            return;
        }
        foreach (var file in files)
        {
            try
            {
                if (new FileInfo(file).Length > 65536) continue;
                if (JsonSerializer.Deserialize<CustomTrack>(Disk.ReadAllText(file)) is { IsValid: true } row &&
                    row.Id >= CustomTrack.FirstId && Path.GetFileNameWithoutExtension(file) == row.Id.ToString(CultureInfo.InvariantCulture))
                    customTracks.TryAdd(row.Id, row);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                GD.PushWarning("Couldn't read a custom track. " + error.Message);
            }
        }
    }

    private void MakeTrackEditorUi(CanvasLayer canvas)
    {
        trackEditorUi = Layer(canvas);
        var background = new ColorRect { Color = new("111c1b"), MouseFilter = Control.MouseFilterEnum.Stop };
        trackEditorUi.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Plate(trackEditorUi, 27, 25, 296, 750, "1a2b25", "657b61");
        Text(trackEditorUi, "TRACK EDITOR", 45, 43, 259, 27, Gold);
        Text(trackEditorUi, "SAVED TRACKS", 46, 91, 230, 13, Soft);
        editorSaved = EditorChoice(46, 110, 182, []);
        editorSaved.ItemSelected += row =>
        {
            int id = editorSaved.GetItemId((int)row);
            if (id != editorId && customTracks.TryGetValue(id, out var saved))
                ReplaceDraft(() => LoadTrackDraft(saved));
        };
        Action(trackEditorUi, "NEW", 236, 110, 69, 34, () => ReplaceDraft(() => LoadTrackDraft(null)));
        Text(trackEditorUi, "TRACK NAME", 46, 153, 230, 13, Soft);
        editorName = EditorField(46, 171, 259, 42, "Name your circuit");
        editorName.TextChanged += _ => { if (!editorLoading) editorDirty = true; };
        Text(trackEditorUi, "LAND BLOCK", 46, 220, 230, 13, Soft);
        editorBlock = EditorField(46, 239, 170, 10, "B9EB or B9EBFFFF");
        editorBlock.TextSubmitted += _ => ChangeEditorLocation();
        Action(trackEditorUi, "LOAD", 225, 239, 80, 34, ChangeEditorLocation);
        editorSize = EditorChoice(46, 286, 91, CustomTrack.Grids.Select(n => $"{n}×{n}"));
        editorSize.Select(1);
        editorSize.ItemSelected += _ => ChangeEditorLocation();
        editorPlaces = EditorChoice(146, 286, 159, ["Jump to area"]);
        editorPlaces.ItemSelected += index =>
        {
            if (index <= 0) return;
            uint center = (uint)editorPlaces.GetItemMetadata((int)index).AsInt64();
            editorPlaces.Select(0);
            editorBlock.Text = $"{center >> 16:X4}";
            ChangeEditorLocation();
        };

        Plate(trackEditorUi, 46, 335, 259, 1, "6b805b");
        Text(trackEditorUi, "PIECE", 46, 349, 135, 13, Soft);
        editorTool = EditorChoice(46, 369, 135, ["Spline point", "Straight", "Sweeper left", "Sweeper right", "S bend"]);
        editorTool.ItemSelected += _ => UpdateTrackPointer(editorPointer);
        Text(trackEditorUi, "SECTION TYPE", 190, 349, 115, 13, Soft);
        editorTravelType = EditorChoice(190, 369, 115, ["Ground", "Jet Ski", "Flight"]);
        editorTravelType.TooltipText = "Set the route from the selected point to the next point. New pieces use the selected section type.";
        editorTravelType.ItemSelected += index => ChangeTrackTravelMode((TrackTravelMode)index);
        Text(trackEditorUi, "GRID SNAP", 46, 415, 120, 13, Soft);
        Text(trackEditorUi, "ROAD WIDTH (M)", 179, 415, 127, 13, Soft);
        var snap = EditorChoice(46, 436, 120, TrackSnaps.Select(m => m == 0 ? "Off" : $"{m} m"));
        snap.Select(Array.IndexOf(TrackSnaps, editorSnap));
        snap.ItemSelected += index => { editorSnap = TrackSnaps[(int)index]; sketch.Snap = editorSnap; UpdateTrackPointer(editorPointer); };
        editorWidth = new SpinBox { Position = new(179, 436), Size = new(126, 34),
            MinValue = CustomTrack.MinWidth, MaxValue = CustomTrack.MaxWidth, Step = 2, Value = CustomTrack.DefaultWidth, Theme = ThemeForUi() };
        trackEditorUi.AddChild(editorWidth);
        editorWidth.GetLineEdit().MaxLength = 3;
        editorWidth.ValueChanged += _ =>
        {
            if (editorLoading) return;
            editorDirty = true;
            ReplotTrack();
        };
        Action(trackEditorUi, "Q  ↶", 46, 479, 67, 34, () => RotatePiece(-1));
        editorHeading = Text(trackEditorUi, "N  /  0°", 120, 487, 111, 15, Mint, HorizontalAlignment.Center);
        Action(trackEditorUi, "↷  E", 238, 479, 67, 34, () => RotatePiece(1));
        editorUndo = Action(trackEditorUi, "UNDO", 46, 522, 78, 34, () => RecallShape(undoTrack, redoTrack));
        editorRedo = Action(trackEditorUi, "REDO", 131, 522, 78, 34, () => RecallShape(redoTrack, undoTrack));
        editorStart = Action(trackEditorUi, "SET START", 216, 522, 89, 34, SetTrackStart);
        editorLoop = Action(trackEditorUi, "CLOSE LOOP", 46, 565, 259, 37, ToggleTrackLoop);
        editorStatus = Text(trackEditorUi, "", 46, 615, 259, 14, Soft);
        editorStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        editorSave = Action(trackEditorUi, "SAVE", 46, 689, 99, 41, SaveCustomTrack);
        editorTest = Action(trackEditorUi, "TEST DRIVE", 153, 685, 152, 49, TestCustomTrack, true);
        Action(trackEditorUi, "BACK TO STABLE", 46, 740, 259, 27, HideTrackEditor);

        Text(trackEditorUi, "DERETH  /  TERRAIN PLAN", 347, 36, 345, 17, Gold);
        editorArea = Text(trackEditorUi, "", 347, 61, 345, 14, Soft);
        Text(trackEditorUi, "GROUND", 706, 60, 76, 13, TrackSketch.ModeColor(TrackTravelMode.Ground));
        Text(trackEditorUi, "JET SKI", 790, 60, 76, 13, TrackSketch.ModeColor(TrackTravelMode.JetSki));
        Text(trackEditorUi, "FLIGHT", 874, 60, 76, 13, TrackSketch.ModeColor(TrackTravelMode.Flight));
        Action(trackEditorUi, "−", 1038, 37, 55, 39, () => sketch.Zoom(1 / 1.4f));
        Action(trackEditorUi, "+", 1101, 37, 55, 39, () => sketch.Zoom(1.4f));
        Action(trackEditorUi, "FIT", 1164, 37, 87, 39, () => sketch.Fit());
        sketch = new TrackSketch { Position = new(346, 91), Size = new(905, 650),
            MouseFilter = Control.MouseFilterEnum.Stop, ClipContents = true, Theme = ThemeForUi() };
        trackEditorUi.AddChild(sketch);
        sketch.Pressed += TrackMapPressed;
        sketch.Moved += MoveTrackPointer;
        sketch.Released += EndTrackDrag;
        sketch.Exited += () => UpdateTrackPointer(null);
        editorStats = Text(trackEditorUi, "", 347, 750, 260, 14, Mint);
        Text(trackEditorUi, "Wheel zoom   ·   Middle-drag pan   ·   Alt-click insert   ·   Right-click remove",
            600, 750, 650, 14, Soft, HorizontalAlignment.Right);
        discardTrackDraft = new ConfirmationDialog { Title = "Discard unsaved changes?", DialogText = "Switching drafts will discard your unsaved changes." };
        discardTrackDraft.Confirmed += () => { editorDiscardAction?.Invoke(); editorDiscardAction = null; };
        discardTrackDraft.Canceled += () => { editorDiscardAction = null; RefreshTrackPicker(); };
        trackEditorUi.AddChild(discardTrackDraft);
        trackEditorUi.Hide();
    }

    private OptionButton EditorChoice(int x, int y, int width, IEnumerable<string> entries)
    {
        var button = new OptionButton { Position = new(x, y), Size = new(width, 34),
            FitToLongestItem = false, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, Theme = ThemeForUi() };
        foreach (string entry in entries) button.AddItem(entry);
        trackEditorUi.AddChild(button);
        return button;
    }

    private LineEdit EditorField(int x, int y, int width, int limit, string placeholder)
    {
        var field = new LineEdit { Position = new(x, y), Size = new(width, 34), MaxLength = limit,
            PlaceholderText = placeholder, Theme = ThemeForUi() };
        trackEditorUi.AddChild(field);
        return field;
    }

    private void OpenTrackEditor()
    {
        if (!loaded || townCells is null || townRegion is null || racing) return;
        courseUi.Hide();
        if (editorLand is null || !editorDirty && customTracks.ContainsKey(selectedTown) && selectedTown != editorId)
            LoadTrackDraft(customTracks.GetValueOrDefault(selectedTown));
        ShowTrackEditor();
    }

    private void ShowTrackEditor()
    {
        stableUi.Hide();
        trackEditorUi.Show();
        RefreshTrackPicker();
        ReplotTrack();
    }

    private void HideTrackEditor()
    {
        EndTrackDrag();
        trackEditorUi.Hide();
        stableUi.Show();
        UpdateStableLabels();
    }

    private void ReplaceDraft(System.Action replace)
    {
        if (!editorDirty) { replace(); return; }
        editorDiscardAction = replace;
        discardTrackDraft.PopupCentered();
    }

    private void RefreshTrackPicker()
    {
        editorSaved.Clear();
        editorSaved.AddItem("Unsaved draft", 0);
        foreach (var row in customTracks.Values.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase))
            editorSaved.AddItem(row.Name, row.Id);
        editorSaved.Select(Math.Max(0, editorSaved.GetItemIndex(editorId)));
        if (editorPlaces.ItemCount > 1) return;
        var places = xlRoutes.Select(pair => (CourseName(pair.Key), pair.Value.Center))
            .Concat(mediumRoutes.Select(pair => (CourseName(pair.Key), pair.Value.Center)))
            .Concat(ridgeCircuits.Values.Select(row => (row.Name, row.Center)))
            .Concat(landmarkCircuits.Values.Select(row => (row.Name, row.Center)));
        foreach (var (name, center) in places.OrderBy(pair => pair.Item1, StringComparer.OrdinalIgnoreCase))
        {
            editorPlaces.AddItem(name);
            editorPlaces.SetItemMetadata(editorPlaces.ItemCount - 1, (long)center);
        }
    }

    private uint DefaultEditorCenter() =>
        xlRoutes.GetValueOrDefault(selectedTown)?.Center ?? mediumRoutes.GetValueOrDefault(selectedTown)?.Center ??
        landmarkCircuits.GetValueOrDefault(selectedTown)?.Center ?? ridgeCircuits.GetValueOrDefault(selectedTown)?.Center ??
        (spanSprint is { } span && span.Id == selectedTown ? (uint?)span.Center : null) ??
        xlRoutes.Values.FirstOrDefault()?.Center ?? townCells!.Entries.Keys.First(id => (id & 0xffff) == 0xffff);

    private void LoadTrackDraft(CustomTrack? saved)
    {
        uint center = saved?.Center ?? editorLand?.Block.Id ?? DefaultEditorCenter();
        int grid = saved?.GridSide ?? editorLand?.GridSide ?? 3;
        try
        {
            var land = AcLandArena.Load(center, townCells!, townRegion!, grid);
            if (saved is not null && saved.Controls().Any(point => !land.WorldBounds.HasPoint(point)))
                throw new InvalidDataException("Saved points are outside this track’s landblock grid.");
            sketch.SetTerrain(land, townCells!);
            editorLand = land;
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            EditorMessage(error.Message);
            RefreshTrackPicker();
            return;
        }
        editorLoading = true;
        editorId = saved?.Id ?? 0;
        editorPoints.Clear();
        editorModes.Clear();
        if (saved is not null) editorPoints.AddRange(saved.Controls());
        if (saved?.Modes is { } modes) editorModes.AddRange(modes);
        else editorModes.AddRange(Enumerable.Repeat(TrackTravelMode.Ground, editorPoints.Count));
        editorTravelMode = TrackTravelMode.Ground;
        editorTravelType.Select((int)editorTravelMode);
        editorName.Text = saved?.Name ?? "New Circuit";
        editorWidth.Value = saved?.Width ?? CustomTrack.DefaultWidth;
        editorClosed = saved is not null;
        editorDirty = false;
        editorSelected = -1;
        editorRotation = 0;
        undoTrack.Clear();
        redoTrack.Clear();
        editorLoading = false;
        UpdateTrackArea();
        EditorMessage(saved is null ? "Click to place points. Section Type sets the route from a point to the next one."
            : "Select a point to change its section type, or drag it to reshape the circuit.");
        RefreshTrackPicker();
        ReplotTrack();
    }

    private void ChangeEditorLocation()
    {
        string text = editorBlock.Text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (text.Length is not (4 or 8) || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint center))
        {
            EditorMessage("Enter a four-digit landblock hex ID, such as B9EB.");
            return;
        }
        center = ((text.Length == 4 ? center << 16 : center) & 0xffff0000) | 0xffff;
        int grid = CustomTrack.Grids[editorSize.Selected];
        if (editorLand?.Block.Id == center && editorLand.GridSide == grid)
        {
            UpdateTrackArea();
            return;
        }
        try
        {
            var land = AcLandArena.Load(center, townCells!, townRegion!, grid);
            uint prior = editorLand?.Block.Id ?? center;
            Vector2 shift = new(((int)(prior >> 24) - (int)(center >> 24)) * AcLandArena.BlockSize,
                ((int)((center >> 16) & 255) - (int)((prior >> 16) & 255)) * AcLandArena.BlockSize);
            var relocated = editorPoints.Select(p => p + shift).ToArray();
            if (relocated.Any(p => !land.WorldBounds.HasPoint(p)))
                throw new InvalidDataException("This grid leaves points outside the map. Pick a larger grid or start a new draft.");
            sketch.SetTerrain(land, townCells!);
            editorDirty |= editorId != 0 && (editorLand?.GridSide != grid || prior != center);
            editorLand = land;
            editorPoints.Clear();
            editorPoints.AddRange(relocated);
            undoTrack.Clear();
            redoTrack.Clear();
            UpdateTrackArea();
            ReplotTrack();
            EditorMessage("Terrain loaded. The larger lines mark original landblock boundaries.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            EditorMessage(error.Message);
            if (editorLand is not null) editorSize.Select(Array.IndexOf(CustomTrack.Grids, editorLand.GridSide));
        }
    }

    private void UpdateTrackArea()
    {
        if (editorLand is not { } land) return;
        editorBlock.Text = $"{land.Block.Id >> 16:X4}";
        editorSize.Select(Array.IndexOf(CustomTrack.Grids, land.GridSide));
        editorArea.Text = $"{land.Block.Id >> 16:X4}   ·   {land.GridSide} × {land.GridSide} native landblocks   ·   N ↑";
    }

    private bool HandleTrackEditorKey(Key code, InputEventKey key)
    {
        if (discardTrackDraft.Visible) return false;
        if (code == Key.Escape) { HideTrackEditor(); return true; }
        if (GetViewport().GuiGetFocusOwner() is LineEdit) return false;
        switch (code)
        {
            case Key.Q: RotatePiece(-1); return true;
            case Key.E: RotatePiece(1); return true;
            case Key.Delete: RemoveTrackPoint(editorSelected); return true;
            case Key.Z when key.CtrlPressed && !key.ShiftPressed: RecallShape(undoTrack, redoTrack); return true;
            case Key.Y when key.CtrlPressed:
            case Key.Z when key.CtrlPressed && key.ShiftPressed: RecallShape(redoTrack, undoTrack); return true;
            default: return false;
        }
    }

    private Vector2 SnapPoint(Vector2 point)
    {
        if (editorSnap == 0 || editorLand is null) return point;
        Vector2 origin = editorLand.WorldBounds.Position;
        return origin + ((point - origin) / editorSnap).Round() * editorSnap;
    }

    private bool InTrackArea(Vector2 point) => editorLand?.WorldBounds.Grow(-.15f).HasPoint(point) == true;

    private Vector2[] TrackPiece(Vector2 anchor)
    {
        float step = editorSnap == 0 ? 24 : editorSnap;
        return TrackPieces[editorTool.Selected].Select(point =>
        {
            var rotated = editorRotation switch
            {
                1 => new Vector2(-point.Y, point.X),
                2 => -point,
                3 => new Vector2(point.Y, -point.X),
                _ => point
            };
            return anchor + rotated * step;
        }).ToArray();
    }

    private void UpdateTrackPointer(Vector2? point)
    {
        editorPointer = point;
        sketch.Cursor = point is { } at ? SnapPoint(at) : null;
        sketch.PreviewMode = editorTravelMode;
        sketch.Ghost = [];
        sketch.GhostFits = sketch.Cursor is { } cursor && InTrackArea(cursor);
        if (point is not null && editorTool.Selected > 0 && sketch.Cursor is { } origin)
        {
            Vector2 anchor = editorPoints.Count == 0 ? origin : editorPoints[^1];
            sketch.Ghost = [anchor, .. TrackPiece(anchor)];
            sketch.GhostFits = sketch.Ghost.All(InTrackArea);
        }
        sketch.QueueRedraw();
    }

    private void MoveTrackPointer(Vector2 point)
    {
        if (editorDrag && editorSelected >= 0)
        {
            Vector2 snapped = SnapPoint(point);
            if (InTrackArea(snapped) && snapped != editorPoints[editorSelected] &&
                !editorPoints.Where((_, i) => i != editorSelected).Any(p => p.DistanceSquaredTo(snapped) < 1))
            {
                editorPoints[editorSelected] = snapped;
                ReplotTrack();
            }
        }
        UpdateTrackPointer(point);
    }

    private int TrackHit(Vector2 point)
    {
        var local = sketch.ToScreen(point);
        int hit = -1;
        float nearest = 12 * 12;
        for (int i = 0; i < editorPoints.Count; i++)
        {
            float gap = local.DistanceSquaredTo(sketch.ToScreen(editorPoints[i]));
            if (gap >= nearest) continue;
            nearest = gap;
            hit = i;
        }
        return hit;
    }

    private void TrackMapPressed(Vector2 point, InputEventMouseButton click)
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        int hit = TrackHit(point);
        if (click.ButtonIndex == MouseButton.Right) { RemoveTrackPoint(hit); return; }
        if (editorTool.Selected == 0 && hit >= 0 && !click.AltPressed)
        {
            if (hit == 0 && click.DoubleClick && !editorClosed && editorPoints.Count >= 4)
            {
                ToggleTrackLoop();
                return;
            }
            editorDrag = true;
            editorDragBefore = editorPoints.ToArray();
            editorSelected = hit;
            SyncTrackTravelMode();
            ReplotTrack();
            return;
        }
        Vector2 snapped = SnapPoint(point);
        if (editorTool.Selected > 0)
        {
            Vector2 anchor = editorPoints.Count == 0 ? snapped : editorPoints[^1];
            Vector2[] piece = editorPoints.Count == 0 ? [anchor, .. TrackPiece(anchor)] : TrackPiece(anchor);
            if (!piece.All(InTrackArea)) { EditorMessage("That piece runs outside the terrain. Rotate it or load a larger grid."); return; }
            bool closes = !editorClosed && editorPoints.Count > 0 && editorPoints.Count + piece.Length >= 5 &&
                piece[^1].DistanceSquaredTo(editorPoints[0]) < 1;
            if (closes) piece = piece[..^1];
            if (piece.Any(p => editorPoints.Any(existing => p.DistanceSquaredTo(existing) < 1)))
            {
                EditorMessage("That piece meets an existing point before its tip. Rotate it or use spline points.");
                return;
            }
            if (editorPoints.Count + piece.Length > CustomTrack.MaxPoints) { TrackPointLimit(); return; }
            RecordTrackEdit();
            if (editorModes.Count > 0) editorModes[^1] = editorTravelMode;
            editorPoints.AddRange(piece);
            editorModes.AddRange(Enumerable.Repeat(editorTravelMode, piece.Length));
            editorSelected = editorPoints.Count - 1;
            if (editorTool.Selected is 2 or 3) RotatePiece(editorTool.Selected == 2 ? -1 : 1);
            if (closes) editorClosed = true;
            EditorMessage(closes ? "Piece joined to the start. The loop is closed." : "Piece placed. The next piece starts at its tip.");
        }
        else
        {
            if (!InTrackArea(snapped)) return;
            if (editorPoints.Any(p => p.DistanceSquaredTo(snapped) < 1)) return;
            if (editorPoints.Count >= CustomTrack.MaxPoints) { TrackPointLimit(); return; }
            int insertAt = editorPoints.Count;
            if (click.AltPressed && editorPoints.Count > 1)
            {
                float closest = float.PositiveInfinity;
                int segments = editorClosed ? editorPoints.Count : editorPoints.Count - 1;
                for (int i = 0; i < segments; i++)
                {
                    Vector2 a = editorPoints[i], b = editorPoints[(i + 1) % editorPoints.Count];
                    float gap = point.DistanceSquaredTo(Geometry2D.GetClosestPointToSegment(point, a, b));
                    if (gap >= closest) continue;
                    closest = gap;
                    insertAt = i + 1;
                }
            }
            RecordTrackEdit();
            TrackTravelMode mode = click.AltPressed && editorPoints.Count > 1
                ? editorModes[insertAt - 1] : editorTravelMode;
            if (!click.AltPressed && !editorClosed && editorModes.Count > 0) editorModes[^1] = mode;
            editorPoints.Insert(insertAt, snapped);
            editorModes.Insert(insertAt, mode);
            editorSelected = insertAt;
            SyncTrackTravelMode();
            EditorMessage(editorClosed ? "Spline point added." :
                editorPoints.Count >= 4 ? "Use Close Loop or double-click the first point." : "Place at least four spline points.");
        }
        ReplotTrack();
    }

    private void EndTrackDrag()
    {
        if (!editorDrag) return;
        editorDrag = false;
        if (editorDragBefore is { } before && !editorPoints.SequenceEqual(before)) RecordTrackEdit(before);
        editorDragBefore = null;
        ReplotTrack();
    }

    private void RotatePiece(int direction)
    {
        editorRotation = (editorRotation + direction + 4) % 4;
        editorHeading.Text = $"{Headings[editorRotation]}  /  {90 * editorRotation}°";
        UpdateTrackPointer(editorPointer);
    }

    private void ChangeTrackTravelMode(TrackTravelMode mode)
    {
        EndTrackDrag();
        editorTravelMode = mode;
        if (editorSelected >= 0 && editorModes[editorSelected] != mode)
        {
            RecordTrackEdit();
            editorModes[editorSelected] = mode;
        }
        SyncTrackTravelMode();
        ReplotTrack();
    }

    private void SyncTrackTravelMode()
    {
        if (editorSelected >= 0) editorTravelMode = editorModes[editorSelected];
        editorTravelType.Select((int)editorTravelMode);
    }

    private void RemoveTrackPoint(int index)
    {
        if (index < 0 || index >= editorPoints.Count) return;
        RecordTrackEdit();
        editorPoints.RemoveAt(index);
        editorModes.RemoveAt(index);
        editorSelected = -1;
        if (editorPoints.Count < 4) editorClosed = false;
        ReplotTrack();
    }

    private void SetTrackStart()
    {
        if (editorSelected <= 0 || !editorClosed) return;
        RecordTrackEdit();
        var start = editorPoints.Take(editorSelected).ToArray();
        var modes = editorModes.Take(editorSelected).ToArray();
        editorPoints.RemoveRange(0, editorSelected);
        editorModes.RemoveRange(0, editorSelected);
        editorPoints.AddRange(start);
        editorModes.AddRange(modes);
        editorSelected = 0;
        SyncTrackTravelMode();
        ReplotTrack();
        EditorMessage("Start moved to the selected point’s section.");
    }

    private void ToggleTrackLoop()
    {
        if (editorPoints.Count < 4) return;
        RecordTrackEdit();
        editorClosed = !editorClosed;
        ReplotTrack();
        EditorMessage(editorClosed ? "Loop closed. Test drive to check the gates and native scenery." : "Loop reopened.");
    }

    private void TrackPointLimit() => EditorMessage($"This track has room for {CustomTrack.MaxPoints} spline points.");

    private void RecordTrackEdit(Vector2[]? before = null)
    {
        undoTrack.Add(new(before ?? editorPoints.ToArray(), editorModes.ToArray(), editorClosed));
        if (undoTrack.Count > 64) undoTrack.RemoveAt(0);
        redoTrack.Clear();
        editorDirty = true;
    }

    private void RecallShape(List<EditorTrackShape> from, List<EditorTrackShape> to)
    {
        if (from.Count == 0) return;
        to.Add(new(editorPoints.ToArray(), editorModes.ToArray(), editorClosed));
        var shape = from[^1];
        from.RemoveAt(from.Count - 1);
        editorPoints.Clear();
        editorPoints.AddRange(shape.Points);
        editorModes.Clear();
        editorModes.AddRange(shape.Modes);
        editorClosed = shape.Closed;
        editorSelected = -1;
        editorDirty = true;
        ReplotTrack();
    }

    private void ReplotTrack()
    {
        float spacing = Math.Max(2.2f, CustomTrack.ControlLength(editorPoints, editorClosed) / 3800);
        using var spline = BogCircuit.MakeSpline(editorPoints, editorClosed, spacing);
        float length = spline.GetBakedLength();
        sketch.Guides = editorPoints.ToArray();
        sketch.Road = spline.GetBakedPoints().Select(point => new Vector2(point.X, point.Z)).ToArray();
        sketch.Sections = PlotTravelSections(spline, spacing);
        sketch.GuideModes = editorModes.ToArray();
        sketch.Selected = editorSelected;
        sketch.RoadWidth = (float)editorWidth.Value;
        sketch.Start = null;
        if (editorClosed && length > BogCircuit.Grid + 2)
        {
            var origin = spline.SampleBaked(BogCircuit.Grid, true);
            var ahead = spline.SampleBaked(BogCircuit.Grid + 2, true) - origin;
            sketch.Start = new Vector2(origin.X, origin.Z);
            sketch.StartRight = new Vector2(-ahead.Z, ahead.X).Normalized();
        }
        editorLoop.Disabled = editorPoints.Count < 4;
        editorLoop.Text = editorClosed ? "REOPEN LOOP" : "CLOSE LOOP";
        editorStart.Disabled = !editorClosed || editorSelected <= 0;
        editorSave.Disabled = editorTest.Disabled = !editorClosed;
        editorUndo.Disabled = undoTrack.Count == 0;
        editorRedo.Disabled = redoTrack.Count == 0;
        editorStats.Text = $"{editorPoints.Count} points   ·   {length:0} m   ·   {(editorClosed ? "closed" : "open")}";
        editorHeading.Text = $"{Headings[editorRotation]}  /  {90 * editorRotation}°";
        UpdateTrackPointer(editorPointer);
    }

    private (TrackTravelMode Mode, Vector2[] Line)[] PlotTravelSections(Curve3D spline, float spacing)
    {
        if (!editorModes.Any(mode => mode != TrackTravelMode.Ground)) return [];
        var sections = new (TrackTravelMode Mode, Vector2[] Line)[Math.Max(0, spline.PointCount - 1)];
        for (int i = 0; i < sections.Length; i++)
        {
            Vector3 a = spline.GetPointPosition(i), b = spline.GetPointPosition(i + 1);
            Vector3 lead = a + spline.GetPointOut(i), entry = b + spline.GetPointIn(i + 1);
            float controlLength = a.DistanceTo(lead) + lead.DistanceTo(entry) + entry.DistanceTo(b);
            int count = Math.Max(2, Mathf.CeilToInt(controlLength / spacing));
            var line = new Vector2[count + 1];
            for (int step = 0; step <= count; step++)
            {
                Vector3 point = spline.Sample(i, step / (float)count);
                line[step] = new Vector2(point.X, point.Z);
            }
            sections[i] = (editorModes[i], line);
        }
        return sections;
    }

    private CustomTrack MakeCustomTrack(int id)
    {
        if (editorLand is null || !editorClosed) throw new InvalidDataException("Close the circuit before racing.");
        string name = editorName.Text.Trim();
        if (name.Length == 0) throw new InvalidDataException("Give your circuit a name.");
        return new CustomTrack(id, name, editorLand.Block.Id, editorLand.GridSide, (float)editorWidth.Value,
            editorPoints.Select(point => new[] { point.X, point.Y }).ToArray(),
            Modes: editorModes.Any(mode => mode != TrackTravelMode.Ground) ? editorModes.ToArray() : null);
    }

    private void SaveCustomTrack()
    {
        string folder = ProjectSettings.GlobalizePath(TrackFolder);
        try
        {
            Directory.CreateDirectory(folder);
            int id = editorId == 0 ? CustomTrack.FirstId : editorId;
            string FileName() => Path.Combine(folder, id.ToString(CultureInfo.InvariantCulture) + ".json");
            if (editorId == 0)
                while (customTracks.ContainsKey(id) || Disk.Exists(FileName())) id++;
            var draft = MakeCustomTrack(id);
            var course = draft.Build(editorLand!);
            using var spline = course.Curve;
            string file = FileName();
            Disk.WriteAllText(file + ".tmp", JsonSerializer.Serialize(draft));
            Disk.Move(file + ".tmp", file, overwrite: true);
            if (customTracks.TryGetValue(id, out var prior) &&
                (prior.Center != draft.Center || prior.GridSide != draft.GridSide || prior.Width != draft.Width ||
                 !prior.Controls().SequenceEqual(draft.Controls()) ||
                 !(prior.Modes ?? []).SequenceEqual(draft.Modes ?? [])))
            {
                bestByCourse.Remove(id);
                rivalLines.Forget(id);
            }
            customTracks[id] = draft;
            editorId = id;
            editorDirty = false;
            ChooseCourse(id);
            RefreshTrackPicker();
            EditorMessage($"Saved {draft.Name}. It’s ready in the course picker.");
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            EditorMessage(error.Message);
        }
    }

    private void TestCustomTrack()
    {
        try { editorPreview = MakeCustomTrack(CustomTrack.PreviewId); }
        catch (InvalidDataException error) { EditorMessage(error.Message); return; }
        editorReturnCourse = selectedTown;
        selectedTown = CustomTrack.PreviewId;
        courseProblem = "";
        trackEditorUi.Hide();
        StartRace();
        if (!racing)
        {
            EditorMessage(courseProblem);
            ReturnFromTrackTest();
            ShowTrackEditor();
        }
    }

    private void ReturnFromTrackTest()
    {
        selectedTown = editorReturnCourse;
        editorPreview = null;
        courseProblem = "";
        best = bestByCourse.GetValueOrDefault(selectedTown, -1);
    }

    private void EditorMessage(string message) => editorStatus.Text = message;
}
