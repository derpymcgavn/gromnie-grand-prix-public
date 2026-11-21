using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const uint RiverMirrorLayer = 1u << 14;
    private SubViewport? riverReflection;
    private Camera3D? riverCamera;
    private float riverLevel;
    private Vector2 riverFocus;

    private void BuildSpanRiver(Node3D scene, SpanSprint sprint, AcLandArena land)
    {
        riverLevel = sprint.WaterLevel - land.ElevationOrigin;
        riverFocus = new((sprint.West + sprint.East) * .5f, sprint.RiverAxis);
        foreach (var prop in scene.GetChildren().OfType<Node3D>()
            .Where(node => node.Name.ToString().StartsWith("Landblock_", StringComparison.Ordinal))
            .SelectMany(block => block.GetChildren().OfType<Node3D>()))
        {
            if (prop.Name.ToString().StartsWith("DAT_Terrain_", StringComparison.Ordinal)) continue;
            var origin = prop.GlobalPosition;
            if (new Vector2(origin.X, origin.Z).DistanceSquaredTo(riverFocus) > 550 * 550) continue;
            AddRiverReflectionLayer(prop);
        }

        riverReflection = new SubViewport
        {
            Name = "Obsidian Span water reflection",
            World3D = GetViewport().FindWorld3D(),
            Size = new Vector2I(576, 360),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        scene.AddChild(riverReflection);
        riverCamera = new Camera3D { CullMask = RiverMirrorLayer, Current = true, Far = 1150 };
        riverReflection.AddChild(riverCamera);
    }

    private static void AddRiverReflectionLayer(Node3D prop)
    {
        if (prop is MeshInstance3D mesh) mesh.Layers |= RiverMirrorLayer;
        foreach (var part in prop.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            part.Layers |= RiverMirrorLayer;
    }

    private void UpdateSpanReflection()
    {
        if (riverReflection is null || !GodotObject.IsInstanceValid(riverReflection) || riverCamera is null) return;
        var terrain = AcTerrainRenderer.For(assets, townRegion!).Material;
        Vector3 position = camera.GlobalPosition;
        bool visible = track.IsSprint && racing && !finished && position.Y > riverLevel + .35f &&
            new Vector2(position.X, position.Z).DistanceSquaredTo(riverFocus) < 520 * 520;
        if (!visible)
        {
            if (riverReflection.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled) StopSpanReflection();
            return;
        }
        Vector2 size = GetViewport().GetVisibleRect().Size;
        Vector2I target = new(Math.Max(1, (int)(size.X * .5f)), Math.Max(1, (int)(size.Y * .5f)));
        if (riverReflection.Size != target) riverReflection.Size = target;
        riverCamera.Projection = camera.Projection;
        riverCamera.Fov = camera.Fov;
        riverCamera.Size = camera.Size;
        riverCamera.Near = camera.Near;
        riverCamera.KeepAspect = camera.KeepAspect;
        var basis = camera.GlobalBasis;
        Vector3 Mirror(Vector3 axis) => new(axis.X, -axis.Y, axis.Z);
        position.Y = 2 * riverLevel - position.Y;
        // Reflect the camera across the river plane and correct its handedness.
        riverCamera.GlobalTransform = new Transform3D(new Basis(Mirror(basis.X), -Mirror(basis.Y), Mirror(basis.Z)), position);
        if (riverReflection.RenderTargetUpdateMode != SubViewport.UpdateMode.Always)
        {
            terrain.SetShaderParameter("bridge_reflection_active", true);
            terrain.SetShaderParameter("bridge_reflection", riverReflection.GetTexture());
            terrain.SetShaderParameter("bridge_water_level", riverLevel);
            terrain.SetShaderParameter("bridge_reflection_center", riverFocus);
            riverReflection.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        }
    }

    private void StopSpanReflection()
    {
        if (riverReflection is null || !GodotObject.IsInstanceValid(riverReflection)) return;
        riverReflection.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        var material = AcTerrainRenderer.For(assets, townRegion!).Material;
        material.SetShaderParameter("bridge_reflection_active", false);
        material.SetShaderParameter("bridge_reflection", default(Variant));
    }
}
