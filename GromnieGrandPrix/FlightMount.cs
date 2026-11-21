using Godot;

internal sealed class FlightRide(FlightMount flyer, float start, float end, float lane, float height, float sweep)
{
    private const float RollDuration = .72f, RollRecharge = 2.15f;
    public readonly FlightMount Flyer = flyer;
    public readonly float Start = start, End = end, StartLane = lane, Height = height, Sweep = sweep;
    public Vector3 Position, Heading;
    public float Age, Pitch, Bank, AirSteer, AirPitch;
    public bool Direct, DiveReady;
    public Vector3? CitadelFlyby;
    public float CourseMetresPerAirMetre = 1;
    public float RollStarted { get; private set; } = float.NegativeInfinity;
    private float rollDirection = 1;
    private float? replicaRoll;
    private float RollAge => Age - RollStarted;
    public bool Rolling => RollAge < RollDuration;
    public bool DodgingRoll => RollAge is >= .055f and < .63f;
    public float RollCooldown => Math.Max(0, RollRecharge - RollAge);
    public float RollAngle => replicaRoll ?? (Rolling ? rollDirection * Mathf.Tau * Mathf.SmoothStep(0, RollDuration, RollAge) : 0);

    internal void SetReplicaRoll(float angle, float cooldown)
    {
        replicaRoll = angle;
        RollStarted = cooldown > 0 ? Age - (RollRecharge - cooldown) : float.NegativeInfinity;
    }

    public bool TryRoll(float steer)
    {
        if (RollCooldown > 0) return false;
        RollStarted = Age;
        rollDirection = steer < -.1f ? 1 : -1;
        return true;
    }
}

internal abstract class FlightMount
{
    private static Mesh? sharedWindMesh;
    private static StandardMaterial3D? sharedWindMaterial;
    private readonly MeshInstance3D[] slipstream = new MeshInstance3D[14];
    public Node3D Root { get; }
    public Node3D RiderAnchor { get; } = new() { Name = "Rider anchor" };
    public virtual bool SeatsRider => false;

    protected FlightMount(string name)
    {
        Root = new Node3D { Name = name };
        Root.AddChild(RiderAnchor);
        var windMesh = sharedWindMesh ??= MakeWindStreak();
        var wind = sharedWindMaterial ??= new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };
        for (int i = 0; i < slipstream.Length; i++)
        {
            slipstream[i] = BogCircuit.MeshAt(Root, windMesh, Vector3.Zero, Vector3.One, wind);
            slipstream[i].CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    public void Draw(float dt, float age, float bank)
    {
        Pose(dt, age, bank);
        for (int i = 0; i < slipstream.Length; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            float t = Mathf.PosMod(age * (1.2f + i % 4 * .18f) + i * .27f, 1);
            var streak = slipstream[i];
            streak.Position = new Vector3(side * (2.15f + i * .31f + t * .48f), 1.3f + (i % 4) * .73f, -7.5f + t * 15);
            streak.Rotation = new Vector3(.015f, side * .08f, .3f + side * .3f + i % 3 * .73f);
            streak.Scale = new Vector3(Mathf.Sin(t * Mathf.Pi) * (i % 3 == 0 ? .83f : .49f), 1, .64f + (i % 4) * .31f);
        }
    }

    protected abstract void Pose(float dt, float age, float bank);

    private static Mesh MakeWindStreak()
    {
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        var glow = new Color("c4f4f8");
        void Vertex(Vector3 p, float alpha) { surface.SetColor(new Color(glow, alpha)); surface.AddVertex(p); }
        Vertex(new(0, 0, -2.65f), 0);
        Vertex(new(-.16f, 0, -.65f), .55f);
        Vertex(new(.16f, 0, -.65f), .55f);
        Vertex(new(-.16f, 0, -.65f), .55f);
        Vertex(new(0, 0, 2.8f), 0);
        Vertex(new(.16f, 0, -.65f), .55f);
        return surface.Commit();
    }
}
