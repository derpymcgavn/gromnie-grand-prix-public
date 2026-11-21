using Godot;
using DerethSurvivors;

internal sealed record RidgeCircuit(int Id, string Name, uint Center, float HalfWidth, float[][] Waypoints,
    string FlightName, float[] Launch, float[] Landing, float FlightHeight)
{
    public BogCircuit Build(AcLandArena land) => new(Name, Waypoints.Select(Point).ToArray(), land, HalfWidth, gradedRoad: true);

    public (float Start, float End) FlightSector(BogCircuit track)
    {
        float Distance(float[] row)
        {
            Vector2 point = Point(row);
            return Mathf.PosMod(track.Project(new(point.X, 0, point.Y), track.Length * .5f).Distance, track.Length);
        }
        return (Distance(Launch), Distance(Landing));
    }

    private static Vector2 Point(float[] value) => new(value[0], value[1]);

    public static void ValidateRoad(BogCircuit track, AcLandArena land)
    {
        if (land.Blocks.Count != 64 || track.Length < 3000 || track.TightestInsideRadius() < 13)
            throw new InvalidDataException($"Invalid Black Hills course size or turn radius for {track.Name}.");
        for (float distance = 0; distance < track.Length; distance += 3)
        {
            var point = track.Point(distance);
            if (!TownRoutePlanner.Clear(land, point, track.HalfWidth + 1.35f) ||
                Math.Abs(point.Y - track.Point(distance + 3).Y) > 2.8f)
                throw new InvalidDataException($"The Black Hills road is blocked at {distance:F0} m ({point.X:F0}, {point.Z:F0}) in {track.Name}.");
        }
    }
}
