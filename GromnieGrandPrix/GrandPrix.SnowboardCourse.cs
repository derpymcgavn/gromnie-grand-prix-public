using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const int MountEsperDownhillId = 13010, SnowreapSlalomId = 13011, CraterLakeLandmarkId = 11102;
    private const int SnowboardLayoutVersion = 1;
    private static readonly bool SnowboardingEnabled = false;
    private static readonly IReadOnlyDictionary<int, string> SnowboardCourses = new Dictionary<int, string>
    {
        [MountEsperDownhillId] = "Mount Esper Downhill",
        [SnowreapSlalomId] = "Snowreap Slalom"
    };

    private static readonly Vector2[] MountEsperDescent =
    [
        new(400, 256), new(415, 280), new(429, 329), new(412, 375), new(459, 451),
        new(519, 475), new(548, 515), new(588, 554), new(625, 607), new(620, 655),
        new(631, 694), new(640, 727), new(637, 750)
    ];

    private static readonly Vector2[] SnowreapDescent =
    [
        new(127, -335), new(113, -311), new(91, -294), new(34, -116), new(26, -89),
        new(29, -53), new(72, 11), new(90, 54), new(100, 110), new(103, 219),
        new(73, 268), new(36, 281), new(14, 295)
    ];

    private bool SnowboardCourseAvailable(int id) =>
        SnowboardingEnabled && SnowboardCourses.ContainsKey(id) && townRegion is not null && townCells is { } cells &&
        landmarkCircuits.TryGetValue(CraterLakeLandmarkId, out var crater) &&
        AcLandArena.NeighborIds(crater.Center, crater.GridSide).All(cells.Entries.ContainsKey);

    private BogCircuit BuildSnowboardCourse(Node3D scene, int id)
    {
        var crater = landmarkCircuits[CraterLakeLandmarkId];
        var land = AcLandArena.Load(crater.Center, townCells!, townRegion!, crater.GridSide);
        var path = id == MountEsperDownhillId ? MountEsperDescent : SnowreapDescent;
        float halfWidth = id == MountEsperDownhillId ? 17.5f : 18.5f;
        var route = new BogCircuit(SnowboardCourses[id], path, land, halfWidth, snowboardCourse: true);
        try
        {
            float drop = route.Point(0).Y - route.Point(route.Length).Y;
            if (route.Length < 560 || drop < 175 || route.TightestInsideRadius() < 33)
                throw new InvalidDataException($"{route.Name} needs a continuous downhill slope in the native terrain.");
            for (float at = 0; at <= route.Length; at += 3)
            {
                if (!DryInside(-route.HalfWidth) || !DryInside(0) || !DryInside(route.HalfWidth))
                    throw new InvalidDataException($"Deep water or the edge of the native grid crosses {route.Name} near {at:F0} m.");

                bool DryInside(float lane)
                {
                    Vector3 contact = route.Surface(at, lane);
                    return land.WorldBounds.Grow(-2).HasPoint(new(contact.X, contact.Z)) && route.NativeDry(contact);
                }
            }
            BuildTownLand(scene, land, assets, townCells!, route, measureScenery: true);
            var terrain = AcTerrainRenderer.For(assets, land.Region).Material;
            terrain.SetShaderParameter("course_mask", CourseRaster.TownMask(route));
            terrain.SetShaderParameter("course_origin", land.WorldBounds.Position);
            terrain.SetShaderParameter("course_extent", land.WorldBounds.Size);
            BuildSnowboardFlags(scene, route, id == MountEsperDownhillId);
            GD.Print($"{route.Name}: {route.Length:F0} m point to point, {drop:F0} m vertical drop through {land.Blocks.Count} native landblocks.");
            return route;
        }
        catch
        {
            route.Curve.Dispose();
            throw;
        }
    }

    private void BuildSnowboardFlags(Node3D scene, BogCircuit route, bool summit)
    {
        var markings = new Node3D { Name = "Downhill flags and fencing" };
        scene.AddChild(markings);
        Texture2D? native = RegattaPlateTexture();
        var post = RegattaFinish(native, "345976", .9f);
        var blue = RegattaFinish(native, summit ? "28a3df" : "597bdc", .91f, .33f);
        var orange = RegattaFinish(native, summit ? "ff942e" : "fb5a4f", .93f, .32f);
        var frost = RegattaFinish(native, "e7f7ff", .9f, .17f);
        var poles = new List<Transform3D>();
        var cloth = new List<Transform3D>[2] { [], [] };
        var stamps = new List<Transform3D>();
        var rails = new List<Transform3D>();
        var flagMesh = new BoxMesh { Size = new(.85f, .63f, .07f) };
        var stampMesh = new BoxMesh { Size = new(.65f, .095f, .098f) };
        var poleMesh = new BoxMesh { Size = new(.13f, 2.3f, .13f) };

        int index = 0;
        for (float at = BogCircuit.Grid + 18; at < route.Length - 8; at += 34)
        {
            Vector3 right = route.Right(at);
            float yaw = BogCircuit.Yaw(route.Forward(at));
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 foot = route.Surface(at, side * (route.HalfWidth + 1.40f));
                var flagBasis = Basis.FromEuler(new(0, yaw, side * -.085f));
                Vector3 banner = foot - right * (side * .36f) + Vector3.Up * 1.95f;
                poles.Add(new(Basis.Identity, foot + Vector3.Up * 1.15f));
                cloth[(index + (side > 0 ? 1 : 0)) % 2].Add(new(flagBasis, banner));
                stamps.Add(new(flagBasis, banner + Vector3.Up * .16f));
            }
            index++;
        }

        AddFence(BogCircuit.Grid - 13, -1);
        AddFence(BogCircuit.Grid - 13, 1);
        AddFence(route.Length - 62, -1);
        AddFence(route.Length - 62, 1);
        AddMarkerBatch(markings, poleMesh, post, poles);
        AddMarkerBatch(markings, flagMesh, blue, cloth[0]);
        AddMarkerBatch(markings, flagMesh, orange, cloth[1]);
        AddMarkerBatch(markings, stampMesh, frost, stamps);
        AddMarkerBatch(markings, new BoxMesh { Size = new(.14f, .23f, 1) }, orange, rails);

        void AddFence(float start, int side)
        {
            float lane = side * (route.HalfWidth + 3.2f);
            Vector3 previous = route.Surface(start, lane);
            for (int i = 0; i <= 7; i++)
            {
                Vector3 foot = route.Surface(start + i * 5.8f, lane);
                poles.Add(new(Basis.Identity.Scaled(new(1.3f, .57f, 1.3f)), foot + Vector3.Up * .66f));
                if (i > 0)
                {
                    Vector3 run = foot - previous;
                    var frame = Basis.LookingAt(run.Normalized()).ScaledLocal(new(1, 1, run.Length()));
                    rails.Add(new(frame, (foot + previous) * .5f + Vector3.Up * .8f));
                }
                previous = foot;
            }
        }
    }
}
