using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const int VoidCourseId = 13003;
    private const string VoidCourseName = "Rynthid Singularity";
    private const float VoidSideStageScale = 1.7f;
    private static readonly Vector3 VoidAerbaxPosition = new(-505, 35, -84);
    private static readonly Vector3 VoidAsheronPosition = new(638, 62, 70);
    private readonly HashSet<int> warmedVoidFlightSkins = [];
    private static bool IsPreviousVoidCourse(int id) => id is 13001 or 13002;

    // The worn Rynthid Energy Field cloak uses render surface 0x05003311.
    private const uint RynthidEnergyTexture = 0x05003311;

    private static BogCircuit CreateVoidCircuit()
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
        var circuit = new BogCircuit(VoidCourseName, [], null, 17, voidCorners: points);
        var flight = VoidFlyover(circuit);
        circuit.ReserveFlightSector(flight.Start, flight.End);
        return circuit;
    }

    private void BuildVoidCourse(Node3D root, BogCircuit circuit)
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
        BuildVoidBattle(root, circuit);
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
