using Godot;
using DerethSurvivors;

internal sealed class Gromnatross : FlightMount
{
    private static readonly Vector3[] WingOutline =
    [
        new(0, 0, -.28f), new(1.50f, .13f, -1.20f), new(3.55f, .34f, -1.38f), new(5.55f, .44f, -.72f),
        new(7.12f, .41f, .35f), new(6.48f, .07f, 1.18f), new(5.10f, -.22f, 1.29f),
        new(4.25f, -.30f, 2.10f), new(2.73f, -.29f, 2.36f), new(1.46f, -.19f, 1.82f), new(0, 0, .91f)
    ];
    private static readonly Vector3 WingRidge = new(2.33f, .33f, .26f);
    private static StandardMaterial3D? membraneMaterial;
    private static Mesh[]? wingMeshes;
    private readonly AcActor body;
    private readonly Node3D[] wings = new Node3D[2];

    public Gromnatross(AcAssets assets, RareCatalog.Creature skin) : base("Gromnatross")
    {
        // An original winged mount built around the game's native Gromnie rig.
        body = assets.CreateActor(skin.Setup, skin.Motion, 2.55f);
        body.ApplyNativeAppearance(skin);
        body.Position = Vector3.Up * 1.55f;
        Root.AddChild(body);
        var meshes = wingMeshes ??= [MakeWing(-1), MakeWing(1)];
        for (int i = 0; i < wings.Length; i++)
        {
            int sign = i == 0 ? -1 : 1;
            var shoulder = new Node3D { Position = new Vector3(sign * .48f, 3.25f, -.13f) };
            Root.AddChild(shoulder);
            BogCircuit.MeshAt(shoulder, meshes[i], Vector3.Zero, Vector3.One, membraneMaterial ??= MakeWingMaterial());
            wings[i] = shoulder;
        }
    }

    protected override void Pose(float dt, float age, float bank)
    {
        body.Tick(dt * .75f, false, Vector3.Zero);
        for (int i = 0; i < wings.Length; i++)
        {
            int side = i == 0 ? -1 : 1;
            float stroke = age * 6.2f + i * .15f;
            wings[i].Rotation = new(.08f + Mathf.Cos(stroke) * .07f, side * .06f,
                side * (.11f + Mathf.Sin(stroke) * .36f) - bank * .18f);
            TuckLeg(i);
        }
        RiderAnchor.Position = Root.ToLocal((MountedRacer.ClawPosition(body, 0) + MountedRacer.ClawPosition(body, 1)) * .5f);
    }

    private void TuckLeg(int foot)
    {
        int first = foot == 0 ? 7 : 13;
        Vector3 hip = body.BodyPartTransform(first).Origin;
        var tuck = new Basis(Vector3.Back, foot == 0 ? .28f : -.28f);
        for (int i = first; i < first + 6; i++)
        {
            var part = body.BodyPartTransform(i);
            body.Part(i).Transform = new Transform3D(tuck * part.Basis, hip + tuck * (part.Origin - hip));
        }
    }

    private static StandardMaterial3D MakeWingMaterial()
    {
        const int size = 64;
        var bytes = new byte[size * size];
        using var noise = new FastNoiseLite { Seed = 581, Frequency = .19f, FractalOctaves = 3 };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                bytes[y * size + x] = (byte)Mathf.Clamp(Mathf.RoundToInt(223 + noise.GetNoise2D(x * .7f, y * 1.8f) * 49), 0, 255);
        using var image = Image.CreateFromData(size, size, false, Image.Format.L8, bytes);
        image.GenerateMipmaps();
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image), VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = .96f, MetallicSpecular = 0,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled
        };
    }

    private static Mesh MakeWing(int side)
    {
        var membrane = new Color("68648e");
        var edge = new Color("354862");
        var tips = new Color("78889d");
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Mirror(Vector3 p) => new(p.X * side, p.Y, p.Z);
        void Vertex(Vector3 p, Color color)
        {
            surface.SetColor(color);
            surface.SetUV(new Vector2(p.X / 7.12f, (p.Z + 1.38f) / 3.74f));
            surface.AddVertex(Mirror(p));
        }
        for (int i = 0; i < WingOutline.Length; i++)
        {
            Vector3 a = WingOutline[i], b = WingOutline[(i + 1) % WingOutline.Length];
            Vertex(WingRidge, membrane);
            Vertex(side > 0 ? a : b, edge.Lerp(tips, (side > 0 ? a.X : b.X) / 8));
            Vertex(side > 0 ? b : a, edge.Lerp(tips, (side > 0 ? b.X : a.X) / 8));
        }
        foreach (int i in new[] { 1, 3, 4, 6, 8, 9 })
        {
            Vector3 a = WingOutline[i] + Vector3.Up * .016f;
            Vector3 b = WingRidge + Vector3.Up * .018f;
            Vertex(a, tips);
            Vertex(b + new Vector3(-.045f, 0, .065f), edge.Lightened(.19f));
            Vertex(b + new Vector3(.045f, 0, -.065f), edge);
        }
        surface.GenerateNormals();
        return surface.Commit();
    }
}
