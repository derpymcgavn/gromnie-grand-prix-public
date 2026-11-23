using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const int VoidCourseId = 13003;
    private const int VoidFootworkCourseId = 13004, VoidAerbaxCourseId = 13005;
    private const string VoidCourseName = "Rynthid Singularity";
    private static readonly IReadOnlyDictionary<int, (string Name, string Details)> VoidCourses =
        new Dictionary<int, (string, string)>
        {
            [VoidCourseId] = (VoidCourseName, "23.1 km total  /  Singularity flyover"),
            [VoidFootworkCourseId] = ("Bael’Zharon’s Footwork", "Close passes around Bael’Zharon’s legs"),
            [VoidAerbaxCourseId] = ("Aerbax’s Orbit", "Low circuit around Aerbax’s dais")
        };
    private const float VoidSideStageScale = 1.7f;
    private static readonly Vector3 VoidAerbaxPosition = new(-505, 35, -84);
    private static readonly Vector3 VoidAsheronPosition = new(638, 62, 70);
    private readonly HashSet<int> warmedVoidFlightSkins = [];
    private static bool IsPreviousVoidCourse(int id) => id is 13001 or 13002;

    // The worn Rynthid Energy Field cloak uses render surface 0x05003311.
    private const uint RynthidEnergyTexture = 0x05003311;

    private static BogCircuit CreateVoidCircuit(int id)
    {
        Vector3[] points = id switch
        {
            VoidFootworkCourseId => VoidFootworkPoints(),
            VoidAerbaxCourseId => VoidAerbaxPoints(),
            _ => VoidSingularityPoints()
        };
        var circuit = new BogCircuit(VoidCourses[id].Name, [], null, 17, voidCorners: points);
        var flight = VoidFlyover(circuit);
        circuit.ReserveFlightSector(flight.Start, flight.End);
        return circuit;
    }

    private static Vector3[] VoidSingularityPoints()
    {
        const int anchors = 96;
        var points = new Vector3[anchors];
        for (int i = 0; i < anchors; i++)
        {
            float orbit = i * (2 * Mathf.Tau / anchors);
            float heading = orbit - .42f;
            // Two intertwined orbits separate vertically where their horizontal paths cross.
            float radius = 355 - 167 * Mathf.Cos(orbit * .5f) + 32 * Mathf.Sin(3 * orbit + .57f) + 16 * Mathf.Sin(2 * orbit - .8f);
            float altitude = 106 + 60 * Mathf.Sin(orbit * .5f) + 16 * Mathf.Sin(orbit + .8f) + 8 * Mathf.Cos(2 * orbit - 1.10f);
            points[i] = new Vector3(radius * Mathf.Cos(heading), altitude, radius * Mathf.Sin(heading));
        }
        return points;
    }

    // The low inner turn curls around the altar below the orbiting debris.
    private static Vector3[] VoidFootworkPoints() =>
    [
        new(365, 106, 275), new(525, 132, 134), new(569, 145, -60), new(470, 113, -275),
        new(271, 88, -424), new(26, 66, -462), new(-226, 48, -373), new(-344, 38, -207),
        new(-300, 29, -64), new(-190, 20, -39), new(-100, 16, -25), new(-77, 17, 45),
        new(-29, 19, 88), new(40, 22, 79), new(87, 25, 24), new(91, 29, -56),
        new(172, 41, -123), new(279, 55, -67), new(339, 66, 52), new(304, 73, 172),
        new(173, 81, 278), new(-5, 91, 356), new(-185, 96, 375), new(-302, 104, 479),
        new(-250, 121, 601), new(-65, 136, 648), new(155, 123, 520), new(248, 109, 373)
    ];

    // Descend from the outer bank, circle the charging dais, then climb back into open space.
    private static Vector3[] VoidAerbaxPoints() =>
    [
        new(265, 129, 51), new(260, 135, -143), new(150, 138, -314), new(-38, 119, -435),
        new(-267, 98, -447), new(-489, 72, -395), new(-653, 56, -283), new(-700, 49, -164),
        new(-675, 42, -89), new(-624, 34, -61), new(-613, 31, 12), new(-559, 35, 53),
        new(-479, 43, 58), new(-410, 49, 10), new(-377, 56, -61), new(-395, 63, -142),
        new(-343, 70, -230), new(-224, 88, -256), new(-65, 114, -221), new(103, 140, -161),
        new(154, 152, -48), new(149, 158, 72), new(82, 157, 179), new(-68, 151, 235),
        new(-223, 147, 285), new(-298, 139, 371),
        new(-214, 126, 489), new(-20, 120, 545), new(167, 139, 443), new(251, 143, 271)
    ];

    private void BuildVoidCourse(Node3D root, BogCircuit circuit, bool alternateLayout)
    {
        warmedVoidFlightSkins.Clear();
        var texture = assets.Texture(RynthidEnergyTexture)
            ?? throw new InvalidDataException("The client DAT is missing the Rynthid Energy Field cloak texture.");
        var energy = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/RynthidEnergyRoad.gdshader") };
        energy.SetShaderParameter("cloak_energy", texture);
        int sections = Mathf.CeilToInt(circuit.Length / VoidRoadSurface.MeshStep);
        int tiles = 2 * Mathf.CeilToInt(circuit.Length / 46);
        energy.SetShaderParameter("track_tiles", (float)tiles);
        var road = new Node3D { Name = "Rynthid energy track" };
        root.AddChild(road);
        BuildStrip("Rynthid cloak road", -circuit.HalfWidth, circuit.HalfWidth, 0, 0, Colors.White, Colors.White, energy);

        var mist = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true
        };
        Color lip = new(.66f, .15f, .49f, .36f), fade = new(.36f, .02f, .35f, 0);
        float edge = circuit.HalfWidth;
        BuildStrip("Inner energy fringe", -edge - 2.7f, -edge, -.75f, .015f, fade, lip, mist);
        BuildStrip("Outer energy fringe", edge, edge + 2.7f, .015f, -.75f, lip, fade, mist);
        BuildVoidRoadScenery(road, circuit);
        BuildVoidSetpiece(root, Vector3.Zero);
        BuildVoidAerbax(root, VoidAerbaxPosition);
        BuildVoidAsheron(root, VoidAsheronPosition);
        BuildVoidBattle(root, circuit, alternateLayout);
        BuildVoidSpectacle(root);

        void BuildStrip(string name, float left, float right, float leftY, float rightY, Color leftColor, Color rightColor, Material material)
        {
            using var mesh = new SurfaceTool();
            mesh.Begin(Mesh.PrimitiveType.Triangles);
            // Repeat the first cross section to wrap the scrolling cloak UVs at a whole tile.
            for (int section = 0; section <= sections; section++)
            {
                float along = section / (float)sections;
                float station = section == sections ? 0 : along * circuit.Length;
                Vertex(left, leftY, leftColor, 0);
                Vertex(right, rightY, rightColor, 1);
                if (section > 0)
                {
                    int start = (section - 1) * 2, end = section * 2;
                    mesh.AddIndex(start); mesh.AddIndex(end); mesh.AddIndex(start + 1);
                    mesh.AddIndex(start + 1); mesh.AddIndex(end); mesh.AddIndex(end + 1);
                }

                void Vertex(float lane, float height, Color color, float across)
                {
                    mesh.SetNormal(circuit.Right(station).Cross(circuit.Forward(station)).Normalized());
                    mesh.SetColor(color);
                    mesh.SetUV(new Vector2(across, along * tiles));
                    mesh.AddVertex(circuit.Surface(station, lane) + Vector3.Up * height);
                }
            }
            var strip = BogCircuit.MeshAt(road, mesh.Commit(), Vector3.Zero, Vector3.One, material);
            strip.Name = name;
            strip.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            if (material == energy) strip.Layers |= VoidReflectionSceneryLayer;
        }
    }

    private void ClearVoidCourse()
    {
        voidSummoning = null;
        voidAerbax = null;
        voidAsheron = null;
        voidBattle = null;
        ClearVoidSpectacle();
        warmedVoidFlightSkins.Clear();
    }

    private async void WarmVoidRaceGraphics()
    {
        bool firstVisit = warmedVoidFlightSkins.Count == 0;
        var flyers = new List<FlightMount>(3);
        foreach (int skin in new[] { skinIndex, RivalGromnatrossSkin })
            if (warmedVoidFlightSkins.Add(skin)) flyers.Add(new Gromnatross(assets, GromnieLook(skin)));
        if (firstVisit && creation.OlthoiSpitter is { } spitter) flyers.Add(new OlthoiSpitter(assets, spitter));
        if (flyers.Count == 0) return;
        if (DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var flyer in flyers) flyer.Root.Free();
            return;
        }

        // Submit real materials once during the start countdown so the first spell or flyer
        // on screen doesn't have to initialize its rendering pipeline during the race.
        var preview = new SubViewport
        {
            Name = "Void effects preparation", World3D = new World3D { Environment = weatherEnvironment }, Size = new Vector2I(56, 56),
            Msaa3D = GetViewport().Msaa3D, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        AddChild(preview);
        preview.AddChild(new DirectionalLight3D { Rotation = weatherSun.Rotation, ShadowEnabled = true });
        var eye = new Camera3D
        {
            Position = new Vector3(0, 8, 17), Current = true,
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 14
        };
        preview.AddChild(eye);
        eye.LookAt(Vector3.Zero);
        for (int i = 0; i < flyers.Count; i++)
        {
            var flyer = flyers[i];
            preview.AddChild(flyer.Root);
            flyer.Root.Scale = Vector3.One * .34f;
            flyer.Root.Position = new Vector3((i - 1) * 3.3f, -2, 0);
            flyer.Draw(0, .4f, 0);
        }
        if (firstVisit && voidBattle is { } battle)
        {
            var spell = battle.Attacks[0].Sites[0];
            AddEffect(spell.Mark, new Vector3(-4.5f, 3, 0));
            AddEffect(spell.Orb, new Vector3(-1.5f, 3, 0));
            AddEffect(spell.Beam, new Vector3(1.5f, 3, 0));
            AddEffect(spell.Burst, new Vector3(4.5f, 3, 0));
        }
        preview.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (GodotObject.IsInstanceValid(preview)) preview.QueueFree();

        void AddEffect(MeshInstance3D source, Vector3 position)
        {
            var bounds = source.Mesh.GetAabb();
            float scale = 1.55f / Mathf.Max(bounds.Size.X, Mathf.Max(bounds.Size.Y, bounds.Size.Z));
            preview.AddChild(new MeshInstance3D
            {
                Mesh = source.Mesh, MaterialOverride = source.MaterialOverride,
                Position = position - bounds.GetCenter() * scale, Scale = Vector3.One * scale,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }
    }

    private static void LightVoidActor(AcActor actor, uint lightLayer, Color fill, float energy)
    {
        actor.SetShadowCasting(false);
        var litMaterials = new Dictionary<StandardMaterial3D, StandardMaterial3D>();
        foreach (var mesh in actor.BodyMeshes)
        {
            mesh.Layers |= lightLayer;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D native) continue;
                if (!litMaterials.TryGetValue(native, out var material))
                {
                    material = (StandardMaterial3D)native.Duplicate();
                    material.EmissionEnabled = true;
                    material.Emission = native.AlbedoColor * fill;
                    material.EmissionTexture = native.AlbedoTexture;
                    material.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply;
                    material.EmissionEnergyMultiplier = energy;
                    litMaterials.Add(native, material);
                }
                mesh.SetSurfaceOverrideMaterial(surface, material);
            }
        }
    }

    private static void AddVoidRiderLight(Node3D rider)
    {
        rider.AddChild(new OmniLight3D
        {
            Name = "Reflected road glow",
            Position = new Vector3(0, .55f, 1.45f),
            LightColor = new Color("e77ade"),
            LightEnergy = 1.6f,
            OmniRange = 8f
        });
    }
}
