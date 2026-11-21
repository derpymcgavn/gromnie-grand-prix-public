using Godot;

internal sealed class FrostboltTrail
{
    private const float Lifetime = .57f;
    private static readonly StandardMaterial3D Ice = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        VertexColorUseAsAlbedo = true
    };
    private readonly ImmediateMesh mesh = new();
    private readonly MeshInstance3D node;
    private readonly List<(Vector3 Point, float Time)> samples = [];
    private float clock;
    public bool Flying = true;
    public int SampleCount => samples.Count;

    public FrostboltTrail(Node3D world, Vector3 launch)
    {
        node = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        world.AddChild(node);
        samples.Add((launch, 0));
    }

    public void Record(Vector3 point)
    {
        if (samples.Count == 0 || samples[^1].Point.DistanceSquaredTo(point) > .02f)
            samples.Add((point, clock));
    }

    public bool Tick(float dt)
    {
        clock += dt;
        while (samples.Count > 0 && clock - samples[0].Time >= Lifetime) samples.RemoveAt(0);
        mesh.ClearSurfaces();
        if (samples.Count >= 2)
        {
            DrawRibbon(false, 1);
            DrawRibbon(false, -1);
            DrawRibbon(true, 1);
            DrawRibbon(true, -1);
        }
        return Flying || samples.Count > 0;
    }

    private void DrawRibbon(bool vertical, float sign)
    {
        mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, Ice);
        for (int i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var tangent = (samples[Math.Min(i + 1, samples.Count - 1)].Point - samples[Math.Max(i - 1, 0)].Point).Normalized();
            Vector3 side = tangent.Cross(Math.Abs(tangent.Y) > .95f ? Vector3.Right : Vector3.Up).Normalized();
            if (vertical) side = side.Cross(tangent);
            float fresh = Mathf.Clamp(1 - (clock - sample.Time) / Lifetime, 0, 1);
            Vector3 edge = side * (sign * Mathf.Lerp(.045f, vertical ? .37f : .50f, fresh));
            mesh.SurfaceSetColor(new Color(.10f, .55f, 1, fresh * .055f));
            mesh.SurfaceAddVertex(sample.Point + edge);
            mesh.SurfaceSetColor(new Color(.47f, .88f, 1, fresh * .66f));
            mesh.SurfaceAddVertex(sample.Point);
        }
        mesh.SurfaceEnd();
    }

    public void Free()
    {
        node.Hide();
        node.QueueFree();
    }
}
