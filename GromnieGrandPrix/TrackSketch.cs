using Godot;
using DerethSurvivors;

internal partial class TrackSketch : Control
{
    private static readonly Color Paper = new("101b1c"), MinorGrid = new("e3e9b625"),
        BlockGrid = new("e3e9b66c"), Gold = new("ffcf76"), Mint = new("7ce7c0"),
        SkiBlue = new("59e0ed"), FlightLilac = new("d3acff");
    private AcLandArena? land;
    private Texture2D? terrain;
    private Vector2 pan;
    private float zoom = 1;
    private bool panning;
    private (Vector2 Point, bool Building)[] scenery = [];

    internal Vector2[] Guides = [], Road = [], Ghost = [];
    internal (TrackTravelMode Mode, Vector2[] Line)[] Sections = [];
    internal TrackTravelMode[] GuideModes = [];
    internal TrackTravelMode PreviewMode;
    internal Vector2? Cursor, Start;
    internal Vector2 StartRight;
    internal int Selected = -1, Snap = 24;
    internal float RoadWidth = 24;
    internal bool GhostFits;
    internal event Action<Vector2, InputEventMouseButton>? Pressed;
    internal event Action<Vector2>? Moved;
    internal event Action? Released;
    internal event Action? Exited;

    internal static Color ModeColor(TrackTravelMode mode) => mode switch
    {
        TrackTravelMode.JetSki => SkiBlue,
        TrackTravelMode.Flight => FlightLilac,
        _ => Gold
    };

    private float ScaleFactor => land is null ? 1 : (Math.Min(Size.X, Size.Y) - 40) / land.WorldBounds.Size.X * zoom;
    private Vector2 Origin => Size * .5f + pan - (land?.WorldBounds.GetCenter() ?? Vector2.Zero) * ScaleFactor;
    internal Vector2 ToScreen(Vector2 position) => Origin + position * ScaleFactor;
    private Vector2 ToWorld(Vector2 position) => (position - Origin) / ScaleFactor;

    internal void SetTerrain(AcLandArena arena, DatArchive cells)
    {
        var markers = new List<(Vector2, bool)>();
        foreach (var block in arena.Blocks.Values)
        {
            uint info = block.Id - 1;
            if (!cells.Entries.ContainsKey(info)) continue;
            Vector2 origin = new(((int)(block.Id >> 24) - (int)(arena.Block.Id >> 24)) * AcLandArena.BlockSize - arena.CenterX,
                ((int)((arena.Block.Id >> 16) & 255) - (int)((block.Id >> 16) & 255)) * AcLandArena.BlockSize + arena.CenterY);
            foreach (var item in AcWorldData.ReadPlacements(cells.Read(info)))
                markers.Add((origin + new Vector2(item.Pose.Position.X, -item.Pose.Position.Y), item.IsBuilding));
        }
        var replacement = MakeTerrain(arena);
        terrain?.Dispose();
        terrain = replacement;
        land = arena;
        scenery = markers.ToArray();
        Fit();
    }

    internal void Fit()
    {
        zoom = 1;
        pan = Vector2.Zero;
        QueueRedraw();
    }

    internal void Zoom(float factor) => ZoomAt(factor, Size * .5f);

    private void ZoomAt(float factor, Vector2 cursor)
    {
        Vector2 at = ToWorld(cursor);
        zoom = Mathf.Clamp(zoom * factor, .55f, 16);
        pan += cursor - ToScreen(at);
        QueueRedraw();
        Moved?.Invoke(ToWorld(cursor));
    }

    public override void _Ready() => MouseExited += () => Exited?.Invoke();

    public override void _ExitTree()
    {
        terrain?.Dispose();
        terrain = null;
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Middle) panning = button.Pressed;
            else if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                ZoomAt(button.ButtonIndex == MouseButton.WheelUp ? 1.23f : 1 / 1.23f, button.Position);
            else if (button.Pressed && button.ButtonIndex is MouseButton.Left or MouseButton.Right)
                Pressed?.Invoke(ToWorld(button.Position), button);
            else if (!button.Pressed && button.ButtonIndex == MouseButton.Left)
                Released?.Invoke();
            AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion)
        {
            if (panning) { pan += motion.Relative; QueueRedraw(); }
            else Moved?.Invoke(ToWorld(motion.Position));
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        DrawRect(new(Vector2.Zero, Size), Paper);
        if (land is null || terrain is null) return;
        Rect2 bounds = land.WorldBounds;
        Rect2 area = new(ToScreen(bounds.Position), bounds.Size * ScaleFactor);
        DrawTextureRect(terrain, area, false);
        DrawGrid(bounds, AcLandArena.BlockSize, BlockGrid, 1.1f);
        if (Snap > 0 && Snap * ScaleFactor >= 8)
            DrawGrid(bounds, Snap, MinorGrid, 1);

        float blockPixels = AcLandArena.BlockSize * ScaleFactor;
        if (blockPixels >= 46)
        {
            var font = GetThemeDefaultFont();
            for (int y = 0; y < land.GridSide; y++)
            for (int x = 0; x < land.GridSide; x++)
            {
                Vector2 world = bounds.Position + new Vector2(x, y) * AcLandArena.BlockSize;
                Vector2 at = ToScreen(world) + new Vector2(6, 15);
                if (!new Rect2(-40, -20, Size.X + 80, Size.Y + 40).HasPoint(at)) continue;
                uint id = land.Resolve(world.X + 2, world.Y + 2).Block.Id;
                DrawString(font, at, $"{id >> 16:X4}", fontSize: 11, modulate: new Color("f5f3d5bd"));
            }
        }

        foreach (var mark in scenery)
        {
            if (!mark.Building && blockPixels < 78) continue;
            Vector2 at = ToScreen(mark.Point);
            if (!new Rect2(Vector2.Zero, Size).Grow(6).HasPoint(at)) continue;
            if (mark.Building) DrawRect(new(at - Vector2.One * 2.5f, Vector2.One * 5), new Color("eab27a"));
            else DrawCircle(at, 1.6f, new Color("dca88395"));
        }

        if (Road.Length > 1)
        {
            var pixels = Road.Select(ToScreen).ToArray();
            float width = Math.Max(3, RoadWidth * ScaleFactor);
            DrawPolyline(pixels, new Color("171c1ee7"), width + 3, true);
            if (Sections.Length == 0) DrawSection(pixels, TrackTravelMode.Ground, width);
            else foreach (var section in Sections)
                DrawSection(section.Line.Select(ToScreen).ToArray(), section.Mode, width);
        }
        for (int i = 1; i < Guides.Length; i++)
            DrawDashedLine(ToScreen(Guides[i - 1]), ToScreen(Guides[i]), new Color("fff1bc53"), 1, 6);
        for (int i = 0; i < Guides.Length; i++)
        {
            Vector2 at = ToScreen(Guides[i]);
            float radius = i == Selected ? 6.4f : 4.8f;
            DrawCircle(at, radius + 1.4f, Paper, antialiased: true);
            Color mode = i < GuideModes.Length ? ModeColor(GuideModes[i]) : Gold;
            DrawCircle(at, radius, i == Selected ? Colors.White : i == 0 ? Mint : mode, antialiased: true);
        }
        if (Start is { } start)
        {
            Vector2 left = ToScreen(start - StartRight * RoadWidth * .5f);
            Vector2 right = ToScreen(start + StartRight * RoadWidth * .5f);
            DrawLine(left, right, Paper, 6, true);
            DrawDashedLine(left, right, Mint, 3.4f, 4);
        }

        Color ghost = GhostFits ? PreviewMode == TrackTravelMode.Ground ? Mint : ModeColor(PreviewMode) : new Color("ff8b7a");
        for (int i = 1; i < Ghost.Length; i++)
            DrawDashedLine(ToScreen(Ghost[i - 1]), ToScreen(Ghost[i]), ghost, 2, 7);
        foreach (var point in Ghost) DrawCircle(ToScreen(point), 3.7f, ghost, antialiased: true);
        if (Cursor is { } cursor && Ghost.Length == 0)
            DrawArc(ToScreen(cursor), 6.3f, 0, Mathf.Tau, 16, ghost, 1.5f, true);
        DrawRect(new(Vector2.Zero, Size), new Color("577067"), false, 1);
    }

    private void DrawSection(Vector2[] pixels, TrackTravelMode mode, float width)
    {
        Color lane = ModeColor(mode);
        Color fill = mode == TrackTravelMode.Ground ? new Color("c18a4eeb") : lane.Darkened(.28f);
        DrawPolyline(pixels, fill, width, true);
        DrawPolyline(pixels, lane, 1.4f, true);
    }

    private void DrawGrid(Rect2 bounds, float step, Color color, float width)
    {
        for (float offset = 0; offset <= bounds.Size.X + .01f; offset += step)
        {
            var across = new Vector2(bounds.Position.X + offset, bounds.Position.Y);
            var down = new Vector2(bounds.Position.X, bounds.Position.Y + offset);
            DrawLine(ToScreen(across), ToScreen(new(across.X, bounds.End.Y)), color, width);
            DrawLine(ToScreen(down), ToScreen(new(bounds.End.X, down.Y)), color, width);
        }
    }

    private static Texture2D MakeTerrain(AcLandArena arena)
    {
        int size = Mathf.Clamp(arena.GridSide * 85, 192, 560);
        var bytes = new byte[size * size * 3];
        var heights = new float[size * size];
        var types = new byte[size * size];
        var roads = new bool[size * size];
        Rect2 bounds = arena.WorldBounds;
        float step = bounds.Size.X / size;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int index = y * size + x;
            Vector2 point = bounds.Position + new Vector2(x + .5f, y + .5f) * step;
            var p = arena.Resolve(point.X, point.Y);
            types[index] = (byte)AcWorldData.TerrainAt(p.Block, p.X, p.Y);
            heights[index] = AcWorldData.SampleHeight(p.Block, arena.Region, p.X, p.Y);
            roads[index] = AcTerrainBlend.OnRoad(p.Block, p.X, p.Y);
        }
        Color moss = new("59654a"), marsh = new("49695c"), stone = new("706779"), desert = new("917650"),
            snow = new("9baeb1"), water = new("254751"), dirt = new("ac9573");
        Color[] palette = arena.Region.Terrains.Select((type, index) =>
        {
            string name = type.Name.ToLowerInvariant();
            if (index is >= 16 and <= 20 or 22 or 23) return water;
            if (name.Contains("snow") || name.Contains("ice")) return snow;
            if (name.Contains("sand") || name.Contains("desert")) return desert;
            if (name.Contains("rock") || name.Contains("volcan") || name.Contains("lava") || name.Contains("obsidian")) return stone;
            return name.Contains("marsh") || name.Contains("swamp") ? marsh : moss;
        }).ToArray();
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int index = y * size + x;
            Color color = palette[types[index]];
            float slope = (heights[y * size + Math.Max(0, x - 1)] + heights[Math.Max(0, y - 1) * size + x] - 2 * heights[index]) / step;
            float shade = color == water ? .94f : Mathf.Clamp(.97f + slope * .38f + heights[index] * .00052f, .58f, 1.37f);
            if (roads[index]) color = color.Lerp(dirt, .61f);
            bytes[index * 3] = Channel(color.R);
            bytes[index * 3 + 1] = Channel(color.G);
            bytes[index * 3 + 2] = Channel(color.B);
            byte Channel(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * shade * 255), 0, 255);
        }
        using var image = Image.CreateFromData(size, size, false, Image.Format.Rgb8, bytes);
        return ImageTexture.CreateFromImage(image);
    }
}
