using Godot;
using DerethSurvivors;

public sealed partial class BogCircuit
{
    internal bool NativeSnow(Vector3 point) => land is not null && land.ContainsGround(point.X, point.Z) &&
        land.TerrainName(point.X, point.Z).Contains("Snow", StringComparison.OrdinalIgnoreCase);
}

public partial class GrandPrix
{
    private const float SnowCell = .5f, SnowDepth = .17f;
    private const int SnowTileSide = 16, SnowPatchSide = 224;
    private readonly Dictionary<Vector2I, byte[]> snowTiles = new();
    private readonly Vector2?[] lastSnowFooting = new Vector2?[RacerCount];
    private readonly byte[] snowPatch = new byte[SnowPatchSide * SnowPatchSide];
    private ImageTexture? snowCompression;
    private float snowRefresh;

    private void ResetSnowLayer()
    {
        snowTiles.Clear();
        Array.Clear(lastSnowFooting);
        snowRefresh = 0;
        if (townRegion is not null && track.NativeBlockCount > 0)
            AcTerrainRenderer.For(assets, townRegion).SetSnowPowder(SnowDepth, courseOverride: track.IsSnowboardCourse);
    }

    private float UnpackedSnow(Vector3 point)
    {
        int x = Mathf.FloorToInt(point.X / SnowCell), z = Mathf.FloorToInt(point.Z / SnowCell);
        var key = new Vector2I(x >> 4, z >> 4);
        return snowTiles.TryGetValue(key, out var tile)
            ? 1 - tile[(z & 15) * SnowTileSide + (x & 15)] / 255f : 1;
    }

    private void TickSnowLayer(float dt)
    {
        if (townRegion is null || track.NativeBlockCount == 0) return;
        for (int i = 0; i < Math.Min(racers.Count, lastSnowFooting.Length); i++)
        {
            var racer = racers[i];
            Vector3 point = racer.CoursePosition(track);
            if (racer.Speed < 2 || racer.UsesJetSki || racer.Flight is not null || racer.Falling ||
                racer.SnowboardAirborne || racer.LeapHeight > .14f ||
                racer.EjectTime > 0 || racer.ResetHold > 0 || !(track.IsSnowboardCourse || track.NativeSnow(point)))
            {
                lastSnowFooting[i] = null;
                continue;
            }
            Vector2 position = new(point.X, point.Z);
            Vector2 from = lastSnowFooting[i] ?? position;
            float distance = position.DistanceTo(from);
            if (distance < .45f && lastSnowFooting[i].HasValue) continue;
            lastSnowFooting[i] = position;
            if (distance > 8) { from = position; distance = 0; }
            float width = racer.UsesSnowboard ? racer.GroundDrifting ? .86f : .54f : .67f;
            int samples = Math.Max(1, Mathf.CeilToInt(distance / .41f));
            for (int sample = 1; sample <= samples; sample++)
                PackSnow(from.Lerp(position, (float)sample / samples), width, racer.GroundDrifting ? 126 : 97);
        }
        snowRefresh = Math.Max(0, snowRefresh - dt);
        if (snowRefresh > 0 || snowTiles.Count == 0 || DisplayServer.GetName() == "headless") return;
        snowRefresh = .067f;
        Vector3 center = Player.CoursePosition(track);
        int left = Mathf.FloorToInt(center.X / SnowCell) - SnowPatchSide / 2;
        int top = Mathf.FloorToInt(center.Z / SnowCell) - SnowPatchSide / 2;
        Array.Clear(snowPatch);
        for (int z = top >> 4; z <= (top + SnowPatchSide - 1) >> 4; z++)
        for (int x = left >> 4; x <= (left + SnowPatchSide - 1) >> 4; x++)
        {
            if (!snowTiles.TryGetValue(new Vector2I(x, z), out var tile)) continue;
            int ox = x * SnowTileSide, oz = z * SnowTileSide;
            int first = Math.Max(left, ox), last = Math.Min(left + SnowPatchSide, ox + SnowTileSide);
            for (int row = Math.Max(top, oz); row < Math.Min(top + SnowPatchSide, oz + SnowTileSide); row++)
                Array.Copy(tile, (row - oz) * SnowTileSide + first - ox, snowPatch,
                    (row - top) * SnowPatchSide + first - left, last - first);
        }
        using var image = Image.CreateFromData(SnowPatchSide, SnowPatchSide, false, Image.Format.R8, snowPatch);
        if (snowCompression is null) snowCompression = ImageTexture.CreateFromImage(image);
        else snowCompression.Update(image);
        Rect2 bounds = new(new Vector2(left, top) * SnowCell, Vector2.One * (SnowPatchSide * SnowCell));
        AcTerrainRenderer.For(assets, townRegion).SetSnowPowder(SnowDepth, snowCompression, bounds, courseOverride: track.IsSnowboardCourse);
    }

    private void PackSnow(Vector2 center, float radius, int load)
    {
        for (int z = Mathf.FloorToInt((center.Y - radius) / SnowCell); z <= Mathf.FloorToInt((center.Y + radius) / SnowCell); z++)
        for (int x = Mathf.FloorToInt((center.X - radius) / SnowCell); x <= Mathf.FloorToInt((center.X + radius) / SnowCell); x++)
        {
            Vector2 at = (new Vector2(x, z) + Vector2.One * .5f) * SnowCell;
            float pressure = 1 - Mathf.SmoothStep(.15f * radius, radius, center.DistanceTo(at));
            if (pressure < .08f) continue;
            Vector2I key = new(x >> 4, z >> 4);
            if (!snowTiles.TryGetValue(key, out var tile))
            {
                if (snowTiles.Count >= 3072) continue;
                snowTiles[key] = tile = new byte[SnowTileSide * SnowTileSide];
            }
            int index = (z & 15) * SnowTileSide + (x & 15);
            tile[index] = (byte)Math.Min(255, tile[index] + Mathf.RoundToInt(load * pressure));
        }
    }
}
