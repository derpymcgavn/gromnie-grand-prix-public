using Godot;
using DerethSurvivors;

internal sealed record LandmarkCircuit(int Id, string Name, string Subtitle, uint Center, float HalfWidth,
    float[][] Waypoints, LandmarkCircuit.Landmark[] Landmarks, LandmarkCircuit.CitadelBattle? Battle = null, int GridSide = 8)
{
    internal sealed record CitadelBattle(float[] Launch, float[] Landing, float[] Deck, float[] FlybyOffset)
    {
        public Vector3 Platform(BogCircuit track) => new(Deck[0], Deck[1] - track.NativeElevationOrigin, Deck[2]);
        public Vector3 FlybyPoint(BogCircuit track) => Platform(track) + new Vector3(FlybyOffset[0], FlybyOffset[1], FlybyOffset[2]);
        public (float Start, float End) FlightSector(BogCircuit track)
        {
            return (Station(Launch), Station(Landing));
            float Station(float[] point) => Mathf.PosMod(track.Project(new(point[0], 0, point[1]), track.Length * .5f).Distance, track.Length);
        }
    }

    internal sealed record Landmark(string Name, float[] Location, float Reach)
    {
        public Vector2 Point => new(Location[0], Location[1]);
    }

    public BogCircuit Build(AcLandArena land) => new(Name, Waypoints.Select(point => new Vector2(point[0], point[1])).ToArray(),
        land, HalfWidth, gradedRoad: true);

    public void Validate(BogCircuit track, AcLandArena land)
    {
        if (land.GridSide != GridSide || track.Length < 3400 || track.HalfWidth < 12 ||
            track.TightestInsideRadius() < BogCircuit.MinimumInsideRadius)
            throw new InvalidDataException($"Invalid road dimensions for {Name} ({track.Length:F0} m, inside turn {track.TightestInsideRadius():F1} m).");
        var blocks = new HashSet<uint>();
        float[] lanes = [-track.HalfWidth, 0, track.HalfWidth];
        for (float at = 0; at < track.Length; at += 3)
        {
            Vector3 point = track.Point(at);
            float stepHeight = Math.Abs(point.Y - track.Point(at + 3).Y);
            if (!land.WorldBounds.Grow(-HalfWidth - 5).HasPoint(new(point.X, point.Z)) ||
                stepHeight > 2.35f)
                throw new InvalidDataException($"The road leaves its usable terrain in {Name} at {at:F0} m ({point.X:F0}, {point.Z:F0}, grade change {stepHeight:F2} m).");
            var road = new System.Numerics.Vector3(point.X, point.Y + 1.22f, point.Z);
            if (land.Physics.Trace(road, road, HalfWidth + 1.2f, includeTerrain: false, stepTop: point.Y + .22f) is not null)
                throw new InvalidDataException($"Native scenery blocks {Name} at {at:F0} m ({point.X:F0}, {point.Z:F0}).");
            foreach (float lane in lanes)
            {
                Vector3 edge = track.Surface(at, lane);
                if (!track.NativeDry(edge) && edge.Y - track.FreeGround(edge).Y < 1.9f)
                    throw new InvalidDataException($"An unbridged water tile crosses {Name} at {at:F0} m ({edge.X:F0}, {edge.Z:F0}).");
            }
            blocks.Add(land.Resolve(point.X, point.Z).Block.Id);
        }
        if (blocks.Count < 20) throw new InvalidDataException($"{Name} only crosses {blocks.Count} landblocks.");
        foreach (var landmark in Landmarks)
        {
            Vector2 point = landmark.Point;
            var projection = track.Project(new(point.X, 0, point.Y), track.Length * .5f);
            var nearest = track.Point(projection.Distance);
            float gap = point.DistanceTo(new(nearest.X, nearest.Z));
            if (!land.WorldBounds.HasPoint(point) || gap > landmark.Reach)
                throw new InvalidDataException($"{Name} misses {landmark.Name} ({gap:F0} m).");
        }
        GD.Print($"{Name}: {land.Blocks.Count} native landblocks loaded, {blocks.Count} crossed, {track.Length:F0} m, {Landmarks.Length} landmarks.");
    }

    public void BuildLandmarkSigns(Node3D course, BogCircuit track)
    {
        var signs = new Node3D { Name = "Landmark signs" };
        course.AddChild(signs);
        var post = BogCircuit.Paint("40352e");
        var plaque = BogCircuit.Paint("414139");
        foreach (var landmark in Landmarks)
        {
            Vector2 point = landmark.Point;
            var nearest = track.Project(new Vector3(point.X, 0, point.Y), track.Length * .5f);
            int toward = nearest.Lane >= 0 ? 1 : -1;
            bool placed = false;
            foreach (float advance in new[] { 10f, 21f, 32f, 44f })
            {
                float at = nearest.Distance - advance;
                if (TryPlace(at, toward) || TryPlace(at, -toward))
                {
                    placed = true;
                    break;
                }
            }
            if (!placed)
            {
                float at = nearest.Distance - 14;
                Place(at, toward, track.Surface(at, toward * (track.HalfWidth + .25f)));
            }

            bool TryPlace(float at, int side)
            {
                Vector3 edge = track.Surface(at, side * track.HalfWidth);
                Vector3 ground = track.FreeGround(track.Surface(at, side * (track.HalfWidth + 2.1f)));
                if (Math.Abs(edge.Y - ground.Y) > 1.6f || !track.CanStageSpectator(ground)) return false;
                Place(at, side, ground);
                return true;
            }

            void Place(float at, int side, Vector3 origin)
            {
                var sign = new Node3D
                {
                    Name = landmark.Name,
                    Position = origin,
                    Rotation = new(0, BogCircuit.Yaw(track.Forward(at)) - .12f * side, 0)
                };
                signs.AddChild(sign);
                float width = Mathf.Clamp(landmark.Name.Length * .22f + .65f, 3.3f, 5.9f);
                BogCircuit.Box(sign, new(0, 1.35f, -.04f), new(.16f, 2.7f, .15f), post);
                BogCircuit.Box(sign, new(0, 2.37f, .06f), new(width, .72f, .14f), plaque);
                sign.AddChild(new Label3D
                {
                    Text = landmark.Name, Position = new(0, 2.37f, .143f),
                    FontSize = 54, PixelSize = .0068f, OutlineSize = 0,
                    Modulate = new Color("fff0bb"), Shaded = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
                });
            }
        }
    }
}
