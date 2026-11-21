using Godot;
using DerethSurvivors;

internal sealed class MuckwaterScenery
{
    private const uint LushGrass = 3, Marsh = 4, Mud = 5, PackedDirt = 7, FreshWater = 17, StoneRoad = 32;
    private const uint Reeds = 0x020010C6, ReedClump = 0x020010D0, Rushes = 0x020003D3;
    private const uint Fern = 0x02001063, BroadFern = 0x02001064, Heather = 0x020007A2;
    private const uint Boulder = 0x020005D0, Stone = 0x020005C9, Stump = 0x02000584;
    private const uint BroadTree = 0x02000322, TallTree = 0x020012FD, Alder = 0x02000246, Palm = 0x020004B1;
    private const uint SawatoShelter = 0x01000BFE, SawatoLonghouse = 0x01000D67, SawatoHall = 0x01000DDA;
    private const uint SawatoColumn = 0x0200040B, LampPost = 0x0200071A, Lantern = 0x02000337,
        StoneWell = 0x02000118, Steps = 0x02000300;

    private readonly AcAssets assets;
    private readonly MuckwaterLayout layout;
    private readonly float roadHalf;
    private readonly ShaderMaterial ground, water;
    private readonly Texture2D stoneRoad;
    private readonly Dictionary<uint, Prototype> prototypes = [];
    private readonly List<SceneryPlacement> placements = [];
    private readonly ProjectileWorld sceneryPhysics = new();
    public int CoursePropCount { get; private set; }
    public int CourseModelCount { get; private set; }
    public float MinimumCourseClearance { get; private set; } = float.MaxValue;

    private readonly record struct Part(Mesh Mesh, Transform3D Pose);
    private sealed record Prototype(Part[] Parts, float Radius);
    private readonly record struct SceneryPlacement(uint Model, Transform3D Pose, bool Shadows);

    public MuckwaterScenery(AcAssets assets, BogCircuit track)
    {
        this.assets = assets;
        roadHalf = track.HalfWidth;
        layout = new MuckwaterLayout(track);
        var region = AcWorldData.ReadRegion(assets.Archive.Read(0x13000000)).Blend
            ?? throw new InvalidDataException("The DAT has no native terrain blending table.");
        var layers = region.Textures.ToDictionary(row => row.Type);
        var masks = layout.BuildMask(assets, region, LushGrass, Marsh);
        Texture2D Layer(uint type) => Texture(layers[type].Texture);
        stoneRoad = Layer(StoneRoad);
        ground = Material("MuckwaterGround", masks);
        ground.SetShaderParameter("grass_tex", Layer(LushGrass));
        ground.SetShaderParameter("marsh_tex", Layer(Marsh));
        ground.SetShaderParameter("mud_tex", Layer(Mud));
        ground.SetShaderParameter("packed_tex", Layer(PackedDirt));
        ground.SetShaderParameter("road_tex", stoneRoad);
        ground.SetShaderParameter("detail_tex", Texture(layers[LushGrass].DetailTexture));
        water = Material("MuckwaterWater", masks);
        water.SetShaderParameter("water_tex", Layer(FreshWater));
    }

    private ShaderMaterial Material(string name, Texture2D masks)
    {
        var shader = GD.Load<Shader>($"res://Shaders/{name}.gdshader")
            ?? throw new InvalidDataException($"Missing scenery shader {name}.");
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("blend_map", masks);
        material.SetShaderParameter("map_origin", MuckwaterLayout.Origin);
        material.SetShaderParameter("map_extent", MuckwaterLayout.Extent);
        return material;
    }

    private Texture2D Texture(uint id) =>
        assets.Texture(id) ?? throw new InvalidDataException($"Missing native scenery texture {id:X8}.");

    public void BuildCourse(Node3D scene, BogCircuit track)
    {
        BuildGround(scene);
        foreach (var pond in MuckwaterLayout.Ponds)
        {
            var mesh = new PlaneMesh { Size = (pond.Radius + Vector2.One * 3.9f) * 2 };
            var surface = BogCircuit.MeshAt(scene, mesh, new(pond.Center.X, MuckwaterLayout.WaterLevel, pond.Center.Y), Vector3.One, water);
            surface.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        // The named town structures come from the Sawato landblock. Their placements
        // follow the race layout, which uses an original course and wetland.
        PlaceAlongTrack(track, SawatoShelter, track.Gate(0) + 27, roadHalf + 19, 1.1f, .4f);
        PlaceAlongTrack(track, SawatoLonghouse, track.Gate(1) + 41, -roadHalf - 21, 1.12f, -.23f);
        PlaceAlongTrack(track, SawatoHall, track.Gate(3) + 22, roadHalf + 22, 1.06f, -.60f);

        foreach (int side in new[] { -1, 1 })
        {
            Vector3 column = track.Surface(BogCircuit.Grid + .65f, side * (roadHalf + 2.95f));
            AddGround(SawatoColumn, MuckwaterLayout.Flat(column), BogCircuit.Yaw(track.Forward(BogCircuit.Grid)), 1.12f);
            var torch = track.Surface(BogCircuit.Grid + 5, side * (roadHalf + 2.35f));
            AddGround(LampPost, MuckwaterLayout.Flat(torch), side * .9f, 1.65f);
            Vector3 hanging = track.Surface(BogCircuit.Grid + .73f, side * (roadHalf + 2.77f)) + Vector3.Up * 4.5f;
            Add(Lantern, hanging, 0, 2.25f);
        }

        for (int i = 0; i < 18; i++)
        {
            float at = 42 + i * (track.Length - 45) / 18;
            int side = i % 3 == 1 ? 1 : -1;
            Vector3 post = track.Surface(at, side * (roadHalf + 3.1f));
            AddGround(LampPost, MuckwaterLayout.Flat(post), BogCircuit.Yaw(track.Forward(at)), 1.3f);
        }

        var random = new Random(624);
        var trees = new[] { Alder, BroadTree, TallTree, Palm };
        var undergrowth = new[] { Fern, BroadFern, Heather, Boulder, Stone, Stump, Rushes };
        var treePositions = new List<Vector2>();
        for (int i = 0; i < 1460; i++)
        {
            Vector2 at = MuckwaterLayout.Origin + new Vector2(.08f + random.NextSingle() * .84f, .08f + random.NextSingle() * .84f) * MuckwaterLayout.Extent;
            if (layout.Height(at) < MuckwaterLayout.WaterLevel || layout.RoadDistance(at) < roadHalf + 3.3f ||
                placements.Take(3).Any(model => MuckwaterLayout.Flat(model.Pose.Origin).DistanceTo(at) < 11))
                continue;
            bool tree = random.NextSingle() < .29f;
            if (tree && treePositions.Any(other => other.DistanceSquaredTo(at) < 38)) continue;
            uint model = tree ? trees[random.Next(trees.Length)] : undergrowth[random.Next(undergrowth.Length)];
            float scale = tree ? .64f + random.NextSingle() * .52f : .42f + random.NextSingle() * .63f;
            if (AddGround(model, at, random.NextSingle() * Mathf.Tau, scale, tree || model == Boulder))
                if (tree) treePositions.Add(at);
        }

        foreach (var pond in MuckwaterLayout.Ponds)
        {
            for (int i = 0; i < 35; i++)
            {
                float angle = i * Mathf.Tau / 35 + random.NextSingle() * .17f;
                var radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 at = pond.Center + radial * (pond.Radius + Vector2.One * (random.NextSingle() * 3 - 1.8f));
                if (layout.Height(at) < WaterlineForPlants) continue;
                AddGround(i % 4 == 0 ? ReedClump : i % 3 == 0 ? Rushes : Reeds, at,
                    random.NextSingle() * Mathf.Tau, .42f + random.NextSingle() * .47f, false);
            }
        }

        AddShortcutColliders();
        CoursePropCount = placements.Count;
        CourseModelCount = placements.Select(p => p.Model).Distinct().Count();
        Flush(scene);
    }

    public Vector3 FreeGround(Vector3 point) => layout.OnGround(MuckwaterLayout.Flat(point));

    public bool DryMove(Vector3 start, Vector3 destination)
    {
        Vector2 relative = MuckwaterLayout.Flat(destination) - MuckwaterLayout.Origin;
        return relative.X > 2 && relative.Y > 2 && relative.X < MuckwaterLayout.Extent.X - 2
            && relative.Y < MuckwaterLayout.Extent.Y - 2 && destination.Y >= MuckwaterLayout.WaterLevel + .05f
            && BogCircuit.ClearMove(sceneryPhysics, start, destination);
    }

    private void AddShortcutColliders()
    {
        foreach (var prop in placements)
        {
            if (prop.Model is SawatoShelter or SawatoLonghouse or SawatoHall)
            {
                var faces = assets.PhysicsTriangles(prop.Model, prop.Pose).ToArray();
                if (faces.Length > 0) { sceneryPhysics.Add(faces, "scenery"); continue; }
            }
            float radius = prop.Model switch
            {
                Alder or BroadTree or TallTree or Palm or SawatoColumn => .56f,
                Boulder or Stone or Stump => .85f,
                SawatoShelter or SawatoLonghouse or SawatoHall => GetPrototype(prop.Model).Radius * .62f,
                _ => 0
            };
            if (radius <= 0) continue;
            Vector3 p = prop.Pose.Origin;
            sceneryPhysics.AddCylinder(new System.Numerics.Vector3(p.X, p.Y - .12f, p.Z),
                System.Numerics.Vector3.UnitY, 4, radius * prop.Pose.Basis.X.Length());
        }
    }
    private const float WaterlineForPlants = MuckwaterLayout.WaterLevel - .28f;

    private void PlaceAlongTrack(BogCircuit track, uint id, float at, float lane, float size, float facing)
    {
        foreach (float extra in new[] { 0f, 4f, 8f, 12f })
        {
            Vector3 point = track.Surface(at, lane + Mathf.Sign(lane) * extra);
            if (layout.PondDistance(MuckwaterLayout.Flat(point)) < 2) continue;
            if (AddGround(id, MuckwaterLayout.Flat(point), facing, size)) return;
        }
    }

    private void BuildGround(Node3D scene)
    {
        const int Across = 185, Along = 189;
        Vector2 offset = MuckwaterLayout.Origin, step = MuckwaterLayout.Extent / new Vector2(Across - 1, Along - 1);
        var vertices = new Vector3[Across * Along];
        var norms = new Vector3[vertices.Length];
        for (int z = 0; z < Along; z++)
            for (int x = 0; x < Across; x++)
            {
                Vector2 p = offset + new Vector2(x, z) * step;
                int index = z * Across + x;
                vertices[index] = layout.OnGround(p);
            }

        for (int z = 0; z < Along; z++)
            for (int x = 0; x < Across; x++)
            {
                int index = z * Across + x;
                Vector3 east = vertices[z * Across + Math.Min(x + 1, Across - 1)] - vertices[z * Across + Math.Max(0, x - 1)];
                Vector3 south = vertices[Math.Min(z + 1, Along - 1) * Across + x] - vertices[Math.Max(0, z - 1) * Across + x];
                norms[index] = south.Cross(east).Normalized();
            }

        using var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < vertices.Length; i++)
        {
            tool.SetNormal(norms[i]);
            tool.AddVertex(vertices[i]);
        }
        for (int z = 0; z < Along - 1; z++)
            for (int x = 0; x < Across - 1; x++)
            {
                int a = z * Across + x, b = a + 1, c = a + Across, d = c + 1;
                tool.AddIndex(a); tool.AddIndex(b); tool.AddIndex(c);
                tool.AddIndex(b); tool.AddIndex(d); tool.AddIndex(c);
            }
        var floor = BogCircuit.MeshAt(scene, tool.Commit(), Vector3.Zero, Vector3.One, ground);
        floor.Name = "Blended DAT terrain";
        floor.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    public void BuildStable(Node3D scene)
    {
        var stableGround = (ShaderMaterial)ground.Duplicate();
        stableGround.SetShaderParameter("stable_ground", true);
        var floor = new PlaneMesh { Size = new(90, 79) };
        BogCircuit.MeshAt(scene, floor, new(0, -.07f, -11), Vector3.One, stableGround);

        var cobbles = new StandardMaterial3D
        {
            AlbedoTexture = stoneRoad, AlbedoColor = new("b2a082"), Uv1Triplanar = true,
            Uv1Scale = Vector3.One * .42f, Roughness = .95f, MetallicSpecular = 0,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
        };
        var ring = new CylinderMesh { TopRadius = 3.13f, BottomRadius = 3.24f, Height = .26f, RadialSegments = 40 };
        BogCircuit.MeshAt(scene, ring, new(0, -.12f, 0), Vector3.One, cobbles);
        var rim = new TorusMesh { InnerRadius = 3.01f, OuterRadius = 3.045f, Rings = 44, RingSegments = 8 };
        BogCircuit.MeshAt(scene, rim, new(0, .021f, 0), Vector3.One, BogCircuit.Paint("bc8856"));

        Add(SawatoShelter, new(7.2f, -.08f, -13.5f), .47f, .76f);
        Add(Steps, new(5f, -.07f, -8.2f), -.22f, .73f);
        Add(StoneWell, new(-4, -.07f, -7.2f), -.2f, 1.35f);
        Add(LampPost, new(4.48f, -.07f, -5.25f), -.1f, 1.88f);
        Add(LampPost, new(-5.22f, -.07f, -3.8f), .6f, 1.8f);
        Add(Alder, new(-9, -.07f, -6.7f), 1.62f, 1.3f);
        Add(TallTree, new(14, -.07f, -14), -.18f, .91f);
        Add(BroadTree, new(-9, -.07f, -17), .72f, 1.38f);
        Add(Palm, new(15, -.07f, -8), 2.8f, 1.28f);

        var random = new Random(908);
        uint[] garden = [Heather, Fern, Rushes, Reeds, Stone, Stump];
        for (int i = 0; i < 44; i++)
        {
            float angle = random.NextSingle() * Mathf.Tau;
            float radius = 4.4f + random.NextSingle() * 11;
            var p = new Vector3(Mathf.Cos(angle) * radius, -.08f, Mathf.Sin(angle) * radius);
            Add(garden[i % garden.Length], p, angle, .42f + random.NextSingle() * .48f, false);
        }
        Flush(scene);
    }

    private bool AddGround(uint id, Vector2 point, float yaw, float scale, bool shadows = true)
    {
        float clearance = layout.RoadDistance(point) - GetPrototype(id).Radius * scale;
        if (clearance < roadHalf + 2.05f) return false;
        MinimumCourseClearance = Math.Min(MinimumCourseClearance, clearance);
        Add(id, layout.OnGround(point), yaw, scale, shadows);
        return true;
    }

    private void Add(uint id, Vector3 point, float yaw, float scale, bool shadows = true)
    {
        var pose = new Transform3D(new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale), point);
        placements.Add(new(id, pose, shadows));
    }

    private Prototype GetPrototype(uint id)
    {
        if (prototypes.TryGetValue(id, out var prototype)) return prototype;
        Part[] parts;
        if (id >> 24 == 1)
            parts = [new(assets.Mesh(id), Transform3D.Identity)];
        else
        {
            var setup = assets.Setup(id);
            parts = Enumerable.Range(0, setup.Parts.Length).Where(i => setup.Parts[i] != 0).Select(i =>
                new Part(assets.Mesh(setup.Parts[i]),
                    AcAssets.Convert(setup.Placement[i]).ScaledLocal(AcAssets.Convert(setup.Scales[i]).Abs()))).ToArray();
        }
        if (parts.Length == 0 || parts.Any(part => part.Mesh.GetSurfaceCount() == 0))
            throw new InvalidDataException($"Native scenery model {id:X8} has missing render geometry.");

        float radius = 0;
        foreach (var part in parts)
        {
            var bounds = part.Pose * part.Mesh.GetAabb();
            if (!bounds.HasVolume() && bounds.Size.LengthSquared() < .000001f) continue;
            var corner = bounds.Position.Abs().Max(bounds.End.Abs());
            radius = Math.Max(radius, new Vector2(corner.X, corner.Z).Length());
        }
        return prototypes[id] = new(parts, radius);
    }

    private void Flush(Node3D scene)
    {
        var groups = new Dictionary<(Mesh Mesh, int X, int Z, bool Shadows), List<Transform3D>>();
        foreach (var prop in placements)
        {
            Vector3 p = prop.Pose.Origin;
            foreach (var part in GetPrototype(prop.Model).Parts)
            {
                var key = (part.Mesh, Mathf.FloorToInt(p.X / 45), Mathf.FloorToInt(p.Z / 45), prop.Shadows);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
                list.Add(prop.Pose * part.Pose);
            }
        }
        foreach (var (key, list) in groups)
        {
            var mesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = key.Mesh, InstanceCount = list.Count };
            for (int i = 0; i < list.Count; i++) mesh.SetInstanceTransform(i, list[i]);
            scene.AddChild(new MultiMeshInstance3D
            {
                Multimesh = mesh,
                CastShadow = key.Shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off
            });
        }
        placements.Clear();
    }
}



