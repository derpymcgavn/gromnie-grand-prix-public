using Godot;
using System.Runtime.CompilerServices;

public sealed partial class MountedRacer
{
    private readonly record struct FurPatch(MeshInstance3D Node, Vector3 Pivot, float Phase);
    private readonly record struct FurGroom(uint Setup, int Part, int Color);
    private readonly record struct FurVertex(Vector3 Position, Vector3 Normal, Vector2 Uv);
    private readonly record struct FurTriangle(FurVertex A, FurVertex B, FurVertex C, Vector3 Normal,
        StandardMaterial3D? Material, float EndArea);

    private static readonly ConditionalWeakTable<Mesh, Dictionary<FurGroom, ArrayMesh>> TuskerCoats = new();
    private static readonly bool ShortTuskerFur = OS.HasFeature("android");
    private static StandardMaterial3D? tuskerCoatMaterial;
    private FurPatch[]? tuskerFur;
    private float furTime, furWind;

    private void BuildTuskerFur(int mountColor)
    {
        bool snow = Rig.Kind == RaceMountKind.SnowTusker;
        if (!snow && Rig.Kind != RaceMountKind.Tusker) return;
        var coat = new List<FurPatch>();
        foreach (var (source, part) in Mount.BodyMeshBindings)
        {
            // Face, hands, feet, and equipment remain the native skin, ivory, or leather.
            if (!source.Visible || source.Mesh is not { } body ||
                !(part is 0 or 1 or 2 or 3 or 5 or 6 or 8 or 9 or 11 or 12 || snow && part is 23 or 24))
                continue;

            var key = new FurGroom(Mount.Setup.Id, part, mountColor);
            var variants = TuskerCoats.GetOrCreateValue(body);
            if (!variants.TryGetValue(key, out var strands))
            {
                Basis bodyBasis = (Mount.BodyPartTransform(part) * source.Transform).Basis;
                strands = GroomTuskerPart(source, bodyBasis, part, snow);
                variants.Add(key, strands);
            }
            if (strands.GetSurfaceCount() == 0) continue;
            var patch = new MeshInstance3D
            {
                Name = $"Tusker Fur {part}", Mesh = strands,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = ShortTuskerFur ? 36 : 95,
                Layers = source.Layers
            };
            // Parent directly to the worn mesh so native joint poses and part scale stay exact.
            source.AddChild(patch);
            coat.Add(new FurPatch(patch, body.GetAabb().GetCenter(), part * 1.21f));
        }
        tuskerFur = coat.ToArray();
    }

    private void UpdateTuskerFur(float dt, bool moving)
    {
        if (tuskerFur is not { Length: > 0 } coat || dt <= 0) return;
        furTime = Mathf.PosMod(furTime + dt * 4.5f, Mathf.Tau);
        float target = moving ? Mathf.Clamp(Speed / 44f, 0, 1.25f) : 0;
        furWind = Mathf.Lerp(furWind, target, 1 - Mathf.Exp(-7 * dt));
        // A small anchored shear makes the coat ripple without deforming its ray-tracing mesh.
        foreach (var patch in coat)
        {
            float ripple = Mathf.Sin(furTime + patch.Phase);
            float sway = ripple * (.004f + furWind * .011f);
            var basis = new Basis(Vector3.Right, new Vector3(sway, 1, -.013f * furWind), Vector3.Back);
            patch.Node.Transform = new Transform3D(basis, patch.Pivot - basis * patch.Pivot);
        }
    }

    private static ArrayMesh GroomTuskerPart(MeshInstance3D source, Basis bodyBasis, int part, bool snow)
    {
        var faces = new List<FurTriangle>();
        float area = 0;
        Mesh body = source.Mesh!;
        for (int surface = 0; surface < body.GetSurfaceCount(); surface++)
        {
            var arrays = body.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var texcoords = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            var material = source.GetActiveMaterial(surface) as StandardMaterial3D;
            int count = indices.Length == 0 ? vertices.Length : indices.Length;
            for (int i = 0; i + 2 < count; i += 3)
            {
                int a = indices.Length == 0 ? i : indices[i];
                int b = indices.Length == 0 ? i + 1 : indices[i + 1];
                int c = indices.Length == 0 ? i + 2 : indices[i + 2];
                Vector3 cross = (vertices[c] - vertices[a]).Cross(vertices[b] - vertices[a]);
                float twiceArea = (bodyBasis * (vertices[c] - vertices[a])).Cross(bodyBasis * (vertices[b] - vertices[a])).Length();
                if (twiceArea <= .00001f || !float.IsFinite(twiceArea)) continue;
                area += twiceArea * .5f;
                Vector3 normal = cross.Normalized();
                if (normals.Length == vertices.Length && normal.Dot(normals[a] + normals[b] + normals[c]) < 0)
                    normal = -normal;
                faces.Add(new FurTriangle(Vertex(a), Vertex(b), Vertex(c), normal, material, area));

                FurVertex Vertex(int index) => new(vertices[index], normals.Length == vertices.Length ? normals[index] : normal,
                    texcoords.Length == vertices.Length ? texcoords[index] : Vector2.Zero);
            }
        }
        if (faces.Count == 0) return new ArrayMesh();

        // Area-stratified seeds avoid sparse broad polygons and clumps on tiny triangles.
        int tuftCount = Mathf.Clamp(Mathf.RoundToInt(area * (ShortTuskerFur ? 100 : snow ? 370 : 290)), 18,
            ShortTuskerFur ? 145 : part is 0 or 1 ? 845 : 380);
        bool upper = part is 1 or 2 or 5 or 23 or 24;
        Vector3 flowDirection = (bodyBasis.Inverse() * new Vector3(0, -.86f, .50f)).Normalized();
        var random = new Random(0x6417 + part * 871);
        var pixels = new Dictionary<Texture2D, Image?>();
        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        mesh.SetMaterial(tuskerCoatMaterial ??= new StandardMaterial3D
        {
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = .87f,
            MetallicSpecular = .24f, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true
        });

        int faceIndex = 0;
        int strands = 0;
        try
        {
            for (int i = 0; i < tuftCount; i++)
            {
                float sampleArea = (i + Next()) / tuftCount * area;
                while (faces[faceIndex].EndArea < sampleArea && faceIndex < faces.Count - 1) faceIndex++;
                var triangle = faces[faceIndex];
                float u = Next(), v = Next();
                if (u + v > 1) (u, v) = (1 - u, 1 - v);
                float w = 1 - u - v;
                Vector3 anchor = triangle.A.Position * w + triangle.B.Position * u + triangle.C.Position * v;
                Vector3 normal = (triangle.A.Normal * w + triangle.B.Normal * u + triangle.C.Normal * v).Normalized();
                if (normal.Dot(triangle.Normal) < .25f) normal = triangle.Normal;
                Vector3 stream = flowDirection - normal * normal.Dot(flowDirection);
                if (stream.LengthSquared() < .045f)
                {
                    Vector3 fallback = Mathf.Abs(normal.X) < .8f ? Vector3.Right : Vector3.Up;
                    stream = fallback - normal * normal.Dot(fallback);
                }
                stream = stream.Normalized().Rotated(normal, (Next() - .5f) * .92f);
                Vector3 side = stream.Cross(normal).Normalized();
                float length = (snow ? .113f : .082f) * Mathf.Lerp(.57f, 1.40f, Next()) * (upper ? 1.12f : .80f);
                float halfWidth = length * Mathf.Lerp(.11f, .22f, Next());
                Color color = NativeColor(triangle.Material, triangle.A.Uv * w + triangle.B.Uv * u + triangle.C.Uv * v);
                if (color.A < .12f) continue;
                color.A = 1;
                float tint = Mathf.Lerp(.81f, 1.12f, Next());
                var dark = Shade(color, tint * .68f);
                var belly = Shade(color, tint);
                var tip = Shade(color, tint * 1.14f);
                Vector3 root = anchor - normal * .029f;
                Vector3 bend = anchor + (normal * .47f + stream * .37f) * length;
                Vector3 point = anchor + (normal * .70f + stream * .83f) * length;
                AddBlade(side, halfWidth);
                // The main mane and close camera need volume from both viewing directions.
                if (!ShortTuskerFur && (upper || i % 3 == 0))
                    AddBlade((side * .28f + stream * .96f).Normalized(), halfWidth * .68f);
                strands++;

                void AddBlade(Vector3 axis, float width)
                {
                    Vector3 left = root - axis * width, right = root + axis * width;
                    Vector3 middleLeft = bend - axis * width * .58f, middleRight = bend + axis * width * .58f;
                    Vector3 lightNormal = (normal * .78f - stream * .46f).Normalized();
                    Add(left, dark); Add(middleLeft, belly); Add(right, dark);
                    Add(right, dark); Add(middleLeft, belly); Add(middleRight, belly);
                    Add(middleLeft, belly); Add(point, tip); Add(middleRight, belly);

                    void Add(Vector3 vertex, Color shade)
                    {
                        mesh.SetNormal(lightNormal);
                        mesh.SetColor(shade);
                        mesh.AddVertex(vertex);
                    }
                }
            }
        }
        finally
        {
            foreach (var image in pixels.Values) image?.Dispose();
        }
        if (strands == 0) return new ArrayMesh();
        mesh.Index();
        return mesh.Commit();

        float Next() => random.NextSingle();

        Color NativeColor(StandardMaterial3D? material, Vector2 uv)
        {
            if (material is null) return snow ? new Color("dce5e6") : new Color("75614c");
            var color = material.AlbedoColor;
            if (material.AlbedoTexture is not { } texture) return color;
            if (!pixels.TryGetValue(texture, out var image))
            {
                image = texture.GetImage();
                if (image?.IsCompressed() == true) image.Decompress();
                pixels.Add(texture, image);
            }
            if (image is null || image.IsEmpty() || image.IsCompressed()) return color;
            var offset = material.Uv1Offset;
            var scale = material.Uv1Scale;
            int x = Mathf.FloorToInt(Mathf.PosMod(uv.X * scale.X + offset.X, 1) * image.GetWidth());
            int y = Mathf.FloorToInt(Mathf.PosMod(uv.Y * scale.Y + offset.Y, 1) * image.GetHeight());
            return color * image.GetPixel(Math.Min(x, image.GetWidth() - 1), Math.Min(y, image.GetHeight() - 1));
        }

        static Color Shade(Color color, float change) => new(
            Mathf.Clamp(color.R * change, 0, 1), Mathf.Clamp(color.G * change, 0, 1), Mathf.Clamp(color.B * change, 0, 1));
    }
}
