using Godot;

public partial class GrandPrix
{
    private const int KaleidoscopeCourseId = 13007;
    private const string KaleidoscopeCourseName = "Rynthid Kaleidoscope";
    private const uint KaleidoscopeMirrorLayer = 1u << 7;
    private const float KaleidoscopeProbeInterval = .55f;
    private Node3D? kaleidoscopeRoot;
    private ReflectionProbe? kaleidoscopeProbe;
    private float kaleidoscopeProbeCooldown;
    private bool kaleidoscopeProbeFallback = true;

    private static BogCircuit CreateKaleidoscopeCircuit()
    {
        const int anchors = 120;
        var points = new Vector3[anchors];
        for (int i = 0; i < anchors; i++)
        {
            float angle = i * Mathf.Tau / anchors + .31f;
            float radius = 645 + 66 * Mathf.Cos(3 * angle + .4f) + 48 * Mathf.Sin(2 * angle - .1f)
                + 38 * Mathf.Cos(5 * angle + .92f);
            float altitude = 126 + 57 * Mathf.Cos(2 * angle + .33f) + 27 * Mathf.Sin(3 * angle - 1.5f)
                + 10 * Mathf.Sin(5 * angle + .22f);
            points[i] = new Vector3(radius * 1.12f * Mathf.Cos(angle), altitude, radius * .91f * Mathf.Sin(angle));
        }
        return new BogCircuit(KaleidoscopeCourseName, [], null, 17, voidCorners: points, voidStyle: VoidCircuitStyle.Kaleidoscope);
    }

    private bool KaleidoscopeCourseReady() =>
        IsInstanceValid(kaleidoscopeRoot) && IsInstanceValid(kaleidoscopeRoot!.GetParent());

    private void BuildKaleidoscopeCourse(Node3D scene, BogCircuit circuit)
    {
        ClearKaleidoscopeCourse();
        kaleidoscopeRoot = new Node3D { Name = "Kaleidoscope" };
        scene.AddChild(kaleidoscopeRoot);

        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color("f4f7fc"), Metallic = 1, Roughness = 0,
            CullMode = BaseMaterial3D.CullModeEnum.Back
        };
        BuildKaleidoscopeTunnel(kaleidoscopeRoot, circuit, glass);

        var cloak = assets.Texture(RynthidEnergyTexture)
            ?? throw new InvalidDataException("The client DAT is missing the Rynthid Energy Field cloak texture.");
        var energy = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/KaleidoscopeRoad.gdshader") };
        energy.SetShaderParameter("cloak_energy", cloak);
        int tiles = Mathf.RoundToInt(circuit.Length / 300) * 50;
        var deck = BogCircuit.MeshAt(kaleidoscopeRoot, BuildClosedVoidDeck(circuit, tiles), Vector3.Zero, Vector3.One, energy);
        deck.Name = "Prismatic race deck";
        deck.Layers |= KaleidoscopeMirrorLayer;
        var rimPaint = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        Color edgeTint = new("9fb9d6");
        var rims = BogCircuit.MeshAt(kaleidoscopeRoot, BuildVoidDeckRims(circuit, _ => edgeTint),
            Vector3.Zero, Vector3.One, rimPaint);
        rims.Name = "Illuminated race edges";
        rims.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        BuildKaleidoscopePrisms(kaleidoscopeRoot, circuit);
        BuildKaleidoscopeProbe(kaleidoscopeRoot);
        GD.Print($"{KaleidoscopeCourseName}: {circuit.Length:F0} m per lap, {circuit.TightestInsideRadius():F0} m minimum inside radius.");
    }

    private void ClearKaleidoscopeCourse()
    {
        ClearKaleidoscopePrisms();
        kaleidoscopeProbe = null;
        kaleidoscopeProbeCooldown = 0;
        kaleidoscopeProbeFallback = true;
        kaleidoscopeRoot = null;
    }

    private void ApplyKaleidoscopeAtmosphere()
    {
        ApplyVoidAtmosphere();
        weatherSun.LightEnergy = .82f;
        weatherEnvironment.AmbientLightEnergy = .54f;
        weatherEnvironment.GlowEnabled = RenderingServer.GetCurrentRenderingMethod() != "gl_compatibility";
        weatherEnvironment.GlowHdrThreshold = 1.12f;
        weatherEnvironment.GlowIntensity = .34f;
        weatherEnvironment.GlowStrength = .76f;
        RenderingServer.GlobalShaderParameterSet("dereth_scene_grade", new Vector4(.02f, 1.04f, -.06f, 0));
    }

    private static void BuildKaleidoscopeTunnel(Node3D root, BogCircuit circuit, Material glass)
    {
        var tunnel = new Node3D { Name = "Mirrored tunnel" };
        root.AddChild(tunnel);
        var seams = new Node3D { Name = "Light along the mirror joints" };
        root.AddChild(seams);
        var seamPaint = new StandardMaterial3D
        {
            AlbedoColor = new Color("8fd0ee"), Metallic = .25f, Roughness = .36f,
            EmissionEnabled = true, Emission = new Color("8fd0ee"), EmissionEnergyMultiplier = 1.30f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        const int faces = 6, divisions = 2, rowsPerTile = 12;
        const int sides = faces * divisions;
        const float edgeWidth = Mathf.Tau / faces;
        int tileCount = Mathf.CeilToInt(circuit.Length / 130);
        int rowCount = tileCount * rowsPerTile;
        var rings = new (Vector3[] Positions, Vector3[] Normals)[rowCount + 1];
        for (int row = 0; row < rowCount; row++)
        {
            float fraction = row / (float)rowCount;
            float station = fraction * circuit.Length;
            Vector3 right = circuit.Right(station), up = right.Cross(circuit.Forward(station)).Normalized();
            Vector3 center = circuit.Point(station) + up * 1.8f;
            float radius = 51.5f + 2.7f * Mathf.Sin(fraction * Mathf.Tau * 5)
                + 1.8f * Mathf.Cos(fraction * Mathf.Tau * 9);
            float twist = .13f * Mathf.Sin(fraction * Mathf.Tau * 4) + .10f * Mathf.Sin(fraction * Mathf.Tau * 7);
            var positions = new Vector3[sides + 1];
            var normals = new Vector3[faces];
            for (int side = 0; side < sides; side++)
            {
                int face = side / divisions;
                float angle = side * Mathf.Tau / sides + twist;
                float offset = (side % divisions / (float)divisions - .5f) * edgeWidth;
                float distance = radius / Mathf.Cos(offset);
                positions[side] = center + (up * Mathf.Cos(angle) + right * Mathf.Sin(angle)) * distance;
                if (side % divisions == 0)
                {
                    float faceAngle = (face + .5f) * edgeWidth + twist;
                    normals[face] = -up * Mathf.Cos(faceAngle) - right * Mathf.Sin(faceAngle);
                }
            }
            positions[^1] = positions[0];
            rings[row] = (positions, normals);
        }
        rings[^1] = rings[0];

        for (int tile = 0; tile < tileCount; tile++)
        {
            int first = tile * rowsPerTile;
            int final = first + rowsPerTile;
            Vector3 origin = circuit.Point((first + rowsPerTile * .5f) * circuit.Length / rowCount);
            using var mesh = new SurfaceTool();
            using var trim = new SurfaceTool();
            mesh.Begin(Mesh.PrimitiveType.Triangles);
            trim.Begin(Mesh.PrimitiveType.Triangles);
            for (int row = first; row <= final; row++)
            for (int face = 0; face < faces; face++)
            {
                float fraction = row / (float)rowCount;
                for (int across = 0; across <= divisions; across++)
                {
                    int side = face * divisions + across;
                    mesh.SetNormal(rings[row].Normals[face]);
                    mesh.SetUV(new Vector2(side / (float)sides, fraction * circuit.Length / 32));
                    mesh.SetUV2(new Vector2(fraction, 0));
                    mesh.AddVertex(rings[row].Positions[side] - origin);
                    if (row == first || across == 0) continue;
                    int end = ((row - first) * faces + face) * (divisions + 1) + across;
                    int start = end - faces * (divisions + 1);
                    // Godot uses clockwise front faces. The silvered face looks into the tunnel.
                    mesh.AddIndex(start - 1); mesh.AddIndex(start); mesh.AddIndex(end - 1);
                    mesh.AddIndex(start); mesh.AddIndex(end); mesh.AddIndex(end - 1);
                }
                int joint = face * divisions;
                var (points, normals) = rings[row];
                Vector3 offset = (normals[face] + normals[(face + faces - 1) % faces]).Normalized() * .17f - origin;
                int left = (joint + sides - 1) % sides;
                trim.SetNormal(normals[face]);
                trim.AddVertex(points[joint].Lerp(points[left], .0045f) + offset);
                trim.AddVertex(points[joint].Lerp(points[joint + 1], .0045f) + offset);
                if (row == first) continue;
                int endTrim = ((row - first) * faces + face) * 2;
                int startTrim = endTrim - faces * 2;
                trim.AddIndex(startTrim); trim.AddIndex(endTrim); trim.AddIndex(startTrim + 1);
                trim.AddIndex(startTrim + 1); trim.AddIndex(endTrim); trim.AddIndex(endTrim + 1);
            }
            var panel = BogCircuit.MeshAt(tunnel, mesh.Commit(), origin, Vector3.One, glass);
            panel.Name = $"Prism sector {tile + 1}";
            panel.Layers = KaleidoscopeMirrorLayer;
            panel.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            var light = BogCircuit.MeshAt(seams, trim.Commit(), origin, Vector3.One, seamPaint);
            light.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    private void BuildKaleidoscopeProbe(Node3D root)
    {
        if (DisplayServer.GetName() == "headless") return;
        kaleidoscopeProbe = new ReflectionProbe
        {
            Name = "Kaleidoscope reflections",
            Size = new Vector3(310, 174, 310), MaxDistance = 180,
            Interior = true, BoxProjection = true, Visible = false,
            // Capture the road, crystals, and racers. The mirror panels use their own layer.
            CullMask = 1, ReflectionMask = KaleidoscopeMirrorLayer,
            UpdateMode = ReflectionProbe.UpdateModeEnum.Once
        };
        root.AddChild(kaleidoscopeProbe);
    }

    private void SetKaleidoscopeProbeFallback(bool enabled)
    {
        if (kaleidoscopeProbeFallback == enabled) return;
        kaleidoscopeProbeFallback = enabled;
        kaleidoscopeProbeCooldown = 0;
        if (!enabled) kaleidoscopeProbe?.Hide();
    }

    private void UpdateKaleidoscopeProbe(float dt, bool refresh = false)
    {
        if (!kaleidoscopeProbeFallback || kaleidoscopeProbe is not { } probe || racers.Count <= localRacerIndex) return;
        kaleidoscopeProbeCooldown -= dt;
        if (!refresh && kaleidoscopeProbeCooldown > 0) return;
        kaleidoscopeProbeCooldown = KaleidoscopeProbeInterval;

        Vector3 origin = track.Point(Player.Distance + 22) + Vector3.Up * 14;
        if (probe.Position != origin) probe.Position = origin;
        else
        {
            // Request a new progressive capture when the racer is stationary and scenery still moves.
            probe.OriginOffset = Vector3.Zero;
        }
        probe.Show();
    }
}
