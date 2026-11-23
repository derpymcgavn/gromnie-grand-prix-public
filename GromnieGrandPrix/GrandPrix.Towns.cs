using Godot;
using DerethSurvivors;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

public partial class GrandPrix
{
    private DatArchive? townCells;
    private AcWorldData.Region? townRegion;
    private readonly Dictionary<int, PointOfInterestCatalog.Destination> townDestinations = [];
    private readonly Dictionary<int, MediumRoute> mediumRoutes = [];
    private readonly Dictionary<int, XlRoute> xlRoutes = [];
    private readonly Dictionary<int, RidgeCircuit> ridgeCircuits = [];
    private readonly Dictionary<int, LandmarkCircuit> landmarkCircuits = [];
    private SpanSprint? spanSprint;
    private const int XlCourseOffset = 10000, YanshiXlCourseId = XlCourseOffset + 101;
    private readonly Dictionary<int, float> bestByCourse = [];
    private int selectedTown = YanshiXlCourseId, activeTown;
    private string courseProblem = "";

    private string SelectedCourseName => CourseName(selectedTown);

    private string CourseName(int id)
    {
        if (VoidCourses.TryGetValue(id, out var voidCourse)) return voidCourse.Name;
        if (CustomCourse(id) is { } custom) return custom.Name;
        if (spanSprint?.Id == id) return spanSprint.Name;
        if (ridgeCircuits.TryGetValue(id, out var ridge)) return ridge.Name;
        if (landmarkCircuits.TryGetValue(id, out var landmark)) return landmark.Name + " XL";
        if (xlRoutes.TryGetValue(id, out var xl)) return townDestinations[xl.TownId].Name + " XL";
        return mediumRoutes.ContainsKey(id) ? townDestinations[id].Name + " Medium" : "Muckwater Circuit";
    }

    private void LoadTownCourses(string folder)
    {
        string path = Path.Combine(folder, "client_cell_1.dat");
        if (!System.IO.File.Exists(path)) throw new FileNotFoundException("Regional races require client_cell_1.dat.", path);
        townCells?.Dispose();
        townCells = null;
        townDestinations.Clear();
        mediumRoutes.Clear();
        xlRoutes.Clear();
        ridgeCircuits.Clear();
        landmarkCircuits.Clear();
        spanSprint = null;
        try
        {
            townCells = new DatArchive(path);
            townRegion = AcWorldData.ReadRegion(archive!.Read(0x13000000));
            LoadCustomTracks();
            var destinations = ReadTownCatalog().OutdoorDestinations.ToDictionary(town => town.Id);
            foreach (var route in JsonSerializer.Deserialize<XlRoute[]>(FileAccess.GetFileAsString("res://Data/xl-routes.json")) ?? [])
                if (destinations.TryGetValue(route.TownId, out var town) && AcLandArena.NeighborIds(route.Center, 8).All(townCells.Entries.ContainsKey))
                {
                    townDestinations.Add(town.Id, town);
                    xlRoutes.Add(XlCourseOffset + route.TownId, route);
                }
            foreach (var route in JsonSerializer.Deserialize<MediumRoute[]>(FileAccess.GetFileAsString("res://Data/medium-routes.json")) ?? [])
                if (!townDestinations.ContainsKey(route.TownId) && destinations.TryGetValue(route.TownId, out var town) &&
                    AcLandArena.NeighborIds(route.Center, MediumRoute.Grid).All(townCells.Entries.ContainsKey))
                {
                    townDestinations.Add(town.Id, town);
                    mediumRoutes.Add(town.Id, route);
                }
            foreach (var ridge in JsonSerializer.Deserialize<RidgeCircuit[]>(FileAccess.GetFileAsString("res://Data/obsidian-routes.json")) ?? [])
                if (AcLandArena.NeighborIds(ridge.Center, 8).All(townCells.Entries.ContainsKey))
                    ridgeCircuits.Add(ridge.Id, ridge);
            foreach (var circuit in JsonSerializer.Deserialize<LandmarkCircuit[]>(FileAccess.GetFileAsString("res://Data/landmark-routes.json")) ?? [])
                if (AcLandArena.NeighborIds(circuit.Center, circuit.GridSide).All(townCells.Entries.ContainsKey))
                    landmarkCircuits.Add(circuit.Id, circuit);
            var sprint = JsonSerializer.Deserialize<SpanSprint>(FileAccess.GetFileAsString("res://Data/obsidian-span-sprint.json"));
            if (sprint is not null && sprint.Blocks.All(townCells.Entries.ContainsKey)) spanSprint = sprint;
            if (townDestinations.Count + ridgeCircuits.Count + landmarkCircuits.Count == 0 && spanSprint is null)
                throw new InvalidDataException("No complete regional landblock grids are available in this client_cell_1.dat.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or JsonException)
        {
            townCells?.Dispose();
            townCells = null;
            townDestinations.Clear();
            mediumRoutes.Clear();
            xlRoutes.Clear();
            ridgeCircuits.Clear();
            landmarkCircuits.Clear();
            spanSprint = null;
            throw;
        }
        if (selectedTown is > 0 and < XlCourseOffset && xlRoutes.ContainsKey(XlCourseOffset + selectedTown))
            selectedTown += XlCourseOffset;
        if (!HasCourse(selectedTown))
            selectedTown = xlRoutes.Keys.Concat(mediumRoutes.Keys).Concat(ridgeCircuits.Keys)
                .Concat(landmarkCircuits.Keys).FirstOrDefault(spanSprint?.Id ?? 0);
        if (TestOriginalCourse) selectedTown = 0;
        best = bestByCourse.GetValueOrDefault(selectedTown, -1);
    }

    private bool TestOriginalCourse => OS.GetCmdlineUserArgs().Contains("--smoke-test");
    private bool HasCourse(int id) => VoidCourses.ContainsKey(id) || spanSprint?.Id == id || mediumRoutes.ContainsKey(id) || xlRoutes.ContainsKey(id) ||
        ridgeCircuits.ContainsKey(id) || landmarkCircuits.ContainsKey(id) || CustomCourse(id) is not null;

    private void ChooseCourse(int id)
    {
        if (!(id == 0 && testMode) && !HasCourse(id)) return;
        selectedTown = id;
        best = bestByCourse.GetValueOrDefault(id, -1);
        courseProblem = "";
        CloseCoursePicker();
        UpdateStableLabels();
        SaveGarage();
    }

    private bool PrepareRaceCourse()
    {
        if (selectedTown == 0 && !testMode) return false;
        var custom = CustomCourse(selectedTown);
        if (activeTown == selectedTown && ReferenceEquals(activeCustomCourse, custom) &&
            (!track.IsVoid || VoidBattleIsReady())) return true;
        BogCircuit nextTrack = muckTrack;
        Node3D nextScene = muckCourse;
        if (selectedTown != 0)
        {
            bool isVoid = VoidCourses.ContainsKey(selectedTown);
            XlRoute? xl = xlRoutes.GetValueOrDefault(selectedTown);
            RidgeCircuit? ridge = ridgeCircuits.GetValueOrDefault(selectedTown);
            LandmarkCircuit? landmarks = landmarkCircuits.GetValueOrDefault(selectedTown);
            MediumRoute? medium = mediumRoutes.GetValueOrDefault(selectedTown);
            SpanSprint? sprint = spanSprint?.Id == selectedTown ? spanSprint : null;
            var destination = townDestinations.GetValueOrDefault(xl?.TownId ?? selectedTown);
            string name = CourseName(selectedTown);
            nextScene = new Node3D { Name = name };
            nextScene.Hide();
            AddChild(nextScene);
            try
            {
                if (isVoid)
                {
                    nextTrack = CreateVoidCircuit(selectedTown);
                    BuildVoidCourse(nextScene, nextTrack, selectedTown != VoidCourseId);
                }
                else
                {
                    var land = sprint is not null ? AcLandArena.LoadCorridor(sprint.Center, sprint.Blocks, townCells!, townRegion!)
                        : AcLandArena.Load(custom?.Center ?? ridge?.Center ?? landmarks?.Center ?? xl?.Center ?? medium!.Center,
                            townCells!, townRegion!, custom?.GridSide ?? landmarks?.GridSide ?? (medium is not null ? MediumRoute.Grid : 8));
                    if (custom is not null)
                    {
                        nextTrack = custom.Build(land);
                        BuildTownLand(nextScene, land, assets, townCells!, nextTrack, measureScenery: true);
                        CustomTrack.ValidateScenery(nextTrack, land);
                    }
                    else if (sprint is not null)
                    {
                        nextTrack = sprint.Build(land);
                        BuildTownLand(nextScene, land, assets, townCells!, nextTrack);
                        BuildSpanRiver(nextScene, sprint, land);
                        GD.Print($"{name}: {land.Blocks.Count} native blocks, {nextTrack.Length:F0} m, Obsidian Span deck and Cragstone finish.");
                    }
                    else if (ridge is not null)
                    {
                        nextTrack = ridge.Build(land);
                        var (start, end) = ridge.FlightSector(nextTrack);
                        nextTrack.ReserveFlightSector(start, end);
                        BuildTownLand(nextScene, land, assets, townCells!, nextTrack);
                        RidgeCircuit.ValidateRoad(nextTrack, land);
                        if (!CheckSummitFlight(nextTrack, start, end))
                            throw new InvalidDataException($"The Black Hills flight path is blocked in {name}.");
                        GD.Print($"{name}: {land.Blocks.Count} native blocks, {nextTrack.Length:F0} m on the ground, launch {start:F0} m, landing {end:F0} m.");
                    }
                    else if (medium is not null)
                    {
                        nextTrack = medium.Build(name, land);
                        BuildTownLand(nextScene, land, assets, townCells!, nextTrack);
                        if (!MediumRoute.Measure(nextTrack, land, XlRoute.TownPosition(destination!, land), out var info))
                            throw new InvalidDataException($"No clear medium route in this copy of {name}.");
                        GD.Print($"{name}: {land.Blocks.Count} native blocks, {info.Length:F0} m per lap, {info.Blocks} blocks crossed, {info.TownGap:F0} m from town.");
                    }
                    else if (landmarks is not null)
                    {
                        nextTrack = landmarks.Build(land);
                        BuildTownLand(nextScene, land, assets, townCells!, nextTrack, measureScenery: true);
                        landmarks.Validate(nextTrack, land);
                        if (landmarks.Battle is { } battle)
                        {
                            var (start, end) = battle.FlightSector(nextTrack);
                            ValidateCitadelFlyover(nextTrack, land, battle, start, end);
                            nextTrack.ReserveFlightSector(start, end);
                        }
                        landmarks.BuildLandmarkSigns(nextScene, nextTrack);
                    }
                    else
                    {
                        nextTrack = new BogCircuit(name, xl!.Points(), land, xl.HalfWidth, gradedRoad: true);
                        BuildTownLand(nextScene, land, assets, townCells!, nextTrack);
                        if (!XlRoute.Validate(nextTrack, land, XlRoute.TownPosition(destination!, land), out var info))
                            throw new InvalidDataException($"No clear XL route in this copy of {name}.");
                        GD.Print($"{name}: {land.Blocks.Count} blocks loaded, {info.Length:F0} m per lap, {info.VisitedBlocks} blocks crossed ({info.InteriorBlocks} interior), all {info.Rows} rows and {info.Columns} columns reached, {info.TownGap:F0} m from town waypoint.");
                    }

                    var material = AcTerrainRenderer.For(assets, townRegion!).Material;
                    var mask = CourseRaster.TownMask(nextTrack);
                    material.SetShaderParameter("course_origin", land.WorldBounds.Position);
                    material.SetShaderParameter("course_extent", land.WorldBounds.Size);
                    material.SetShaderParameter("course_mask", mask);
                    nextTrack.BuildGradedRoad(nextScene, PackedRoadMaterial(land, mask));
                    nextTrack.BuildTownMarkers(nextScene);
                }
            }
            catch (Exception error)
            {
                if (nextTrack.IsCustom || nextTrack.IsVoid) nextTrack.Curve.Dispose();
                if (isVoid) ClearVoidCourse();
                RemoveChild(nextScene);
                nextScene.QueueFree();
                courseProblem = (custom is not null || isVoid) && error is InvalidDataException
                    ? error.Message : $"Couldn't load {name}. Pick another course.";
                GD.PushWarning(error.Message);
                UpdateStableLabels();
                return false;
            }
        }

        ClearRacers();
        ClearItemVisuals();
        course.Hide();
        if (courseItems is not null)
        {
            course.RemoveChild(courseItems);
            courseItems.QueueFree();
        }
        if (course != muckCourse)
        {
            RemoveChild(course);
            course.QueueFree();
        }
        if (track.IsCustom || track.IsVoid) track.Curve.Dispose();
        course = nextScene;
        track = nextTrack;
        if (!track.IsVoid) ClearVoidCourse();
        activeTown = selectedTown;
        activeCustomCourse = custom;
        BuildCourseItems();
        return true;
    }

    private ShaderMaterial PackedRoadMaterial(AcLandArena land, Texture2D mask)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/PackedRoad.gdshader") };
        var layers = land.Region.Blend?.Textures;
        var dirt = layers?.FirstOrDefault(layer => layer.Type == 7);
        var stoneRow = layers?.FirstOrDefault(layer => layer.Type == 32);
        var stone = stoneRow is null ? null : assets.Texture(stoneRow.Texture);
        var grit = dirt is null || AndroidGraphics ? null : assets.Texture(dirt.DetailTexture);
        var packed = dirt is null ? null : assets.Texture(dirt.Texture);
        if (packed is not null) material.SetShaderParameter("packed_dirt", packed);
        if (stone is not null) material.SetShaderParameter("road_stone", stone);
        if (grit is not null) material.SetShaderParameter("road_grit", grit);
        material.SetShaderParameter("road_detail", !AndroidGraphics);
        material.SetShaderParameter("course_mask", mask);
        material.SetShaderParameter("course_origin", land.WorldBounds.Position);
        material.SetShaderParameter("course_extent", land.WorldBounds.Size);
        AcTerrainRenderer.InitializeCloudShadow(material);
        return material;
    }

    private static void BuildTownLand(Node3D root, AcLandArena land, AcAssets assets, DatArchive cells, BogCircuit route,
        bool measureScenery = false)
    {
        var radii = new Dictionary<uint, float>();
        land.Build(root, assets, cells, (point, scale, model) =>
        {
            var projection = route.Project(new Vector3(point.X, 0, point.Y), 0);
            float distance = Math.Abs(projection.Lane);
            float margin = Math.Min(7f, scale * 3);
            float width = route.WidthAt(projection.Distance);
            if (distance <= width + 3.1f + margin || route.IsRegional && distance >= 255) return false;
            if (route.IsMedium || route.IsSprint || measureScenery)
            {
                if (!radii.TryGetValue(model, out float radius))
                    radii[model] = radius = assets.SceneryCollisionRadius(model);
                margin = Math.Max(margin, radius * scale);
            }
            return distance > width + 3.1f + margin;
        });
    }
    private void PlanTownRoutes(string folder, string output, bool resume, bool verify, int? preferredTour)
    {
        var rows = new List<TownRoute>();
        try
        {
            using var portal = new DatArchive(Path.Combine(folder, "client_portal.dat"));
            using var cells = new DatArchive(Path.Combine(folder, "client_cell_1.dat"));
            using var reader = new AcAssets(portal, folder);
            var region = AcWorldData.ReadRegion(portal.Read(0x13000000));
            var catalog = ReadTownCatalog();
            var previous = JsonSerializer.Deserialize<TownRoute[]>(FileAccess.GetFileAsString("res://Data/town-routes.json")) ?? [];
            if (resume && System.IO.File.Exists(output))
                rows.AddRange(JsonSerializer.Deserialize<TownRoute[]>(System.IO.File.ReadAllText(output)) ?? []);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var destinations = catalog.OutdoorDestinations.Where(town => preferredTour is null || town.Id == preferredTour).OrderBy(town => town.Id).ToArray();
            foreach (var town in destinations)
            {
                if (rows.Any(route => route.Id == town.Id)) continue;
                var root = new Node3D();
                AddChild(root);
                try
                {
                    var world = AcLandArena.Load(town.Landblock, cells, region);
                    var seed = previous.FirstOrDefault(route => route.Id == town.Id);
                    TownRoute route;
                    if (verify)
                    {
                        route = seed ?? throw new InvalidDataException("Missing town route.");
                        var track = new BogCircuit(town.Name, TownRoutePlanner.Points(route), world, route.HalfWidth);
                        using var verifyCurve = track.Curve;
                        BuildTownLand(root, world, reader, cells, track);
                        if (!TownRoutePlanner.Validate(track, world, route.HalfWidth, route.Compact))
                            throw new InvalidDataException("The full scenery failed the route clearance check.");
                        GD.Print($"TOWN COURSE {town.Name}: {track.Length:F0} m long, {route.HalfWidth * 2:F1} m wide, inside radius {track.TightestInsideRadius():F1} m, {world.Blocks.Count} blocks.");
                    }
                    else
                    {
                        world.Build(root, reader, cells, static (_, _, _) => false);
                        route = TownRoutePlanner.Plan(town, world, seed, resume, gentle: preferredTour is not null);
                        GD.Print($"TOWN ROUTE {town.Name}: {route.HalfWidth:F2}m half-width, {world.PlacedObjects} native props ({timer.Elapsed.TotalSeconds:F1}s).");
                    }
                    rows.Add(route);
                }
                catch (Exception error) { GD.PrintErr($"TOWN ROUTE FAILED {town.Name}: {error.Message}"); }
                finally { root.Free(); }
                reader.TrimCaches();
            }
            if (!verify) TownRoutePlanner.Write(output, rows.OrderBy(row => row.Id).ToArray());
            GD.Print($"TOWN ROUTE TOTAL {rows.Count}/{destinations.Length}");
            GetTree().Quit(destinations.Length > 0 && rows.Count == destinations.Length ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    private static PointOfInterestCatalog ReadTownCatalog() =>
        new(FileAccess.GetFileAsString("res://Data/town-destinations.json"));
}








