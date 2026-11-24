using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const int DismountCourseId = 13008, DismountBaseTownId = 81;
    private const int DismountWaypointCount = 48;
    private const string DismountCourseName = "Last Legs Sprint";
    private bool DismountCourseAvailable => townCells is not null && townRegion is not null &&
        mediumRoutes.TryGetValue(DismountBaseTownId, out var route) && route.Waypoints.Length >= DismountWaypointCount;

    private BogCircuit BuildDismountCourse(Node3D scene)
    {
        var neydisa = mediumRoutes[DismountBaseTownId];
        var land = AcLandArena.Load(neydisa.Center, townCells!, townRegion!, MediumRoute.Grid);
        // Open the validated Neydisa route before its last turn back to town.
        var points = neydisa.Waypoints.Take(DismountWaypointCount).Select(p => new Vector2(p[0], p[1])).ToArray();
        var circuit = new BogCircuit(DismountCourseName, points, land, neydisa.HalfWidth, gradedRoad: true, dismountSprint: true);
        BuildTownLand(scene, land, assets, townCells!, circuit, measureScenery: true);

        var mask = CourseRaster.TownMask(circuit);
        var terrain = AcTerrainRenderer.For(assets, townRegion!).Material;
        terrain.SetShaderParameter("course_origin", land.WorldBounds.Position);
        terrain.SetShaderParameter("course_extent", land.WorldBounds.Size);
        terrain.SetShaderParameter("course_mask", mask);
        circuit.BuildGradedRoad(scene, PackedRoadMaterial(land, mask));
        BuildDismountScenery(scene, circuit);
        GD.Print($"{DismountCourseName}: {land.Blocks.Count} native blocks, {circuit.Length:F0} m, road ends at {circuit.DismountLine:F0} m.");
        return circuit;
    }

    private static void BuildDismountScenery(Node3D scene, BogCircuit circuit)
    {
        var timber = BogCircuit.Paint("4e392b");
        var amber = BogCircuit.Paint("e6a74c");
        var teal = BogCircuit.Paint("56c4ac");
        float line = circuit.DismountLine;

        AddRoadEndWarning(line - 72, 1, "ROAD ENDS AHEAD");
        AddRoadEndWarning(line - 31, -1, "DISMOUNT AHEAD");
        var crossing = new Node3D { Name = "Dismount crossing" };
        scene.AddChild(crossing);
        float signLane = circuit.HalfWidth + 1.25f;
        Vector3 left = circuit.NativeSurface(line, -signLane);
        Vector3 right = circuit.NativeSurface(line, signLane);
        Vector3 center = circuit.Point(line);
        float headroom = Math.Max(center.Y, Math.Max(left.Y, right.Y)) + 5.5f;
        foreach (var footing in new[] { left, right })
        {
            float height = headroom - footing.Y;
            BogCircuit.Box(crossing, footing + Vector3.Up * (height * .5f), new(.38f, height, .38f), timber);
            BogCircuit.Box(crossing, footing + Vector3.Up * .25f, new(.72f, .5f, .72f), amber);
        }

        var arch = new Node3D
        {
            Position = center with { Y = headroom },
            Rotation = new(0, BogCircuit.Yaw(circuit.Forward(line)), 0)
        };
        crossing.AddChild(arch);
        BogCircuit.Box(arch, Vector3.Zero, new(2 * signLane + .4f, .30f, .38f), timber);
        AddDismountPlaque(arch, .93f, 9.1f, 1.4f, "ROAD ENDS", timber, amber, new("281b0f"));
        AddDismountPlaque(arch, -.55f, 8.3f, .80f, "ON FOOT TO THE FINISH", timber, teal, new("142924"));

        var stone = BogCircuit.Paint("82715c");
        var rubble = new PrismMesh { Size = new(1.14f, .34f, 1.08f) };
        foreach (int side in new[] { -1, 1 })
            for (int i = 0; i < 8; i++)
            {
                float at = line + (i - 3) * 1.58f;
                float lane = side * (circuit.HalfWidth - .75f + (i % 3) * .88f);
                Vector3 ground = circuit.Surface(at, lane);
                float size = .58f + (i % 4) * .17f;
                var chip = BogCircuit.MeshAt(crossing, rubble, ground + Vector3.Up * .06f, Vector3.One * size, stone);
                chip.Rotation = new(.08f * side, i * 2.48f, -.07f * side);
            }

        var pole = new BoxMesh { Size = new(.11f, 2.14f, .11f) };
        var flag = new BoxMesh { Size = new(.78f, .52f, .065f) };
        AddTrailMarkers("Riding course markers", BogCircuit.Grid + 44, line - 38, 56, circuit.HalfWidth + .45f, amber);
        AddTrailMarkers("Foot trail markers", line + 11, circuit.Length - 12, 49, circuit.HalfWidth - 1.35f, teal);

        const int startTiles = 12;
        float tileWidth = circuit.HalfWidth * 2 / startTiles;
        for (int tile = 0; tile < startTiles; tile++)
            for (int row = 0; row < 2; row++)
                circuit.Ribbon(scene, tile * tileWidth - circuit.HalfWidth, (tile + 1) * tileWidth - circuit.HalfWidth,
                    .09f, (tile + row) % 2 == 0 ? "fae6b1" : "3d3a2d", BogCircuit.Grid + row * .77f, BogCircuit.Grid + (row + 1) * .77f);

        void AddRoadEndWarning(float at, int side, string message)
        {
            var sign = new Node3D
            {
                Name = "Road end warning",
                Position = circuit.NativeSurface(at, side * (circuit.HalfWidth + 1.7f)),
                Rotation = new(0, BogCircuit.Yaw(circuit.Forward(at)) - side * .15f, 0)
            };
            scene.AddChild(sign);
            BogCircuit.Box(sign, new(0, 1.5f, 0), new(.18f, 3, .19f), timber);
            AddDismountPlaque(sign, 2.65f, 4.7f, .82f, message, timber, amber, new("281b0f"));
        }

        void AddTrailMarkers(string name, float start, float end, float spacing, float lane, Material color)
        {
            var group = new Node3D { Name = name };
            scene.AddChild(group);
            var posts = new List<Transform3D>();
            var pennants = new List<Transform3D>();
            for (float at = start; at < end; at += spacing)
            {
                float yaw = BogCircuit.Yaw(circuit.Forward(at));
                Vector3 across = circuit.Right(at);
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 point = circuit.NativeSurface(at, lane * side);
                    posts.Add(new Transform3D(Basis.Identity, point + Vector3.Up * 1.07f));
                    pennants.Add(new Transform3D(Basis.FromEuler(new(0, yaw, -.10f * side)),
                        point + Vector3.Up * 1.86f + across * (.35f * side)));
                }
            }
            AddMarkerBatch(group, pole, timber, posts);
            AddMarkerBatch(group, flag, color, pennants);
        }
    }

    private static void AddMarkerBatch(Node3D parent, Mesh mesh, Material material, IReadOnlyList<Transform3D> poses)
    {
        var batch = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = poses.Count
        };
        for (int i = 0; i < poses.Count; i++) batch.SetInstanceTransform(i, poses[i]);
        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = batch, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }

    private static void AddDismountPlaque(Node3D parent, float y, float width, float height, string message,
        Material frame, Material board, Color ink)
    {
        BogCircuit.Box(parent, new(0, y, 0), new(width, height, .19f), frame);
        BogCircuit.Box(parent, new(0, y, .105f), new(width - .18f, height - .15f, .045f), board);
        parent.AddChild(new Label3D
        {
            Text = message,
            Position = new(0, y, .139f),
            FontSize = 80,
            PixelSize = height * .0093f,
            OutlineSize = 0,
            Modulate = ink,
            Shaded = false,
            DoubleSided = false
        });
    }
}
