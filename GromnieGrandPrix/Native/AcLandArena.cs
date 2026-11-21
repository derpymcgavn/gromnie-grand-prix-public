using Godot;

namespace DerethSurvivors;

public sealed partial class AcLandArena
{
    public AcWorldData.Block Block { get; }
    public AcWorldData.Region Region { get; }
    public const float BlockSize = 192;
    public const float HalfSize = BlockSize * 1.5f;
    public int GridSide { get; }
    public Rect2 WorldBounds { get; }
    private readonly int minEast, maxEast, minNorth, maxNorth;
    public float CenterX => BlockSize / 2;
    public float CenterY => BlockSize / 2;
    internal float ElevationOrigin => baseHeight;
    public IReadOnlyDictionary<uint,AcWorldData.Block> Blocks { get; }
    private readonly float baseHeight;
    private readonly Dictionary<uint, AcWorldData.Block> edgeBlocks = [];
    public sealed record SceneryCollision(uint Model,Vector3 Position,string Kind,float NativeRadius,float VisualRadius,Transform3D Transform,uint Landblock,bool IsBuilding = false);
    public readonly List<SceneryCollision> CollisionObjects=new();
    private SceneMovement.BodySphere[] playerBody=Array.Empty<SceneMovement.BodySphere>();
    private float stepHeight=.3f;
    private readonly Func<System.Numerics.Vector3,System.Numerics.Vector3> movementGround;
    private readonly Func<System.Numerics.Vector3,bool> movementValid;
    private AcTerrainRenderer? terrainRenderer;
    private readonly Dictionary<(int X,int Y),Vector3> terrainNormals=new();
    public int PlacedObjects { get; private set; }
    public int FailedObjects { get; private set; }
    public ProjectileWorld Physics { get; } = new();
    public string Name => $"{Region.Terrains[AcWorldData.TerrainAt(Block, CenterX, CenterY)].Name} · {Block.Id >> 16:X4}";
    public AcLandArena(AcWorldData.Block block, AcWorldData.Region region, IEnumerable<AcWorldData.Block> blocks, int gridSide = 3)
    {
        Block = block;
        Region = region;
        GridSide = gridSide;
        Blocks = blocks.ToDictionary(b => b.Id);
        if (gridSide != 0)
        {
            var expected = NeighborIds(block.Id, gridSide);
            if (Blocks.Count != expected.Length || expected.Any(id => !Blocks.ContainsKey(id)))
                throw new InvalidDataException($"The selected center needs all {expected.Length} original landblocks.");
        }
        minEast = Blocks.Keys.Min(id => East(id) - East(block.Id));
        maxEast = Blocks.Keys.Max(id => East(id) - East(block.Id));
        minNorth = Blocks.Keys.Min(id => North(id) - North(block.Id));
        maxNorth = Blocks.Keys.Max(id => North(id) - North(block.Id));
        WorldBounds = new Rect2(minEast * BlockSize - CenterX, -maxNorth * BlockSize - CenterY,
            (maxEast - minEast + 1) * BlockSize, (maxNorth - minNorth + 1) * BlockSize);
        baseHeight = AcWorldData.SampleHeight(block, region, CenterX, CenterY);
        movementGround = p => Numeric(Ground(p.X, p.Z));
        movementValid = p => ValidGround(new(p.X, p.Y, p.Z));
    }

    internal static AcLandArena LoadCorridor(uint center, uint[] ids, DatArchive cells, AcWorldData.Region region)
    {
        var blocks = ids.Select(id => AcWorldData.ReadBlock(cells.Read(id))).ToArray();
        return new AcLandArena(blocks.Single(b => b.Id == center), region, blocks, 0);
    }

    private static int East(uint id) => (int)(id >> 24);
    private static int North(uint id) => (int)((id >> 16) & 255);
    private uint CellAt(int east, int north) =>
        ((uint)(East(Block.Id) + east) << 24) | ((uint)(North(Block.Id) + north) << 16) | 0xffff;

    internal bool ContainsGround(float x, float z) =>
        WorldBounds.HasPoint(new(x, z)) && (GridSide != 0 || Blocks.ContainsKey(CellAt(
            (int)MathF.Floor((x + CenterX) / BlockSize), (int)MathF.Floor((CenterY - z) / BlockSize))));

    public static uint[] NeighborIds(uint center, int gridSide = 3)
    {
        if (gridSide is not (1 or 3 or 7 or 8 or 14)) throw new ArgumentOutOfRangeException(nameof(gridSide));
        int blockX = (int)(center >> 24), blockY = (int)((center >> 16) & 255);
        int min = -(gridSide / 2), max = min + gridSide - 1;
        if (blockX + min < 0 || blockY + min < 0 || blockX + max > 255 || blockY + max > 255)
            throw new InvalidDataException("The landblock grid extends beyond the world.");
        return (from dx in Enumerable.Range(min, gridSide) from dy in Enumerable.Range(min, gridSide)
            select ((uint)(blockX + dx) << 24) | ((uint)(blockY + dy) << 16) | 0xffffu).ToArray();
    }

    public static AcLandArena Load(uint id, DatArchive cells, AcWorldData.Region region, int gridSide = 3)
    {
        id = (id & 0xffff0000) | 0xffffu;
        var ids = NeighborIds(id, gridSide);
        var missing = ids.Where(n => !cells.Entries.ContainsKey(n)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Incomplete {gridSide}×{gridSide} grid. Missing " +
                string.Join(", ", missing.Select(n => (n >> 16).ToString("X4"))));
        var blocks = ids.Select(n => AcWorldData.ReadBlock(cells.Read(n))).ToArray();
        return new(blocks.Single(b => b.Id == id), region, blocks, gridSide);
    }

    public (AcWorldData.Block Block, float X, float Y) Resolve(float x, float z)
    {
        float nx = Mathf.Clamp(x, WorldBounds.Position.X, WorldBounds.End.X) + CenterX;
        float ny = CenterY - Mathf.Clamp(z, WorldBounds.Position.Y, WorldBounds.End.Y);
        int dx = Math.Clamp((int)MathF.Floor(nx / BlockSize), minEast, maxEast);
        int dy = Math.Clamp((int)MathF.Floor(ny / BlockSize), minNorth, maxNorth);
        uint id = CellAt(dx, dy);
        if (!Blocks.TryGetValue(id, out var block))
        {
            if (!edgeBlocks.TryGetValue(id, out block))
                edgeBlocks[id] = block = Blocks.Values.MinBy(row =>
                    Math.Abs(East(row.Id) - East(id)) + Math.Abs(North(row.Id) - North(id)))!;
            dx = East(block.Id) - East(Block.Id);
            dy = North(block.Id) - North(Block.Id);
        }
        return (block, Math.Clamp(nx - dx * BlockSize, 0, BlockSize), Math.Clamp(ny - dy * BlockSize, 0, BlockSize));
    }
    public Vector3 Ground(float x, float z)
    {
        var location=Resolve(x,z);return new(x,AcWorldData.SampleHeight(location.Block,Region,location.X,location.Y)-baseHeight,z);
    }
    public string TerrainName(float x,float z){var location=Resolve(x,z);return Region.Terrains[AcWorldData.TerrainAt(location.Block,location.X,location.Y)].Name;}
    private bool ValidGround(Vector3 point)=>ContainsGround(point.X,point.Z)&&
        !TerrainName(point.X,point.Z).Contains("Water",StringComparison.OrdinalIgnoreCase);
    public bool Walkable(Vector3 point, float radius = 0.5f)
    {
        if(!ValidGround(point))return false;
        float extra=Math.Max(0,radius-.5f);
        var body=extra>0?playerBody.Select(s=>s with{Radius=s.Radius+extra}).ToArray():playerBody;
        return SceneMovement.Clear(Physics,Numeric(point),body,stepHeight);
    }
    private static System.Numerics.Vector3 Numeric(Vector3 p)=>new(p.X,p.Y,p.Z);
    private static float EdgeInset(IReadOnlyList<SceneMovement.BodySphere> body,float minimum)
    {
        float inset=minimum;
        for(int i=0;i<body.Count;i++){var sphere=body[i];inset=Math.Max(inset,sphere.Radius+Math.Max(Math.Abs(sphere.Offset.X),Math.Abs(sphere.Offset.Z)));}
        return inset;
    }
    public Vector3 Move(Vector3 start, Vector3 target,IReadOnlyList<SceneMovement.BodySphere>? body=null)
    {
        float inset=EdgeInset(body??playerBody,.5f);
        target=Ground(Mathf.Clamp(target.X,WorldBounds.Position.X+inset,WorldBounds.End.X-inset),Mathf.Clamp(target.Z,WorldBounds.Position.Y+inset,WorldBounds.End.Y-inset));
        var result=SceneMovement.Move(Physics,Numeric(start),Numeric(target),body??playerBody,
            movementGround,movementValid,stepHeight);
        return new(result.X,result.Y,result.Z);
    }
    public Vector3 FindOpen(Vector3 preferred,IReadOnlyList<SceneMovement.BodySphere>? body=null)
    {
        bool Clear(Vector3 point)=>body is null?Walkable(point,1.2f):ValidGround(point)&&SceneMovement.Clear(Physics,Numeric(point),body,stepHeight);
        float inset=EdgeInset(body??playerBody,1.2f);
        preferred.X=Mathf.Clamp(preferred.X,WorldBounds.Position.X+inset,WorldBounds.End.X-inset);preferred.Z=Mathf.Clamp(preferred.Z,WorldBounds.Position.Y+inset,WorldBounds.End.Y-inset);
        var ground = Ground(preferred.X, preferred.Z); if (Clear(ground)) return ground;
        for (int i = 0; i < 400; i++)
        {
            float angle = i * 2.399963f, radius = Math.Min(WorldBounds.Size.X / 2 - 1, 1 + MathF.Sqrt(i) * 1.3f);
            var p = Ground(Mathf.Clamp(preferred.X + Mathf.Cos(angle) * radius, -HalfSize + inset, HalfSize - inset), Mathf.Clamp(preferred.Z + Mathf.Sin(angle) * radius, -HalfSize + inset, HalfSize - inset)); if (Clear(p)) return p;
        }
        throw new InvalidDataException("No safe ground near this position in the landblock grid. Choose a different center.");
    }
    public static uint[] Discover(DatArchive cells, AcWorldData.Region region)
    {
        var ids = cells.Entries.Keys.Where(id => (id & 0xffff) == 0xffff).Order().ToArray();
        var chosen = new Dictionary<string, uint>();
        var fallback = new Dictionary<string, uint>();
        for (int i = 0; i < Math.Min(ids.Length, 8000) && chosen.Count < 3; i++)
        {
            uint id = ids[(int)((i * 997L + 17000) % ids.Length)]; var b = AcWorldData.ReadBlock(cells.Read(id));
            string type = region.Terrains[AcWorldData.TerrainAt(b, 96, 96)].Name;
            string? biome = type.Contains("Grass", StringComparison.OrdinalIgnoreCase) ? "grass" : type.Contains("Snow", StringComparison.OrdinalIgnoreCase) ? "snow" : type.StartsWith("sand-", StringComparison.OrdinalIgnoreCase) ? "sand" : null;
            if (biome is null || chosen.ContainsKey(biome)) continue;
            try{if(NeighborIds(id).Any(n=>!cells.Entries.ContainsKey(n)))continue;}catch(InvalidDataException){continue;}
            var heights = new List<float>(); bool wet = false;
            foreach (float x in new[] {60f, 96, 132}) foreach (float y in new[] {60f, 96, 132})
            {
                heights.Add(AcWorldData.SampleHeight(b, region, x, y));
                wet |= region.Terrains[AcWorldData.TerrainAt(b, x, y)].Name.Contains("Water", StringComparison.OrdinalIgnoreCase);
            }
            if (wet || heights.Max() - heights.Min() > 9) continue;
            fallback.TryAdd(biome, id);
            uint info = id - 1;
            int nearbyObjects = b.HasObjects && cells.Entries.ContainsKey(info) ? AcWorldData.ReadPlacements(cells.Read(info)).Count(p => Math.Abs(p.Pose.Position.X - 96) < 40 && Math.Abs(p.Pose.Position.Y - 96) < 40) : 0;
            if (nearbyObjects < 2 || nearbyObjects > 20) continue;
            chosen[biome] = id;
        }
        foreach (var pair in fallback) chosen.TryAdd(pair.Key, pair.Value);
        return chosen.OrderBy(p => p.Key == "grass" ? 0 : p.Key == "snow" ? 1 : 2).Select(p => p.Value).ToArray();
    }
    public void Build(Node3D root, AcAssets assets, DatArchive cells, Func<Vector2, float, uint, bool>? keepNatural = null)
    {
        terrainRenderer=AcTerrainRenderer.For(assets,Region);BuildTerrainNormals();
        var player=assets.Setup(AcCatalog.Player.Setup);
        playerBody=player.Spheres.Select(s=>new SceneMovement.BodySphere(Numeric(AcAssets.Convert(s.Center)),s.Radius)).ToArray();
        if(playerBody.Length==0)playerBody=new[]{new SceneMovement.BodySphere(new(0,.5f,0),.48f),new SceneMovement.BodySphere(new(0,1.35f,0),.48f)};
        stepHeight=Math.Clamp(player.StepUpHeight,.05f,.6f);
        foreach(var block in Blocks.Values.OrderBy(b=>b.Id==Block.Id?0:1).ThenBy(b=>b.Id))
        {
            var chunk=new Node3D{Name=$"Landblock_{block.Id>>16:X4}"};root.AddChild(chunk);
            BuildBlock(chunk,assets,cells,block,keepNatural);
        }
    }
    private Vector3 Offset(AcWorldData.Block block)=>new(((int)(block.Id>>24)-(int)(Block.Id>>24))*BlockSize,0,-((int)((block.Id>>16)&255)-(int)((Block.Id>>16)&255))*BlockSize);
    public static int[] TileCornerIndices(uint block,int x,int y)
    {
        int a=x*9+y,b=a+9,c=a+1,d=b+1;
        return AcWorldData.NorthEastCut(block,x,y)?new[]{a,b,d,a,d,c}:new[]{a,b,c,b,d,c};
    }
    private (int X,int Y) VertexKey(AcWorldData.Block block,int vertex)=>((int)(block.Id>>24)*8+vertex/9,(int)((block.Id>>16)&255)*8+vertex%9);
    private Vector3 TerrainPoint(AcWorldData.Block block,int vertex)
    {
        var offset=Offset(block);return new(offset.X+vertex/9*24-CenterX,Region.Heights[block.Heights[vertex]]-baseHeight,offset.Z+CenterY-vertex%9*24);
    }
    private void BuildTerrainNormals()
    {
        terrainNormals.Clear();
        foreach(var block in Blocks.Values)for(int x=0;x<8;x++)for(int y=0;y<8;y++)
        {
            var vertices=TileCornerIndices(block.Id,x,y);
            for(int i=0;i<6;i+=3)
            {
                var a=TerrainPoint(block,vertices[i]);var b=TerrainPoint(block,vertices[i+1]);var c=TerrainPoint(block,vertices[i+2]);var normal=(b-a).Cross(c-a).Normalized();
                for(int n=0;n<3;n++){var key=VertexKey(block,vertices[i+n]);terrainNormals[key]=terrainNormals.GetValueOrDefault(key)+normal;}
            }
        }
    }
    private void BuildBlock(Node3D root,AcAssets assets,DatArchive cells,AcWorldData.Block block, Func<Vector2, float, uint, bool>? keepNatural)
    {
        var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        for(int channel=0;channel<4;channel++)tool.SetCustomFormat(channel,SurfaceTool.CustomFormat.RgbaFloat);
        for (int x = 0; x < 8; x++) for (int y = 0; y < 8; y++)
        {
            var triangle=TileCornerIndices(block.Id,x,y);var recipe=AcTerrainBlend.SurfaceFor(block,Region,x,y);
            AcTerrainRenderer.SetRecipe(tool,recipe);
            for(int i=0;i<6;i++)
            {
                // Reverse the native upward-cross-product winding for Godot.
                int v=triangle[i/3*3+(i%3==0?0:3-i%3)],vx=v/9,vy=v%9;var key=VertexKey(block,v);
                tool.SetNormal(terrainNormals[key].Normalized());tool.SetUV(new(key.X,key.Y));tool.SetUV2(new(vx-x,1-(vy-y)));tool.AddVertex(TerrainPoint(block,v));
            }
            Physics.Add(new[]{new ProjectileCollision.Triangle(Numeric(TerrainPoint(block,triangle[0])),Numeric(TerrainPoint(block,triangle[1])),Numeric(TerrainPoint(block,triangle[2]))),new ProjectileCollision.Triangle(Numeric(TerrainPoint(block,triangle[3])),Numeric(TerrainPoint(block,triangle[4])),Numeric(TerrainPoint(block,triangle[5])))},"terrain");
        }
        var mesh=tool.Commit();tool.Dispose();mesh.SurfaceSetMaterial(0,terrainRenderer!.Material);
        root.AddChild(new MeshInstance3D { Mesh = mesh, Name = $"DAT_Terrain_{block.Id:X8}" });
        uint info = (block.Id & 0xffff0000) | 0xfffe;
        if (cells.Entries.ContainsKey(info))
            foreach (var placement in AcWorldData.ReadPlacements(cells.Read(info)))
                Place(root, assets, block, placement.Model, placement.Pose, 1, placement.IsBuilding);
        // Seed each native block independently, so adjacent grids agree on its scenery.
        // Bound natural decoration per block; all authored static placements are retained.
        var rng = new RandomNumberGenerator { Seed = block.Id };
        for (int x = 0; x < 8; x++) for (int y = 0; y < 8; y++)
        {
            int index = x * 9 + y, type = (block.Terrain[index] >> 2) & 31, scenery = block.Terrain[index] >> 11;
            var terrain = Region.Terrains[type]; if (terrain.SceneTypes.Length == 0) continue;
            uint sceneType = terrain.SceneTypes[Math.Min(scenery, terrain.SceneTypes.Length - 1)];
            if (sceneType >= Region.Scenes.Length || Region.Scenes[sceneType].Length == 0) continue;
            var scenes = Region.Scenes[sceneType]; uint scene = scenes[rng.RandiRange(0, scenes.Length - 1)];
            if (!assets.Archive.Entries.ContainsKey(scene)) continue;
            int natural=0;
            foreach (var item in AcWorldData.ReadScene(assets.Archive.Read(scene)).Take(12))
            {
                if (rng.Randf() > item.Frequency || item.Model == 0) continue;
                // Keep consuming the deterministic RNG even after the decoration budget fills.
                float px = x * 24 + item.Pose.Position.X + rng.RandfRange(-Math.Abs(item.Dx), Math.Abs(item.Dx));
                float py = y * 24 + item.Pose.Position.Y + rng.RandfRange(-Math.Abs(item.Dy), Math.Abs(item.Dy));
                float scale=rng.RandfRange(Math.Max(.1f,item.MinScale),Math.Max(.1f,item.MaxScale));
                if(natural>=3||px<0||py<0||px>=BlockSize||py>=BlockSize||AcTerrainBlend.OnRoad(block,px,py))continue;
                var pose = new AcFormats.Pose(new(px, py, AcWorldData.SampleHeight(block, Region, px, py)), item.Pose.Rotation);
                if (keepNatural is not null)
                {
                    var offset = Offset(block);
                    var point = new Vector2(offset.X + px - CenterX, offset.Z + CenterY - py);
                    if (!keepNatural(point, scale, item.Model)) { natural++; continue; }
                }
                int before=PlacedObjects;Place(root, assets, block, item.Model, pose, scale);if(PlacedObjects>before)natural++;
            }
        }
    }
    private void Place(Node3D root, AcAssets assets,AcWorldData.Block block, uint model, AcFormats.Pose pose, float scale, bool isBuilding = false)
    {
        var offset=Offset(block);float x = offset.X+pose.Position.X-CenterX, z = offset.Z+CenterY-pose.Position.Y;
        if (!WorldBounds.Grow(24).HasPoint(new(x, z))) return;
        Node3D? node = null;
        try
        {
            node = model >> 24 == 2 ? assets.CreateActor(model, 0, scale) : new MeshInstance3D { Mesh = assets.Mesh(model), Scale = Vector3.One * scale };
            root.AddChild(node); var transform = AcAssets.Convert(pose); transform.Origin = new(x, pose.Position.Z - baseHeight, z);
            node.Transform = transform.ScaledLocal(Vector3.One * scale);
            if (node is MeshInstance3D meshNode) meshNode.SetMeta("ac_model", (long)model);
            string collision=AddPhysics(node, assets);
            var bounds = Bounds(node);
            float nativeRadius=model>>24==2?assets.Setup(model).Cylinders.FirstOrDefault()?.Radius??assets.Setup(model).Spheres.FirstOrDefault()?.Radius??0:0;
            CollisionObjects.Add(new(model,node.GlobalPosition,collision,nativeRadius*scale,Math.Max(bounds.Size.X,bounds.Size.Z)*scale*.5f,node.GlobalTransform,block.Id,isBuilding));
            PlacedObjects++;
        }
        catch (Exception ex) { if (node is not null) node.QueueFree(); FailedObjects++; GD.Print($"Landblock prop {model:X8} skipped: {ex.Message}"); }
    }

    private string AddPhysics(Node3D node, AcAssets assets)
    {
        var faces=new List<ProjectileCollision.Triangle>();
        Collect(node);
        // The reference client selects actual physics polygons first, then setup
        // cylinders, then setup spheres. Render bounds/canopies are never solids.
        if(faces.Count>0){Physics.Add(faces,"scenery");return "Mesh";}
        if(node is not AcActor actor)return "None";
        var setup=actor.Setup;var transform=node.GlobalTransform;
        if(setup.Cylinders.Length>0)
        {
            var axis=transform.Basis*Vector3.Up;float radiusScale=Math.Max((transform.Basis*Vector3.Right).Length(),(transform.Basis*Vector3.Back).Length());
            foreach(var c in setup.Cylinders)Physics.AddCylinder(Numeric(transform*AcAssets.Convert(c.Origin)),Numeric(axis.Normalized()),c.Height*axis.Length(),c.Radius*radiusScale);
            return "Cylinder";
        }
        float scale=Math.Max(transform.Basis.X.Length(),Math.Max(transform.Basis.Y.Length(),transform.Basis.Z.Length()));
        foreach(var sphere in setup.Spheres)Physics.AddSphere(Numeric(transform*AcAssets.Convert(sphere.Center)),sphere.Radius*scale);
        return setup.Spheres.Length>0?"Sphere":"None";
        void Collect(Node3D current)
        {
            if(current is MeshInstance3D&&current.HasMeta("ac_model"))faces.AddRange(assets.PhysicsTriangles(unchecked((uint)current.GetMeta("ac_model").AsInt64()),current.GlobalTransform));
            foreach(var child in current.GetChildren().OfType<Node3D>())Collect(child);
        }
    }
    private static Aabb Bounds(Node3D node)
    {
        if (node is MeshInstance3D mesh) return mesh.GetAabb();
        var bounds = new Aabb(); bool first = true;
        foreach (var child in node.GetChildren().OfType<Node3D>())
        {
            var b = child.Transform * Bounds(child); bounds = first ? b : bounds.Merge(b); first = false;
        }
        return bounds;
    }
}





