using Godot;

internal sealed class RegattaFluidField
{
    internal const float CellSize = .8f;
    internal const float MaximumHeight = .38f;
    private const int TileShift = 4, TileSide = 1 << TileShift, Stride = TileSide + 2;
    private const int ActiveTileLimit = 512, SpareTileLimit = 96;
    private const float FixedStep = 1f / 30, WaveSpeed = 6.4f;
    private const float Courant = WaveSpeed * FixedStep / CellSize;
    private const float Damping = .89f;
    private const float BoundaryThreshold = .0025f, SleepThreshold = .0008f;
    private const int SleepSteps = 12;
    private static readonly float VelocityRetention = MathF.Exp(-Damping * FixedStep);

    private sealed class Tile
    {
        internal Vector2I Key;
        internal float[] Height = new float[Stride * Stride];
        internal float[] Previous = new float[Stride * Stride];
        internal int QuietSteps;

        internal void Reset(Vector2I key)
        {
            Key = key;
            QuietSteps = 0;
            Array.Clear(Height);
            Array.Clear(Previous);
        }
    }

    private readonly Dictionary<Vector2I, Tile> tileIndex = new();
    private readonly List<Tile> tiles = new();
    private readonly Stack<Tile> spareTiles = new();
    private double pendingTime;

    internal void Clear()
    {
        foreach (Tile tile in tiles) Recycle(tile);
        tiles.Clear();
        tileIndex.Clear();
        pendingTime = 0;
    }

    // A fixed clock keeps propagation and damping independent of rendering.
    internal void Step(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0) return;
        pendingTime = Math.Min(pendingTime + dt, 4 * FixedStep);
        while (pendingTime + 1e-8 >= FixedStep)
        {
            pendingTime = Math.Max(0, pendingTime - FixedStep);
            Advance();
        }
    }

    // Displace the current and previous surface equally, so the source does not
    // inject numerical velocity. A shallow ring balances the raised center.
    internal void Disturb(Vector2 position, float radius, float strength)
    {
        if (!position.IsFinite() || !float.IsFinite(radius) || !float.IsFinite(strength) || Math.Abs(strength) < .0001f)
            return;
        radius = Math.Clamp(radius, CellSize * 1.55f, 16);
        Vector2 grid = position / CellSize;
        float gridRadius = radius / CellSize;
        float inverseSquare = 1 / (gridRadius * gridRadius);
        int left = Mathf.CeilToInt(grid.X - gridRadius), right = Mathf.FloorToInt(grid.X + gridRadius);
        int top = Mathf.CeilToInt(grid.Y - gridRadius), bottom = Mathf.FloorToInt(grid.Y + gridRadius);

        for (int z = top; z <= bottom; z++)
        for (int x = left; x <= right; x++)
        {
            float dx = x - grid.X, dz = z - grid.Y;
            float radial = (dx * dx + dz * dz) * inverseSquare;
            if (radial >= 1) continue;
            float taper = 1 - radial;
            float displacement = strength * taper * taper * (1 - 4 * radial);
            if (Math.Abs(displacement) < .0001f) continue;
            Vector2I key = TileKey(x, z);
            Tile? tile = GetOrCreate(key);
            if (tile is null) continue;
            int index = LocalIndex(x, z, key);
            float displaced = Math.Clamp(tile.Height[index] + displacement, -MaximumHeight, MaximumHeight);
            float applied = displaced - tile.Height[index];
            tile.Height[index] = displaced;
            tile.Previous[index] = Math.Clamp(tile.Previous[index] + applied, -MaximumHeight, MaximumHeight);
            tile.QuietSteps = 0;
        }
    }

    internal float HeightOffset(Vector2 position)
    {
        if (tiles.Count == 0) return 0;
        Vector2 grid = position / CellSize;
        int x = Mathf.FloorToInt(grid.X), z = Mathf.FloorToInt(grid.Y);
        float tx = grid.X - x, tz = grid.Y - z;
        Vector2I key = TileKey(x, z);
        if ((x & (TileSide - 1)) != TileSide - 1 && (z & (TileSide - 1)) != TileSide - 1)
        {
            if (!tileIndex.TryGetValue(key, out Tile? tile)) return 0;
            int i = LocalIndex(x, z, key);
            var h = tile.Height;
            return Mathf.Lerp(Mathf.Lerp(h[i], h[i + 1], tx), Mathf.Lerp(h[i + Stride], h[i + Stride + 1], tx), tz);
        }

        return Mathf.Lerp(Mathf.Lerp(SampleNode(x, z), SampleNode(x + 1, z), tx),
            Mathf.Lerp(SampleNode(x, z + 1), SampleNode(x + 1, z + 1), tx), tz);
    }

    // The first destination texel is the sample at (gridX, gridZ) * CellSize.
    // X increases across rows, Z increases between rows.
    internal void CopyHeightPatch(int gridX, int gridZ, int width, int height, float[] destination)
    {
        Array.Clear(destination, 0, width * height);
        foreach (Tile tile in tiles)
        {
            int x = tile.Key.X * TileSide, z = tile.Key.Y * TileSide;
            int fromX = Math.Max(x, gridX), toX = Math.Min(x + TileSide, gridX + width);
            int fromZ = Math.Max(z, gridZ), toZ = Math.Min(z + TileSide, gridZ + height);
            if (fromX >= toX || fromZ >= toZ) continue;
            int source = LocalIndex(fromX, fromZ, tile.Key);
            int target = (fromZ - gridZ) * width + fromX - gridX;
            for (int row = fromZ; row < toZ; row++)
            {
                Array.Copy(tile.Height, source, destination, target, toX - fromX);
                source += Stride;
                target += width;
            }
        }
    }

    private void Advance()
    {
        int existing = tiles.Count;
        for (int i = 0; i < existing; i++)
            ExpandAtBoundary(tiles[i]);

        // Copy all current edge values before advancing any tile. This removes
        // seams at tile boundaries without a dictionary lookup in the stencil.
        foreach (Tile tile in tiles) FillGhostCells(tile);
        for (int i = tiles.Count - 1; i >= 0; i--)
        {
            Tile tile = tiles[i];
            Integrate(tile);
            if (tile.QuietSteps < SleepSteps) continue;
            tileIndex.Remove(tile.Key);
            tiles[i] = tiles[^1];
            tiles.RemoveAt(tiles.Count - 1);
            Recycle(tile);
        }
    }

    private static void Integrate(Tile tile)
    {
        var current = tile.Height;
        var previous = tile.Previous;
        bool energetic = false;
        for (int z = 1; z <= TileSide; z++)
        {
            int end = z * Stride + TileSide;
            for (int i = z * Stride + 1; i <= end; i++)
            {
                float center = current[i];
                float laplacian = current[i - 1] + current[i + 1] + current[i - Stride] + current[i + Stride] - 4 * center;
                float next = center + (center - previous[i]) * VelocityRetention + laplacian * (Courant * Courant);
                previous[i] = Math.Clamp(next, -MaximumHeight, MaximumHeight);
                energetic |= Math.Abs(next) > SleepThreshold || Math.Abs(center) > SleepThreshold;
            }
        }

        tile.QuietSteps = energetic ? 0 : tile.QuietSteps + 1;
        (tile.Height, tile.Previous) = (previous, current);
    }

    private void ExpandAtBoundary(Tile tile)
    {
        Vector2I key = tile.Key;
        TryExpand(new(key.X - 1, key.Y), Stride + 1, Stride);
        TryExpand(new(key.X + 1, key.Y), Stride + TileSide, Stride);
        TryExpand(new(key.X, key.Y - 1), Stride + 1, 1);
        TryExpand(new(key.X, key.Y + 1), TileSide * Stride + 1, 1);

        void TryExpand(Vector2I neighbor, int index, int spacing)
        {
            if (tileIndex.ContainsKey(neighbor)) return;
            for (int c = 0; c < TileSide; c++, index += spacing)
            {
                if (Math.Abs(tile.Height[index]) <= BoundaryThreshold && Math.Abs(tile.Previous[index]) <= BoundaryThreshold) continue;
                GetOrCreate(neighbor);
                return;
            }
        }
    }

    private void FillGhostCells(Tile tile)
    {
        Vector2I key = tile.Key;
        float[] height = tile.Height;
        var west = FindHeight(new(key.X - 1, key.Y));
        var east = FindHeight(new(key.X + 1, key.Y));
        var north = FindHeight(new(key.X, key.Y - 1));
        var south = FindHeight(new(key.X, key.Y + 1));

        const int NorthGhost = 1, SouthGhost = (TileSide + 1) * Stride + 1;
        if (north is not null) Array.Copy(north, TileSide * Stride + 1, height, NorthGhost, TileSide);
        else Array.Clear(height, NorthGhost, TileSide);
        if (south is not null) Array.Copy(south, Stride + 1, height, SouthGhost, TileSide);
        else Array.Clear(height, SouthGhost, TileSide);
        for (int i = Stride; i <= TileSide * Stride; i += Stride)
        {
            height[i] = west is null ? 0 : west[i + TileSide];
            height[i + TileSide + 1] = east is null ? 0 : east[i + 1];
        }
    }

    private float[]? FindHeight(Vector2I key) =>
        tileIndex.TryGetValue(key, out Tile? tile) ? tile.Height : null;

    private float SampleNode(int x, int z)
    {
        Vector2I key = TileKey(x, z);
        return tileIndex.TryGetValue(key, out Tile? tile) ? tile.Height[LocalIndex(x, z, key)] : 0;
    }

    private Tile? GetOrCreate(Vector2I key)
    {
        if (tileIndex.TryGetValue(key, out Tile? tile)) return tile;
        if (tiles.Count >= ActiveTileLimit) return null;
        tile = spareTiles.Count == 0 ? new Tile() : spareTiles.Pop();
        tile.Reset(key);
        tileIndex.Add(key, tile);
        tiles.Add(tile);
        return tile;
    }

    private void Recycle(Tile tile)
    {
        if (spareTiles.Count < SpareTileLimit) spareTiles.Push(tile);
    }

    // Signed shift is floor division for the power-of-two tile size.
    private static Vector2I TileKey(int x, int z) => new(x >> TileShift, z >> TileShift);

    private static int LocalIndex(int x, int z, Vector2I key) =>
        (z - key.Y * TileSide + 1) * Stride + x - key.X * TileSide + 1;
}
