using Godot;
using DerethSurvivors;

internal sealed record SpanSprint(int Id, uint Center, string Name, float HalfWidth, uint[] Blocks,
    float[][] Waypoints, float[] Span, float[][] Deck, float WaterLevel)
{
    private const float StoneLength = 168, SecondSpan = 192;
    internal float West => Span[0];
    internal float East => West + SecondSpan + StoneLength;
    internal float RiverAxis => Span[1];

    internal BogCircuit Build(AcLandArena land) =>
        new(Name, Waypoints.Select(p => new Vector2(p[0], p[1])).ToArray(), land, HalfWidth, gradedRoad: true, sprint: this);

    internal bool OnSpan(Vector3 point) =>
        point.X >= West && point.X <= East && Math.Abs(point.Z - RiverAxis) < 12;

    internal float WidthAt(Vector3 point)
    {
        if (Math.Abs(point.Z - RiverAxis) > 45) return HalfWidth;
        float outside = Math.Max(West - point.X, point.X - East);
        return Mathf.Lerp(2.95f, HalfWidth, Mathf.SmoothStep(0, 58, Math.Max(0, outside)));
    }

    internal float? DeckHeight(Vector3 point, AcLandArena land)
    {
        if (!OnSpan(point)) return null;
        float span = point.X - West;
        if (span > StoneLength && span < SecondSpan) return null;
        if (span >= SecondSpan) span -= SecondSpan;
        float crownDistance = Math.Min(span, StoneLength - span);
        int sample = Math.Clamp((int)(crownDistance / 4), 0, Deck.Length - 2);
        float t = (crownDistance - Deck[sample][0]) / (Deck[sample + 1][0] - Deck[sample][0]);
        return Mathf.Lerp(Deck[sample][1], Deck[sample + 1][1], t) - land.ElevationOrigin;
    }
}
