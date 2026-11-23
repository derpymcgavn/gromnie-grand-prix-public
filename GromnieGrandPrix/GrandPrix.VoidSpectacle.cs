using System.Collections.Generic;
using Godot;

public partial class GrandPrix
{
    private static readonly StringName VoidGalaxyProgressParameter = "battle_progress";
    private readonly List<ShaderMaterial> voidGalaxyMaterials = [];
    private float voidGalaxyProgress;

    private void SetVoidGalaxyProgress(float progress)
    {
        progress = Mathf.Clamp(progress, 0, 1);
        if (Mathf.IsEqualApprox(progress, voidGalaxyProgress)) return;
        voidGalaxyProgress = progress;
        foreach (var material in voidGalaxyMaterials)
            material.SetShaderParameter(VoidGalaxyProgressParameter, progress);
    }

    private void ClearVoidSpectacle()
    {
        voidGalaxyMaterials.Clear();
        voidGalaxyProgress = 0;
    }

    private void BuildVoidSpectacle(Node3D root)
    {
        ClearVoidSpectacle();
        var sky = new Node3D { Name = "Far reaches of the void" };
        root.AddChild(sky);

        var planet = GD.Load<Shader>("res://Shaders/VoidRingedWorld.gdshader");
        AddBackdrop("Eclipsed ringed world", "Ringed-World", planet, new Vector3(-530, 270, -1090),
            new Vector2(710, 710), new Vector2(.96f, .58f), .315f);

        var galaxy = GD.Load<Shader>("res://Shaders/VoidCasterGalaxy.gdshader");
        AddGalaxy("Galaxy of the three casters", "Ultraviolet-Nebula", new Vector3(0, 485, -1435),
            new Vector2(3160, 1500), .16f, .1f, 1.0f, .93f);
        AddGalaxy("Western crimson arm", "Rose-Nebula", new Vector3(-1395, 276, 175),
            new Vector2(2090, 1100), -.17f, .87f, -.56f, .72f);
        AddGalaxy("Eastern herald arm", "Turquoise-Nebula", new Vector3(1380, 321, 400),
            new Vector2(2150, 1050), .31f, -.86f, .62f, .72f);
        AddGalaxy("Far turn of the galaxy", "Ultraviolet-Nebula", new Vector3(0, 415, 1530),
            new Vector2(2550, 1130), -.13f, 2.25f, -.52f, .75f);

        var filament = GD.Load<Shader>("res://Shaders/VoidDistantFilament.gdshader");
        AddFilament("Lower violet current", .15f, 1.42f, 700, -21, 11, "553bb4");
        AddFilament("Lower blue current", .52f, 1.10f, 735, -33, 7, "3673a6");
        AddFilament("High indigo current", 2.08f, 1.26f, 789, 105, 10, "394b9a");
        AddFilament("Gold outer current", 3.47f, 1.23f, 756, 12, 8, "8b535c");
        AddFilament("Turquoise outer current", 4.98f, 1.27f, 699, 39, 12, "308b91");
        AddFilament("Turquoise outer trace", 5.14f, 1.05f, 725, 23, 6, "2f6d8b");

        AddVoidDistantDebris(sky);

        void AddBackdrop(string name, string texture, Shader shader, Vector3 position, Vector2 size,
            Vector2 outline, float tilt = 0, int priority = 0)
        {
            var paint = new ShaderMaterial { Shader = shader, RenderPriority = priority };
            paint.SetShaderParameter("backdrop", GD.Load<Texture2D>($"res://Data/Void/{texture}.png"));
            AddMesh(name, BuildVoidBackdrop(size, outline, tilt), paint, position).Layers |= VoidReflectionSceneryLayer;
        }

        void AddGalaxy(string name, string texture, Vector3 position, Vector2 size, float tilt, float phase,
            float drift, float strength)
        {
            var paint = new ShaderMaterial { Shader = galaxy, RenderPriority = -4 };
            paint.SetShaderParameter("clouds", GD.Load<Texture2D>($"res://Data/Void/{texture}.png"));
            paint.SetShaderParameter("arm_phase", phase);
            paint.SetShaderParameter("drift", drift);
            paint.SetShaderParameter("strength", strength);
            voidGalaxyMaterials.Add(paint);
            AddMesh(name, BuildVoidBackdrop(size, Vector2.One * .99f, tilt), paint, position).Layers |= VoidReflectionSceneryLayer;
        }

        void AddFilament(string name, float heading, float sweep, float radius, float altitude, float width, string color)
        {
            var paint = new ShaderMaterial { Shader = filament, RenderPriority = -3 };
            paint.SetShaderParameter("tint", new Color(color));
            paint.SetShaderParameter("seed", heading);
            AddMesh(name, BuildVoidDistantArc(heading, sweep, radius, altitude, width), paint, Vector3.Zero);
        }

        MeshInstance3D AddMesh(string name, Mesh mesh, Material paint, Vector3 position)
        {
            var node = new MeshInstance3D
            {
                Name = name, Mesh = mesh, MaterialOverride = paint, Position = position,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            sky.AddChild(node);
            return node;
        }
    }

    private static ArrayMesh BuildVoidBackdrop(Vector2 size, Vector2 outline, float tilt)
    {
        const int sides = 24;
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetUV(new Vector2(.5f, .5f));
        surface.AddVertex(Vector3.Zero);
        float radius = 0;
        for (int i = 0; i < sides; i++)
        {
            float angle = i * Mathf.Tau / sides;
            Vector2 point = (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * outline).Rotated(tilt);
            Vector2 vertex = point * size * .5f;
            radius = Math.Max(radius, vertex.Length());
            surface.SetUV(new Vector2(.5f + point.X * .5f, .5f - point.Y * .5f));
            surface.AddVertex(new Vector3(vertex.X, vertex.Y, 0));
            surface.AddIndex(0);
            surface.AddIndex(i + 1);
            surface.AddIndex((i + 1) % sides + 1);
        }
        var mesh = surface.Commit();
        // The billboard can yaw and pitch to face any race camera.
        mesh.CustomAabb = new Aabb(Vector3.One * -radius, Vector3.One * (2 * radius));
        return mesh;
    }

    private static ArrayMesh BuildVoidDistantArc(float heading, float sweep, float radius, float altitude, float width)
    {
        const int steps = 96;
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i <= steps; i++)
        {
            float along = i / (float)steps;
            float angle = heading + along * sweep;
            Vector3 radial = new(MathF.Cos(angle), 0, MathF.Sin(angle));
            float drift = MathF.Sin(along * Mathf.Tau + heading) * 14 + MathF.Sin(along * Mathf.Pi) * 22;
            Vector3 center = radial * (radius + drift)
                + Vector3.Up * (altitude + MathF.Sin(angle * 2.7f) * 24 + 32 * MathF.Sin(along * Mathf.Pi));
            Vector3 outward = (radial * .69f + Vector3.Up * .72f) * width;
            surface.SetUV(new Vector2(along, 0));
            surface.AddVertex(center - outward);
            surface.SetUV(new Vector2(along, 1));
            surface.AddVertex(center + outward);
            if (i == 0) continue;
            int start = (i - 1) * 2, end = i * 2;
            surface.AddIndex(start); surface.AddIndex(end); surface.AddIndex(start + 1);
            surface.AddIndex(end); surface.AddIndex(end + 1); surface.AddIndex(start + 1);
        }
        return surface.Commit();
    }

    private static void AddVoidDistantDebris(Node3D sky)
    {
        const int fragments = 65;
        var paint = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/VoidDistantDebris.gdshader") };
        var mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 6, Rings = 2, Material = paint },
            InstanceCount = fragments
        };
        using var random = new RandomNumberGenerator { Seed = 0x57a9b3 };
        for (int i = 0; i < fragments; i++)
        {
            float phase = i * Mathf.Tau / fragments;
            float angle = phase + random.RandfRange(-.058f, .058f);
            float radius = random.RandfRange(690, 900);
            float height = MathF.Sin(phase * 1.7f) * 56 + random.RandfRange(-55, 54);
            Vector3 center = new(MathF.Cos(angle) * radius, height, MathF.Sin(angle) * radius);
            float size = random.RandfRange(2.7f, 13);
            Vector3 shape = new Vector3(random.RandfRange(.48f, .93f), random.RandfRange(.44f, 1), random.RandfRange(.7f, 1.4f)) * size;
            Basis basis = Basis.FromEuler(new Vector3(.35f + phase, phase * 1.3f, -phase * 1.7f)).Scaled(shape);
            mesh.SetInstanceTransform(i, new Transform3D(basis, center));
            mesh.SetInstanceColor(i, new Color("7a55b5").Lerp(new Color("457991"), random.Randf()));
        }
        sky.AddChild(new MultiMeshInstance3D
        {
            Name = "Outer belt of debris", Multimesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }
}
