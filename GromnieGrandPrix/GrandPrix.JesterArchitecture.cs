using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const uint JesterMasonryColumn = 0x0200040B;
    private const uint JesterStoneSteps = 0x02000300;
    private const uint JesterStreetLamp = 0x0200071A;
    private const uint JesterGraveyardTower = 0x01001106;
    private const uint JesterGraveyardHall = 0x01000F69;
    private const uint JesterGraveyardRoof = 0x010011C0;
    private const uint JesterGraveyardPlatform = 0x01004031;

    private void BuildJesterArchitecture(Node3D root, BogCircuit circuit)
    {
        var gallery = new Node3D { Name = "Stone gallery" };
        var inversion = new Node3D { Name = "Inverted streets" };
        var ruins = new Node3D { Name = "Floating colonnade" };
        root.AddChild(gallery);
        root.AddChild(inversion);
        root.AddChild(ruins);

        var prototypes = new Dictionary<uint, (Aabb Bounds, (Mesh Mesh, Transform3D Pose)[] Parts)>();
        var batches = new Dictionary<(Node3D District, Mesh Mesh, int X, int Z), List<Transform3D>>();
        int sections = Mathf.CeilToInt(circuit.Length / 6);
        var road = new (Vector3 Point, float Width, float Bank)[sections];
        for (int i = 0; i < sections; i++)
        {
            float station = circuit.Length * i / sections;
            float width = circuit.WidthAt(station);
            road[i] = (circuit.Point(station), width, MathF.Abs(circuit.Right(station).Y) * width);
        }

        // Lamps and level stone lintels establish which way is up before the descent.
        float firstStation = Math.Max(BogCircuit.Grid + 23, 38);
        for (int bay = 0; bay < 8; bay++)
        {
            float station = firstStation + bay * 21;
            var (center, forward, right, heading, width) = Frame(station);
            float columnLane = width + 5.8f;
            float floor = MathF.Min(circuit.Surface(station, -width).Y, circuit.Surface(station, width).Y) - .3f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 edge = (center + right * (side * columnLane)) with { Y = floor };
                Place(gallery, JesterMasonryColumn, edge, heading, new(3, 3.05f, 3));
                Place(gallery, JesterStreetLamp, edge + forward * 4 - right * side * .9f, heading, Vector3.One * 3.2f);
            }
            if (bay % 2 == 0)
                Place(gallery, JesterGraveyardPlatform, center with { Y = floor + 16.2f }, heading,
                    new((columnLane * 2 + 3.6f) / 22.6f, .85f, .27f));
        }

        for (int block = 0; block < 6; block++)
        {
            float station = circuit.Length * (.095f + block * .024f);
            var (center, forward, right, heading, width) = Frame(station);
            float side = block % 2 == 0 ? 1 : -1;
            Vector3 terrace = center + right * side * (width + 31) + Vector3.Down * 3.2f;
            Place(gallery, JesterGraveyardPlatform, terrace, heading, new(1.55f, .72f, 1.5f));
            Place(gallery, block % 3 == 0 ? JesterGraveyardTower : JesterGraveyardHall,
                terrace + Vector3.Up * 1.6f, heading, Vector3.One * 1.42f);
            Place(gallery, JesterStoneSteps, terrace + forward * 17, heading, Vector3.One * 1.9f);
        }

        // Detached houses are still legible upside down. Their stairs keep climbing along the wrong axes.
        for (int block = 0; block < 12; block++)
        {
            float station = circuit.Length * (.245f + block * .027f);
            var (center, forward, right, heading, width) = Frame(station);
            float side = block % 2 == 0 ? -1 : 1;
            bool fullyInverted = block % 3 != 1;
            Basis tilt = heading * new Basis(Vector3.Back, side * (fullyInverted ? Mathf.Pi - .18f : 1.22f));
            Vector3 apron = center + right * side * (width + 40) + Vector3.Up * (13 + block % 4 * 5);
            Place(inversion, JesterGraveyardPlatform, apron, tilt, new(1.5f, 1.4f, 1.8f), anchorBottom: false);
            Place(inversion, block % 3 == 0 ? JesterGraveyardTower : JesterGraveyardHall,
                apron + tilt.Y * 2, tilt, Vector3.One * (1.35f + block % 3 * .28f), anchorBottom: false);

            Basis stairDirection = heading * new Basis(Vector3.Right, -.43f) * new Basis(Vector3.Back, side * 1.15f);
            Vector3 firstStep = center - right * side * (width + 14) + forward * (14 + block % 4 * 4) + Vector3.Up * 2.5f;
            for (int step = 0; step < 7; step++)
            {
                Vector3 anchor = firstStep + stairDirection * new Vector3(0, step * 2.25f, -step * 5.7f);
                Place(inversion, JesterStoneSteps, anchor, stairDirection, Vector3.One * 2.75f, anchorBottom: false);
            }

            Basis roofPitch = heading * Basis.FromEuler(new(-.4f, .31f, side * 1.78f));
            Vector3 roof = center + right * side * (width + 31) + forward * 13 + Vector3.Up * 36;
            Place(inversion, JesterGraveyardRoof, roof, roofPitch, Vector3.One * 2.2f, anchorBottom: false, drift: block % 2 == 0);
        }

        // Each flight climbs in its own frame. The landings close a square without sharing an up direction.
        float staircase = circuit.Length * .335f;
        var (squareCenter, _, squareRight, squareHeading, squareWidth) = Frame(staircase);
        Vector3 heart = squareCenter - squareRight * (squareWidth + 54) + Vector3.Up * 37;
        Basis square = squareHeading * Basis.FromEuler(new(-.26f, -.62f, .43f));
        for (int flight = 0; flight < 4; flight++)
        {
            Basis corner = new(Vector3.Forward, flight * Mathf.Pi * .5f);
            Basis incline = square * corner * Basis.FromEuler(new(.36f, -Mathf.Pi * .5f, -.22f));
            for (int step = 0; step < 7; step++)
            {
                Vector3 block = corner * new Vector3(-21 + step * 7.0f, -24 + step * .5f, step * .62f);
                Place(inversion, JesterStoneSteps, heart + square * block, incline, Vector3.One * 3.1f,
                    anchorBottom: false);
            }
        }

        // Deep in the descent whole porticoes have sheared off their foundations.
        for (int group = 0; group < 10; group++)
        {
            float station = circuit.Length * (.581f + group * .032f);
            var (center, forward, right, heading, width) = Frame(station);
            float side = group % 2 == 0 ? 1 : -1;
            Basis pitch = heading * Basis.FromEuler(new(.12f + group % 3 * .13f, .10f, side * (.31f + group % 4 * .16f)));
            for (int arch = 0; arch < 4; arch++)
            {
                Vector3 threshold = center + right * side * (width + 31 + arch * 6.7f)
                    + forward * ((arch - 1.5f) * 18) + Vector3.Up * (6 + arch * 7.8f);
                float size = 1.05f + arch * .30f;
                for (int column = -1; column <= 1; column += 2)
                    Place(ruins, JesterMasonryColumn, threshold + pitch.X * (column * 4.45f * size), pitch,
                        new(2.8f * size, 2.35f * size, 2.8f * size), anchorBottom: false);
                Place(ruins, JesterGraveyardPlatform, threshold + pitch.Y * (12.9f * size), pitch,
                    new(.49f * size, .95f * size, .25f * size), anchorBottom: false);
            }
        }

        foreach (var (key, list) in batches)
        {
            Vector3 sectorCenter = new((key.X + .5f) * 180, list[0].Origin.Y, (key.Z + .5f) * 180);
            var meshes = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = key.Mesh, InstanceCount = list.Count
            };
            for (int i = 0; i < list.Count; i++)
            {
                var transform = list[i];
                transform.Origin -= sectorCenter;
                meshes.SetInstanceTransform(i, transform);
            }
            key.District.AddChild(new MultiMeshInstance3D
            {
                Position = sectorCenter, Multimesh = meshes, Layers = 1u | JesterReflectionSourceLayer, VisibilityRangeEnd = 630,
                VisibilityRangeEndMargin = 42, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }

        (Vector3 Center, Vector3 Forward, Vector3 Right, Basis Heading, float Width) Frame(float station)
        {
            Vector3 forward = (circuit.Forward(station) with { Y = 0 }).Normalized();
            if (forward.LengthSquared() < .01f) forward = Vector3.Forward;
            return (circuit.Point(station), forward, forward.Cross(Vector3.Up), Basis.LookingAt(forward), circuit.WidthAt(station));
        }

        void Place(Node3D district, uint id, Vector3 position, Basis orientation, Vector3 scale, bool anchorBottom = true, bool drift = false)
        {
            if (!prototypes.TryGetValue(id, out var prototype))
            {
                var parts = new List<(Mesh Mesh, Transform3D Pose)>();
                if (id >> 24 == 1) parts.Add((assets.Mesh(id), Transform3D.Identity));
                else
                {
                    var setup = assets.Setup(id);
                    for (int i = 0; i < setup.Parts.Length; i++)
                        if (setup.Parts[i] != 0)
                            parts.Add((assets.Mesh(setup.Parts[i]), AcAssets.Convert(setup.Placement[i])
                                .ScaledLocal(AcAssets.Convert(setup.Scales[i]).Abs())));
                }
                Aabb bounds = parts[0].Pose * parts[0].Mesh.GetAabb();
                for (int i = 1; i < parts.Count; i++) bounds = bounds.Merge(parts[i].Pose * parts[i].Mesh.GetAabb());
                prototypes[id] = prototype = (bounds, parts.ToArray());
            }

            Basis basis = orientation * Basis.FromScale(scale);
            if (anchorBottom)
            {
                Aabb relative = new Transform3D(basis, Vector3.Zero) * prototype.Bounds;
                position.Y -= relative.Position.Y;
            }
            var pose = new Transform3D(basis, position);
            Aabb world = pose * prototype.Bounds;
            if (drift) world = world.Grow(3.2f);

            // Check both levels of road and keep the chase camera clear of the masonry.
            foreach (var (point, width, bank) in road)
            {
                if (point.Y < world.Position.Y - bank - 10 || point.Y > world.End.Y + bank + 4) continue;
                float dx = MathF.Max(0, MathF.Max(world.Position.X - point.X, point.X - world.End.X));
                float dz = MathF.Max(0, MathF.Max(world.Position.Z - point.Z, point.Z - world.End.Z));
                float clearance = width + 2.8f;
                if (dx * dx + dz * dz < clearance * clearance) return;
            }

            if (drift)
            {
                var roof = new Node3D { Name = "Drifting roof", Position = position };
                district.AddChild(roof);
                pose.Origin = Vector3.Zero;
                foreach (var (mesh, part) in prototype.Parts)
                    roof.AddChild(new MeshInstance3D
                    {
                        Mesh = mesh, Transform = pose * part, Layers = 1u | JesterReflectionSourceLayer,
                        VisibilityRangeEnd = 640, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
                    });
                var sway = roof.CreateTween().SetLoops().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                sway.TweenProperty(roof, "position", position + Vector3.Up * 2.8f, 5.7);
                sway.TweenProperty(roof, "position", position, 5.7);
                return;
            }

            foreach (var (mesh, part) in prototype.Parts)
            {
                var key = (district, mesh, Mathf.FloorToInt(position.X / 180), Mathf.FloorToInt(position.Z / 180));
                if (!batches.TryGetValue(key, out var list)) batches[key] = list = [];
                list.Add(pose * part);
            }
        }
    }
}
