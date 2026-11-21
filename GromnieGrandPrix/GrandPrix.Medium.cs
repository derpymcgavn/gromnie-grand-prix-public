using Godot;
using DerethSurvivors;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

public partial class GrandPrix
{
    private void PlanMediumCourses(string folder, string output, bool verify, bool resume)
    {
        try
        {
            using var portal = new DatArchive(Path.Combine(folder, "client_portal.dat"));
            using var cells = new DatArchive(Path.Combine(folder, "client_cell_1.dat"));
            using var reader = new AcAssets(portal, folder);
            var region = AcWorldData.ReadRegion(portal.Read(0x13000000));
            var templates = JsonSerializer.Deserialize<XlRoute[]>(FileAccess.GetFileAsString("res://Data/xl-routes.json")) ?? [];
            var existing = JsonSerializer.Deserialize<TownRoute[]>(FileAccess.GetFileAsString("res://Data/town-routes.json")) ?? [];
            var ids = existing.Select(row => row.Id).Except(templates.Select(row => row.TownId)).ToHashSet();
            var towns = ReadTownCatalog().OutdoorDestinations.Where(town => ids.Contains(town.Id)).OrderBy(town => town.Id).ToArray();
            var routes = (verify || resume && System.IO.File.Exists(output)
                ? JsonSerializer.Deserialize<MediumRoute[]>(System.IO.File.ReadAllText(output)) ?? [] : []).ToDictionary(route => route.TownId);
            var wetness = new Dictionary<uint, float>();
            int passed = 0;
            foreach (var town in towns)
            {
                if (resume && !verify && routes.ContainsKey(town.Id)) { passed++; continue; }
                try
                {
                    MediumCourseStats stats;
                    if (verify)
                    {
                        if (!routes.TryGetValue(town.Id, out var route)) throw new InvalidDataException("No route in the catalog.");
                        var land = AcLandArena.Load(route.Center, cells, region, MediumRoute.Grid);
                        var track = route.Build(town.Name, land);
                        using var curve = track.Curve;
                        var scene = new Node3D();
                        AddChild(scene);
                        try { BuildTownLand(scene, land, reader, cells, track); }
                        finally { scene.Free(); }
                        if (!MediumRoute.Measure(track, land, XlRoute.TownPosition(town, land), out stats))
                            throw new InvalidDataException("The built route failed clearance.");
                    }
                    else
                    {
                        routes[town.Id] = PlanMediumTown(town, cells, reader, region, templates, wetness, out stats);
                        System.IO.File.WriteAllText(output, JsonSerializer.Serialize(routes.Values.OrderBy(row => row.TownId)));
                    }
                    GD.Print($"MEDIUM {town.Id} {town.Name}: {stats.Length:F0} m, {stats.Blocks}/49 blocks traversed, {stats.TownGap:F0} m to town, radius {stats.InsideRadius:F0} m, {stats.WetMetres:F0} m of water crossings.");
                    passed++;
                }
                catch (Exception error) { GD.PrintErr($"MEDIUM FAILED {town.Id} {town.Name}: {error.Message}"); }
                reader.TrimCaches();
            }
            GD.Print($"MEDIUM TOTAL {passed}/{towns.Length}");
            GetTree().Quit(passed == towns.Length && passed > 0 ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    private MediumRoute PlanMediumTown(PointOfInterestCatalog.Destination town, DatArchive cells, AcAssets assets,
        AcWorldData.Region region, XlRoute[] templates, Dictionary<uint, float> wetness, out MediumCourseStats stats)
    {
        MediumRoute? best = null;
        stats = default;
        foreach (uint center in MediumRoutePlanner.FindCenters(town.Landblock, cells, wetness).Take(8))
        {
            var land = AcLandArena.Load(center, cells, region, MediumRoute.Grid);
            var scene = new Node3D();
            AddChild(scene);
            try
            {
                land.Build(scene, assets, cells, static (_, _, _) => false);
                var candidate = MediumRoutePlanner.Plan(town, land, templates, out var measured);
                if (best is null || measured.WetMetres < stats.WetMetres) (best, stats) = (candidate, measured);
                if (stats.WetMetres < 750) break;
            }
            catch (InvalidDataException) { }
            finally { scene.Free(); }
        }
        return best ?? throw new InvalidDataException($"No checked medium circuit around {town.Name}.");
    }
}
