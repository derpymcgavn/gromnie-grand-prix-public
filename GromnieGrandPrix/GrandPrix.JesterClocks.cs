using Godot;

public partial class GrandPrix
{
    private static void BuildJesterClocks(Node3D root, BogCircuit circuit)
    {
        var casing = JesterClockMaterial("c49a55", .82f, .23f, .11f);
        var enamel = JesterClockMaterial("d9caaa", .04f, .42f, .28f);
        var lettering = JesterClockMaterial("241629", .15f, .48f, .05f);
        ArrayMesh[] clocks =
        [
            BuildJesterClock(.68f, .28f, 10, 10, casing, enamel, lettering),
            BuildJesterClock(.55f, -.39f, 3, 47, casing, enamel, lettering),
            BuildJesterClock(.81f, .48f, 7, 33, casing, enamel, lettering)
        ];
        var scenery = new Node3D { Name = "Melting clocks" };
        root.AddChild(scenery);

        int sectionCount = Math.Max(8, Mathf.CeilToInt(circuit.Length / 9));
        var road = new (Vector3 Center, float Clearance)[sectionCount];
        for (int i = 0; i < road.Length; i++)
        {
            float station = i * circuit.Length / road.Length;
            road[i] = (circuit.Point(station), circuit.WidthAt(station) + 5);
        }

        int count = Mathf.Clamp(Mathf.RoundToInt(circuit.Length / 440), 6, 9);
        float[] shifts = [0, -29, 35, 63, -67];
        for (int index = 0; index < count; index++)
        {
            bool landmark = index % 3 == 0;
            float scale = landmark ? 10.5f : index % 3 == 1 ? 6.4f : 4.3f;
            float target = index == 0 ? Math.Min(76, circuit.Length * .09f) : circuit.Length * (index + .24f) / count;
            float preferredSide = index % 2 == 0 ? 1 : -1;
            ArrayMesh clock = clocks[index % clocks.Length];
            foreach (float shift in shifts)
            {
                float station = target + shift;
                if (station < BogCircuit.Grid + 18 || station >= circuit.Length - 12) continue;
                if (TryPlace(clock, index, station, scale, preferredSide) || TryPlace(clock, index, station, scale, -preferredSide))
                    break;
            }
        }

        bool TryPlace(ArrayMesh clock, int index, float station, float scale, float side)
        {
            Vector3 heading = circuit.Forward(station) with { Y = 0 };
            if (heading.LengthSquared() < .06f) return false;
            heading = heading.Normalized();
            Vector3 outward = heading.Cross(Vector3.Up) * side;
            Vector3 center = circuit.Point(station) + outward * (circuit.WidthAt(station) + 8 + scale * 1.25f)
                + Vector3.Up * (3.4f + scale * .63f);
            Vector3 face = (-heading * .8f - outward * .6f).Normalized();
            var basis = Basis.LookingAt(face, Vector3.Up, true) * new Basis(Vector3.Back, side * (.14f + index % 3 * .045f));
            var transform = new Transform3D(basis.Scaled(Vector3.One * scale), center);
            Aabb world = transform * clock.GetAabb();
            Vector3 middle = world.GetCenter(), half = world.Size * .5f;
            foreach (var (point, clearance) in road)
            {
                Vector3 gap = (point - middle).Abs() - half;
                Vector3 nearest = new(Math.Max(0, gap.X), Math.Max(0, gap.Y), Math.Max(0, gap.Z));
                if (nearest.LengthSquared() < clearance * clearance) return false;
            }

            scenery.AddChild(new MeshInstance3D
            {
                Name = $"Melting clock {index + 1}", Mesh = clock, Transform = transform,
                Layers = 1u | JesterReflectionSourceLayer | JesterMirrorLayer,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = 380 + scale * 9, VisibilityRangeEndMargin = 25
            });
            return true;
        }
    }

    private static StandardMaterial3D JesterClockMaterial(string color, float metallic, float roughness, float fill)
    {
        var tint = new Color(color);
        return new StandardMaterial3D
        {
            AlbedoColor = tint, Metallic = metallic, MetallicSpecular = .51f, Roughness = roughness,
            EmissionEnabled = true, Emission = tint, EmissionEnergyMultiplier = fill
        };
    }

    private static ArrayMesh BuildJesterClock(float drop, float slip, int hour, int minute,
        Material casing, Material enamel, Material lettering)
    {
        const int radialSteps = 56;
        const float dialRadius = .88f, faceDepth = .032f, glyphDepth = .052f;
        var mesh = new ArrayMesh();

        using (var rim = Surface(casing))
        {
            Band(rim, dialRadius, faceDepth, .935f, .099f);
            Band(rim, .935f, .099f, 1.035f, .040f);
            Band(rim, 1.035f, .040f, 1.035f, -.16f);
            Band(rim, 1.035f, -.16f, .96f, -.235f);
            Disc(rim, .96f, -.235f, 6, back: true);
            // The winding stem and bow remain recognizable as the face flows downward.
            Mark(rim, new(-.092f, .98f), new(.092f, .98f), new(.092f, 1.15f), new(-.092f, 1.15f), .008f);
            for (int tooth = -2; tooth <= 2; tooth++)
                Stroke(rim, new(tooth * .037f, 1.102f), new(tooth * .037f, 1.155f), .019f, .034f);
            Band(rim, .092f, -.007f, .136f, .035f, new Vector2(0, 1.247f));
            Disc(rim, .060f, .098f, 1);
            Commit(rim);
        }
        using (var dial = Surface(enamel))
        {
            Disc(dial, dialRadius, faceDepth, 8);
            Commit(dial);
        }
        using (var detail = Surface(lettering))
        {
            string[] roman = ["XII", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI"];
            for (int tick = 0; tick < 60; tick++)
            {
                bool hourMark = tick % 5 == 0;
                Vector2 ray = PointOnClock(tick * (Mathf.Tau / 60));
                Stroke(detail, ray * (hourMark ? .788f : .826f), ray * .852f, hourMark ? .021f : .010f, glyphDepth);
                if (hourMark) Numeral(detail, roman[tick / 5], ray * .66f);
            }
            Hand(detail, (hour + minute / 60f) * (Mathf.Tau / 12), .42f, .053f);
            Hand(detail, minute * (Mathf.Tau / 60), .56f, .032f);
            Commit(detail);
        }
        return mesh;

        static SurfaceTool Surface(Material material)
        {
            var surface = new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            surface.SetMaterial(material);
            return surface;
        }

        void Commit(SurfaceTool surface)
        {
            surface.GenerateNormals();
            surface.Index();
            surface.Commit(mesh);
        }

        Vector3 Warp(Vector2 point, float depth)
        {
            float sag = Mathf.SmoothStep(.06f, 1, -point.Y);
            float fold = Mathf.SmoothStep(.25f, 1.03f, point.X) * (1 - Mathf.SmoothStep(.32f, .95f, Mathf.Abs(point.Y)));
            return new Vector3(
                point.X * (1 - .27f * sag) + slip * sag * sag + .065f * point.Y,
                point.Y - drop * sag * (1 + .16f * Mathf.Sign(slip) * point.X),
                depth + .09f * (point.X * point.X - point.Y * point.Y) + .14f * sag - .24f * fold);
        }

        void Disc(SurfaceTool surface, float radius, float depth, int rings, bool back = false)
        {
            for (int i = 0; i < radialSteps; i++)
            {
                Vector3 a = Warp(PointOnClock(i * (Mathf.Tau / radialSteps)) * (radius / rings), depth);
                Vector3 b = Warp(PointOnClock((i + 1) * (Mathf.Tau / radialSteps)) * (radius / rings), depth);
                Triangle(surface, Warp(Vector2.Zero, depth), a, b, back);
            }
            for (int ring = 1; ring < rings; ring++)
                Band(surface, radius * ring / rings, depth, radius * (ring + 1) / rings, depth, back: back);
        }

        void Band(SurfaceTool surface, float inner, float innerDepth, float outer, float outerDepth,
            Vector2 center = default, bool back = false)
        {
            for (int i = 0; i < radialSteps; i++)
            {
                Vector2 a = PointOnClock(i * (Mathf.Tau / radialSteps));
                Vector2 b = PointOnClock((i + 1) * (Mathf.Tau / radialSteps));
                Vector3 start = Warp(center + a * inner, innerDepth), end = Warp(center + b * inner, innerDepth);
                Vector3 edge = Warp(center + a * outer, outerDepth), next = Warp(center + b * outer, outerDepth);
                Triangle(surface, start, edge, next, back);
                Triangle(surface, start, next, end, back);
            }
        }

        void Mark(SurfaceTool surface, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float depth)
        {
            Triangle(surface, Warp(a, depth), Warp(b, depth), Warp(c, depth), (b - a).Cross(c - a) > 0);
            Triangle(surface, Warp(a, depth), Warp(c, depth), Warp(d, depth), (c - a).Cross(d - a) > 0);
        }

        void Stroke(SurfaceTool surface, Vector2 a, Vector2 b, float width, float depth)
        {
            Vector2 side = (b - a).Orthogonal().Normalized() * (width * .5f);
            Mark(surface, a - side, a + side, b + side, b - side, depth);
        }

        void Numeral(SurfaceTool surface, string text, Vector2 center)
        {
            float pitch = text.Length > 2 ? .046f : .055f;
            for (int index = 0; index < text.Length; index++)
            {
                Vector2 at = center + new Vector2((index - (text.Length - 1) * .5f) * pitch, 0);
                Vector2 upper = at + new Vector2(0, .059f), lower = at - new Vector2(0, .059f);
                Vector2 off = Vector2.Right * .018f;
                switch (text[index])
                {
                    case 'I':
                        Stroke(surface, lower, upper, .013f, glyphDepth);
                        Stroke(surface, upper - off * .69f, upper + off * .69f, .009f, glyphDepth);
                        Stroke(surface, lower - off * .69f, lower + off * .69f, .009f, glyphDepth);
                        break;
                    case 'V':
                        Stroke(surface, upper - off, lower, .012f, glyphDepth);
                        Stroke(surface, upper + off, lower, .012f, glyphDepth);
                        break;
                    case 'X':
                        Stroke(surface, upper - off, lower + off, .012f, glyphDepth);
                        Stroke(surface, upper + off, lower - off, .012f, glyphDepth);
                        break;
                }
            }
        }

        void Hand(SurfaceTool surface, float angle, float length, float width)
        {
            const float depth = .075f;
            Vector2 ray = PointOnClock(angle);
            Vector2 blade = ray * (length - .086f), side = ray.Orthogonal() * (width * .86f);
            Stroke(surface, -ray * .10f, blade, width, depth);
            Vector2 tip = ray * length;
            Triangle(surface, Warp(blade - side, depth), Warp(blade + side, depth),
                Warp(tip, depth), (side * 2).Cross(tip - blade + side) > 0);
        }

        static Vector2 PointOnClock(float angle) => new(Mathf.Sin(angle), Mathf.Cos(angle));
        static void Triangle(SurfaceTool surface, Vector3 a, Vector3 b, Vector3 c, bool reverse)
        {
            surface.AddVertex(a);
            surface.AddVertex(reverse ? c : b);
            surface.AddVertex(reverse ? b : c);
        }
    }
}
