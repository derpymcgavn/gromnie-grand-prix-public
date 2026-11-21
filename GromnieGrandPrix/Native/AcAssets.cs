using Godot;
using NVector = System.Numerics.Vector3;

namespace DerethSurvivors;

public sealed partial class AcAssets : IDisposable
{
    public DatArchive Archive { get; }
    private readonly DatArchive? highres;
    public AcAssets(DatArchive dat, string folder)
    {
        Archive = dat;
        string path = System.IO.Path.Combine(folder, "client_highres.dat");
        if (System.IO.File.Exists(path)) highres = new DatArchive(path);
    }
    private readonly Dictionary<uint, AcFormats.Setup> setups = new();
    private readonly Dictionary<uint, AcFormats.Motion> motions = new();
    private readonly Dictionary<uint, AcFormats.Animation> animations = new();
    private readonly Dictionary<(uint, bool), ArrayMesh> meshes = new();
    private readonly Dictionary<(uint, bool, bool), StandardMaterial3D> materials = new();
    private readonly Dictionary<(uint, uint, bool), Texture2D?> textures = new();
    private readonly Dictionary<uint, AcFormats.Emitter> emitters = new();
    private readonly Dictionary<uint, AcFormats.TimedHook[]> scripts = new();
    private readonly Dictionary<uint, Dictionary<uint, uint>> scriptTables = new();
    private readonly Dictionary<uint,float?> nativeLaunchScales = new();
    private readonly Dictionary<ulong,(WeakRef Material,Vector3 Velocity)> scrolling = new();
    private readonly List<ulong> expiredScrolling=new();
    private readonly Dictionary<uint, AcFormats.Clothing> clothing = new();
    private readonly Dictionary<string, ArrayMesh> wornMeshes = new();
    private readonly Dictionary<uint, AcFormats.Geometry> geometry = new();
    private readonly Dictionary<uint,(uint? Mode,bool Billboard,Vector3 AxisX,Vector3 AxisY)> particleFacing = new();
    private readonly Dictionary<uint,uint[]> palettes=new();
    private readonly Dictionary<uint,AcFormats.Surface> surfaceDefinitions=new();
    private readonly Dictionary<uint,bool> portalIndexedTextures=new();
    private readonly NativeAppearanceTextureCache appearanceTextures=new();
    private readonly Dictionary<uint, Texture2D?> normalMaps = [];
    private readonly Queue<uint> normalMapOrder = new();
    public long GeometryReads{get;private set;}
    public long MeshBuilds{get;private set;}
    public long WornMeshBuilds{get;private set;}
    public long MaterialBuilds{get;private set;}
    public long TextureDecodes{get;private set;}
    public long AnimationReads{get;private set;}
    public long MetadataReads{get;private set;}
    public long ColdCacheMisses=>GeometryReads+MeshBuilds+WornMeshBuilds+MaterialBuilds+TextureDecodes+AnimationReads+MetadataReads+appearanceTextures.Misses;
    public int AppearanceTextureCount=>appearanceTextures.Count;
    public long AppearanceTextureBytes=>appearanceTextures.EstimatedResidentBytes;
    public long AppearanceTextureHits=>appearanceTextures.Hits;
    public long AppearanceTextureMisses=>appearanceTextures.Misses;
    public long AppearanceTextureEvictions=>appearanceTextures.Evictions;
    public AcFormats.Geometry Geometry(uint id)
    {
        TouchAsset(AcAssetCacheKind.Geometry,id);
        if(geometry.TryGetValue(id,out var value))return value;
        GeometryReads++;return geometry[id]=AcFormats.ReadGeometry(Archive.Read(id));
    }
    public (uint? Mode,bool Billboard,Vector3 AxisX,Vector3 AxisY) ParticleFacing(uint id)
    {
        TouchAsset(AcAssetCacheKind.ParticleFacing,id);
        if(particleFacing.TryGetValue(id,out var cached))return cached;
        var source=Geometry(id);
        uint? mode=source.DegradeId!=0?AcFormats.ReadDegradeFacing(Archive.Read(source.DegradeId)):null;
        Vector3 minimum=new(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity),maximum=new(float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity);
        foreach(var vertex in source.Vertices.Values){var p=Convert(vertex.Position);minimum=minimum.Min(p);maximum=maximum.Max(p);}
        var size=source.Vertices.Count==0?Vector3.Zero:maximum-minimum;
        // An explicit non-facing mode takes precedence even for planar art.
        // Older sprite assets without a degrade record retain the same planar
        // fallback used by DerethClient's DatParticleEmitter.
        bool billboard=mode is null&&size.Z<.0001f&&size.X>.0001f&&size.Y>.0001f;
        Vector3 x=Vector3.Right,y=Vector3.Up;
        if(size.X<size.Y&&size.X<size.Z){x=Vector3.Up;y=Vector3.Back;}
        else if(size.Y<size.Z)y=Vector3.Back;
        return particleFacing[id]=(mode,billboard,x,y);
    }
    public AcFormats.Clothing Clothing(uint id)
    {
        TouchAsset(AcAssetCacheKind.Clothing,id);
        if(clothing.TryGetValue(id,out var value))return value;
        MetadataReads++;return clothing[id]=AcFormats.ReadClothing(Archive.Read(id));
    }
    public IEnumerable<ProjectileCollision.Triangle> PhysicsTriangles(uint id, Transform3D transform)
    {
        var g = Geometry(id);
        foreach (var p in g.PhysicsPolygons)
            for (int i = 1; i < p.Vertices.Length - 1; i++)
            {
                var a = transform * Convert(g.Vertices[p.Vertices[0]].Position);
                var b = transform * Convert(g.Vertices[p.Vertices[i]].Position);
                var c = transform * Convert(g.Vertices[p.Vertices[i + 1]].Position);
                yield return new(new(a.X,a.Y,a.Z),new(b.X,b.Y,b.Z),new(c.X,c.Y,c.Z));
            }
    }

    internal float SceneryCollisionRadius(uint model)
    {
        float radius = 0;
        void IncludeGeometry(uint id, Transform3D transform)
        {
            var geometry = Geometry(id);
            foreach (var polygon in geometry.PhysicsPolygons)
                foreach (ushort index in polygon.Vertices)
                {
                    var point = transform * Convert(geometry.Vertices[index].Position);
                    radius = Math.Max(radius, new Vector2(point.X, point.Z).Length());
                }
        }
        if (model >> 24 == 1) IncludeGeometry(model, Transform3D.Identity);
        else if (model >> 24 == 2)
        {
            var setup = Setup(model);
            for (int i = 0; i < setup.Parts.Length; i++)
                if (setup.Parts[i] != 0)
                    IncludeGeometry(setup.Parts[i], Convert(setup.Placement[i]).ScaledLocal(Convert(setup.Scales[i]).Abs()));
            foreach (var shape in setup.Cylinders)
                radius = Math.Max(radius, new Vector2(shape.Origin.X, shape.Origin.Y).Length() + shape.Radius);
            foreach (var shape in setup.Spheres)
                radius = Math.Max(radius, new Vector2(shape.Center.X, shape.Center.Y).Length() + shape.Radius);
        }
        return radius;
    }
    private uint[] Palette(uint id)
    {
        TouchAsset(AcAssetCacheKind.Palette,id);
        if(palettes.TryGetValue(id,out var cached))return cached;
        MetadataReads++;
        using var r = AcFormats.Reader(Archive.Read(id)); r.ReadUInt32(); var colors = new uint[AcFormats.Count(r, 65536)];
        for (int i = 0; i < colors.Length; i++) colors[i] = r.ReadUInt32(); return palettes[id]=colors;
    }
    public Dictionary<int, uint> ClothingColors(AcFormats.Clothing table, uint color, float shade)
    {
        var result = new Dictionary<int, uint>();
        if (!table.Colors.TryGetValue(color, out var variant)) return result;
        foreach (var palette in variant.Palettes)
        {
            var set = Palette(palette.Set); if (set.Length == 0) continue;
            uint selected = set[Math.Clamp((int)(shade * (set.Length - 0.000001f)), 0, set.Length - 1)]; var colors = Palette(selected);
            foreach (var range in palette.Ranges)
                for (int i = 0; i < range.Count && range.Offset+i < colors.Length; i++) result[range.Offset + i] = colors[range.Offset+i];
        }
        return result;
    }
    public Dictionary<int,uint> PaletteColors(uint palette,int offset=0,int count=-1)
    {
        var result=new Dictionary<int,uint>();if(palette==0)return result;var colors=Palette(palette);
        if(count<0)count=colors.Length;
        if(count==0)count=256;
        long end=Math.Min(colors.Length,(long)offset+count);
        for(int i=Math.Max(0,offset);i<end;i++)result[i]=colors[i];return result;
    }
    public static string ColorSignature(IReadOnlyDictionary<int,uint> colors)
    {
        var bytes=new byte[checked(colors.Count*8)];int offset=0;
        foreach(var pair in colors.OrderBy(p=>p.Key))
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset),pair.Key);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset+4),pair.Value);offset+=8;
        }
        return System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
    public ArrayMesh WearMesh(AcFormats.ClothingPart part, Dictionary<int, uint> colors, string signature,uint paletteFallback=0)
    {
        string key=WearMeshKey(part,signature,paletteFallback);TouchAsset(AcAssetCacheKind.WornMesh,key);
        if (wornMeshes.TryGetValue(key, out var existing)) return existing;
        WornMeshBuilds++;
        var mesh=(ArrayMesh)Mesh(part.Model).Duplicate();var surfaceIds=GeometrySurfaceIds(Geometry(part.Model));
        string colorSignature=CanonicalColorSignature(signature,colors);
        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
            ApplyWornSurface(mesh,i,surfaceIds[i],part,colors,colorSignature,paletteFallback);
        wornMeshes[key]=mesh;return mesh;
    }
    private static string WearMeshKey(AcFormats.ClothingPart part,string signature,uint paletteFallback)
        =>$"{part.Model:X8}/{paletteFallback:X8}/{signature}/{string.Join(',',part.Textures.OrderBy(p=>p.Key).Select(p=>$"{p.Key:X8}:{p.Value:X8}"))}";
    private static string CanonicalColorSignature(string signature,Dictionary<int,uint> colors)
    {
        if(colors.Count==0)return "";
        int slash=signature.LastIndexOf('/');var suffix=slash<0?signature:signature[(slash+1)..];
        return suffix.Length==64&&suffix.All(Uri.IsHexDigit)?suffix:ColorSignature(colors);
    }
    private static uint[] GeometrySurfaceIds(AcFormats.Geometry source)=>source.Polygons
        .Where(p=>p.Surface>=0&&p.Surface<source.Surfaces.Length&&p.Vertices.Length>=3)
        .Select(p=>p.Surface).Distinct().Select(index=>source.Surfaces[index]).ToArray();
    private AcFormats.Surface SurfaceDefinition(uint id)
    {
        TouchAsset(AcAssetCacheKind.SurfaceDefinition,id);
        if(surfaceDefinitions.TryGetValue(id,out var surface))return surface;
        MetadataReads++;return surfaceDefinitions[id]=AcFormats.ReadSurface(Archive.Read(id));
    }
    private uint ResolveTextureId(uint texture)
    {
        if(texture>>24!=5)return texture;
        using var r=AcFormats.Reader(Archive.Read(texture));AcFormats.Skip(r,9);int count=AcFormats.Count(r,1000);texture=0;
        for(int i=0;i<count;i++){uint candidate=r.ReadUInt32();if(texture==0&&(Archive.Entries.ContainsKey(candidate)||highres?.Entries.ContainsKey(candidate)==true))texture=candidate;}
        return texture;
    }
    private bool PortalTextureIsIndexed(uint texture)
    {
        TouchAsset(AcAssetCacheKind.PortalTextureFormat,texture);
        if(portalIndexedTextures.TryGetValue(texture,out bool indexed))return indexed;
        MetadataReads++;var bytes=Archive.Read(texture);
        indexed=bytes.Length>=20&&System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)) is 41 or 101;
        portalIndexedTextures[texture]=indexed;return indexed;
    }
    private void ApplyWornSurface(ArrayMesh mesh,int index,uint surfaceId,AcFormats.ClothingPart part,Dictionary<int,uint> colors,string signature,uint paletteFallback)
    {
        var material=(StandardMaterial3D)Surface(surfaceId).Duplicate();var surface=SurfaceDefinition(surfaceId);
        uint texture=ResolveTextureId(part.Textures.GetValueOrDefault(surface.Texture,surface.Texture));
        if(texture!=0)
        {
            var source=highres?.Entries.ContainsKey(texture)==true?highres:Archive;
            if(colors.Count>0&&Archive.Entries.ContainsKey(texture)&&PortalTextureIsIndexed(texture))source=Archive;
            var image=appearanceTextures.Load(source,texture,surface.Palette!=0?surface.Palette:paletteFallback,Archive,(surface.Flags&4)!=0,colors,signature,preferEmbeddedPalette:true);
            if(image is null)throw new InvalidDataException($"Clothing texture {texture:X8} could not decode.");
            material.AlbedoTexture=image;material.AlbedoColor=new(1,1,1,1-surface.Transparency);
            ApplySurfaceRelief(material, texture);
        }
        mesh.SurfaceSetMaterial(index,material);
    }
    public int MeshCount => meshes.Count;
    public int TextureCount => textures.Values.Count(t => t is not null);
    public int AnimationCount => animations.Count;
    public int EmitterCount => emitters.Count;
    public int WornMeshCount=>wornMeshes.Count;
    public int TrimCount{get;private set;}
    public string CacheSummary=>$"mesh={meshes.Count} worn={wornMeshes.Count} texture={textures.Count} material={materials.Count} geometry={geometry.Count} animation={animations.Count} setup={setups.Count} motion={motions.Count} trims={TrimCount}";
    public int Triangles { get; private set; }
    public int FailedTextures { get; private set; }
    public static Vector3 Convert(NVector v) => new(v.X, v.Z, -v.Y);
    public static Transform3D Convert(AcFormats.Pose p)
    {
        var q = p.Rotation;
        return new(new Basis(new Quaternion(q.X, q.Z, -q.Y, q.W)), Convert(p.Position));
    }
    public AcFormats.Setup Setup(uint id)
    {
        TouchAsset(AcAssetCacheKind.Setup,id);if(setups.TryGetValue(id,out var value))return value;
        MetadataReads++;return setups[id]=AcFormats.ReadSetup(Archive.Read(id));
    }
    public Texture2D? Texture(uint id)
    {
        if (id == 0) return null;
        if ((id >> 24) == 5)
        {
            using var r = AcFormats.Reader(Archive.Read(id)); AcFormats.Skip(r, 9); int count = AcFormats.Count(r, 1000); id = 0;
            for (int i = 0; i < count; i++) { uint next = r.ReadUInt32(); if (id == 0 && (Archive.Entries.ContainsKey(next) || highres?.Entries.ContainsKey(next) == true)) id = next; }
        }
        if (id == 0) return null;
        var key = (id, 0u, false);
        TouchAsset(AcAssetCacheKind.Texture,key);
        if (!textures.TryGetValue(key, out var texture))
        {
            TextureDecodes++;
            texture = DatTextures.Load(highres?.Entries.ContainsKey(id) == true ? highres : Archive, id, 0, Archive); textures[key] = texture;
            if (texture is null) FailedTextures++;
            else
            {
                var image = texture.GetImage();
                if (image.IsCompressed()) image.Decompress();
                image.GenerateMipmaps(); textures[key] = texture = ImageTexture.CreateFromImage(image);
            }
        }
        return texture;
    }
    public AcFormats.Animation Animation(uint id)
    {
        TouchAsset(AcAssetCacheKind.Animation,id);if(animations.TryGetValue(id,out var value))return value;
        AnimationReads++;return animations[id]=AcFormats.ReadAnimation(Archive.Read(id));
    }
    public AcFormats.Motion Motion(uint id)
    {
        TouchAsset(AcAssetCacheKind.Motion,id);if(motions.TryGetValue(id,out var value))return value;
        MetadataReads++;return motions[id]=AcFormats.ReadMotion(Archive.Read(id));
    }
    public AcFormats.Emitter Emitter(uint id)
    {
        TouchAsset(AcAssetCacheKind.Emitter,id);if(emitters.TryGetValue(id,out var value))return value;
        MetadataReads++;return emitters[id]=AcFormats.ReadEmitter(Archive.Read(id));
    }
    public AcFormats.TimedHook[] Script(uint id)
    {
        TouchAsset(AcAssetCacheKind.Script,id);if(scripts.TryGetValue(id,out var value))return value;
        MetadataReads++;return scripts[id]=AcFormats.ReadScript(Archive.Read(id));
    }
    public uint EffectScript(uint table, uint effect)
    {
        if (effect == 0 || table == 0) return 0;
        TouchAsset(AcAssetCacheKind.ScriptTable,table);
        if (!scriptTables.TryGetValue(table, out var map)){MetadataReads++;scriptTables[table] = map = AcFormats.ReadScriptTable(Archive.Read(table));}
        return map.GetValueOrDefault(effect);
    }
    public float NativeLaunchScale(uint effectTable,float fallback)
    {
        if(effectTable==0)return fallback;
        TouchAsset(AcAssetCacheKind.LaunchScale,effectTable);
        if(!nativeLaunchScales.TryGetValue(effectTable,out var scale))
        {
            scale=null;uint script=EffectScript(effectTable,4);
            if(script!=0)foreach(var step in Script(script))
                if(step.Time==0&&step.Hook.Type==12&&step.Hook.ScaleTime==0&&step.Hook.ScaleEnd is float end&&float.IsFinite(end)&&end>0&&end<=100)
                    scale=end;
            nativeLaunchScales[effectTable]=scale;
        }
        // This reports an immediate visual override only. Callers explicitly
        // choose where to apply it; native physics profiles remain untouched.
        return scale??fallback;
    }
    public AcFormats.Clip[] Clips(uint motionTable, uint command, bool action = false,uint style=0,uint current=0x41000003)
    {
        if (motionTable == 0) return Array.Empty<AcFormats.Clip>();
        var motion = Motion(motionTable);uint active=style==0?motion.Style:style;
        uint key=MotionKey(active,command);
        if(!action&&motion.Cycles.TryGetValue(key,out var cycle)&&cycle.Length>0)return cycle;
        if(action)
        {
            uint fallback=motion.StyleDefaults.GetValueOrDefault(active,motion.StyleDefaults.GetValueOrDefault(0x80000000|(active&0xffff),0x41000003u));
            // The three source states retain native priority and deduplication
            // without an array, iterator or hash set for every action lookup.
            for(int index=0;index<3;index++)
            {
                uint from=index==0?current:index==1?fallback:0;
                if(index==1&&from==current||index==2&&(current==0||fallback==0))continue;
                if(motion.Links.TryGetValue(MotionKey(active,from),out var links)&&links.TryGetValue(command,out var linked)&&linked.Length>0)return linked;
            }
        }
        if(motion.Cycles.TryGetValue(key,out cycle)&&cycle.Length>0)return cycle;
        if(motion.Modifiers.TryGetValue(key,out var modifier)&&modifier.Length>0)return modifier;
        // A missing command stays missing: never borrow a different weapon stance.
        return Array.Empty<AcFormats.Clip>();
    }
    private static uint MotionKey(uint active,uint command)=>(active&0xffff)<<16|(command&0xffff);

    private void ApplySurfaceRelief(StandardMaterial3D material, uint texture)
    {
        if (material.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded ||
            material.Transparency != BaseMaterial3D.TransparencyEnum.Disabled || material.AlbedoTexture is not { } source) return;
        if (!normalMaps.TryGetValue(texture, out var normal))
        {
            if (normalMapOrder.Count >= 256) normalMaps.Remove(normalMapOrder.Dequeue());
            normal = NativeSurfaceRelief.FromTexture(source);
            normalMaps.Add(texture, normal);
            normalMapOrder.Enqueue(texture);
        }
        material.NormalTexture = normal;
        material.NormalEnabled = normal is not null;
        material.NormalScale = .76f;
    }

    public StandardMaterial3D Surface(uint id, bool particle = false, bool billboard = false)
    {
        TouchAsset(AcAssetCacheKind.Material,(id,particle,billboard));
        if (materials.TryGetValue((id, particle, billboard), out var material)) return material;
        MaterialBuilds++;var s = SurfaceDefinition(id);
        material = new StandardMaterial3D {
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 1, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled, MetallicSpecular = 0,
            AlbedoColor = new Color((s.Color >> 16 & 255) / 255f, (s.Color >> 8 & 255) / 255f, (s.Color & 255) / 255f, 1 - s.Transparency)
        };
        uint texture = 0;
        if (s.Texture != 0)
        {
            texture = ResolveTextureId(s.Texture);
            if (texture != 0)
            {
                var key = (texture, s.Palette, (s.Flags & 4) != 0);
                TouchAsset(AcAssetCacheKind.Texture,key);
                if (!textures.TryGetValue(key, out var image))
                {
                    TextureDecodes++;
                    var archive = highres?.Entries.ContainsKey(texture) == true ? highres : Archive;
                    image = DatTextures.Load(archive, texture, s.Palette, Archive, (s.Flags & 4) != 0,preferEmbeddedPalette:true); textures[key] = image;
                    if (image is null) { FailedTextures++; GD.PushWarning($"Unsupported AC texture {texture:X8}"); }
                }
                material.AlbedoTexture = image;
                material.AlbedoColor = new Color(1, 1, 1, 1 - s.Transparency);
            }
        }
        if ((s.Flags & 4) != 0) { material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor; material.AlphaScissorThreshold = 0.3f; }
        if (particle || s.Transparency > 0 || (s.Flags & (0x100 | 0x200 | 0x10000)) != 0)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            material.NoDepthTest = false;
            material.DepthDrawMode=BaseMaterial3D.DepthDrawModeEnum.Disabled;
        }
        if ((s.Flags & 0x10000) != 0) material.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
        if (particle || s.Luminosity > 0)
        {
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            material.DisableReceiveShadows = true;
        }
        if (particle)
        {
            material.BillboardMode = billboard ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled;
            material.BillboardKeepScale = true;
            material.TextureRepeat=false;
        }
        else if (texture != 0) ApplySurfaceRelief(material, texture);
        materials[(id, particle, billboard)] = material;
        return material;
    }
    public ArrayMesh Mesh(uint id, bool particle = false)
    {
        TouchAsset(AcAssetCacheKind.Mesh,(id,particle));
        // Keep mesh construction in a separate method: its LINQ predicate
        // captures the source geometry, otherwise allocating a display class
        // even when this very frequent lookup returns an already cached mesh.
        return meshes.TryGetValue((id,particle),out var cached)?cached:BuildMesh(id,particle);
    }
    private ArrayMesh BuildMesh(uint id,bool particle)
    {
        MeshBuilds++;
        var source = Geometry(id);
        bool billboard=particle&&ParticleFacing(id).Billboard;
        var mesh = new ArrayMesh();
        foreach (var group in source.Polygons.Where(p => p.Surface >= 0 && p.Surface < source.Surfaces.Length && p.Vertices.Length >= 3).GroupBy(p => p.Surface))
        {
            int triangleCount=group.Sum(p=>p.Vertices.Length-2),cursor=0;
            var positions=new Vector3[checked(triangleCount*3)];var normals=new Vector3[positions.Length];var uvs=new Vector2[positions.Length];
            foreach (var polygon in group)
            for (int i = 1; i < polygon.Vertices.Length - 1; i++)
            {
                for(int corner=0;corner<3;corner++)
                {
                    int index=corner==0?0:corner==1?i:i+1;
                    var vertex = source.Vertices[polygon.Vertices[index]];
                    positions[cursor]=Convert(vertex.Position);normals[cursor]=Convert(vertex.Normal);
                    var uv = vertex.Uvs.Length > polygon.Uvs[index] ? vertex.Uvs[polygon.Uvs[index]] : System.Numerics.Vector2.Zero;
                    uvs[cursor++]=new(uv.X,uv.Y);
                }
                Triangles++;
            }
            var arrays = new Godot.Collections.Array(); arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Godot.Mesh.ArrayType.Normal] = normals; arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs;
            var material = Surface(source.Surfaces[group.Key], particle, billboard);
            if (!particle && material.ShadingMode != BaseMaterial3D.ShadingModeEnum.Unshaded &&
                material.Transparency == BaseMaterial3D.TransparencyEnum.Disabled)
                arrays[(int)Godot.Mesh.ArrayType.Tangent] = NativeSurfaceRelief.Tangents(positions, normals, uvs);
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
        meshes[(id, particle)] = mesh;
        return mesh;
    }
    public AcActor CreateActor(uint setup, uint motionTable, float scale = 1)
    {
        var actor = new AcActor();
        try { actor.Initialize(this, Setup(setup), motionTable); actor.Scale = Vector3.One * scale; return actor; }
        catch { actor.Free(); throw; }
    }
    public string Summary => $"{MeshCount} meshes · {TextureCount} textures · {AnimationCount} animations · {EmitterCount} emitters";
    public void Scroll(uint geometry, float u, float v)
    {
        ScrollMesh(Mesh(geometry),u,v);
    }
    public void ScrollMesh(Mesh mesh,float u,float v)
    {
        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
            if (mesh.SurfaceGetMaterial(i) is StandardMaterial3D material)
            {
                ulong id=material.GetInstanceId();
                if(scrolling.TryGetValue(id,out var current))scrolling[id]=(current.Material,new(u,v,0));
                else if(GodotObject.WeakRef(material) is {} weak)scrolling[id]=(weak,new(u,v,0));
            }
    }
    public void Tick(float dt)
    {
        expiredScrolling.Clear();
        foreach(var pair in scrolling)
        {
            if(pair.Value.Material.GetRef().AsGodotObject() is StandardMaterial3D material&&GodotObject.IsInstanceValid(material))material.Uv1Offset+=pair.Value.Velocity*dt;
            else expiredScrolling.Add(pair.Key);
        }
        foreach(ulong id in expiredScrolling)scrolling.Remove(id);
    }
    // Cache references never own a live actor's native resources. Upcoming
    // cohorts hold bounded leases; trim old unpinned entries instead of clearing
    // freshly prepared generations. Forced trim invalidates every lease.
    public void TrimCaches(bool force=false)
    {
        bool trimmed=force;
        if(force){InvalidateAllPreparations();appearanceTextures.Clear();normalMaps.Clear();normalMapOrder.Clear();}
        trimmed|=TrimAssetCache(wornMeshes,1536,AcAssetCacheKind.WornMesh,force);
        trimmed|=TrimAssetCache(meshes,2048,AcAssetCacheKind.Mesh,force);
        trimmed|=TrimAssetCache(materials,2048,AcAssetCacheKind.Material,force);
        trimmed|=TrimAssetCache(textures,1024,AcAssetCacheKind.Texture,force);
        trimmed|=TrimAssetCache(geometry,1024,AcAssetCacheKind.Geometry,force);
        trimmed|=TrimAssetCache(particleFacing,1024,AcAssetCacheKind.ParticleFacing,force);
        trimmed|=TrimAssetCache(animations,512,AcAssetCacheKind.Animation,force);
        trimmed|=TrimAssetCache(setups,512,AcAssetCacheKind.Setup,force);
        trimmed|=TrimAssetCache(motions,256,AcAssetCacheKind.Motion,force);
        trimmed|=TrimAssetCache(clothing,512,AcAssetCacheKind.Clothing,force);
        trimmed|=TrimAssetCache(palettes,512,AcAssetCacheKind.Palette,force);
        trimmed|=TrimAssetCache(emitters,256,AcAssetCacheKind.Emitter,force);
        trimmed|=TrimAssetCache(scripts,512,AcAssetCacheKind.Script,force);
        trimmed|=TrimAssetCache(scriptTables,256,AcAssetCacheKind.ScriptTable,force);
        trimmed|=TrimAssetCache(nativeLaunchScales,256,AcAssetCacheKind.LaunchScale,force);
        trimmed|=TrimAssetCache(surfaceDefinitions,2048,AcAssetCacheKind.SurfaceDefinition,force);
        trimmed|=TrimAssetCache(portalIndexedTextures,1024,AcAssetCacheKind.PortalTextureFormat,force);
        if(trimmed)TrimCount++;
    }
    public void Dispose(){InvalidateAllPreparations();appearanceTextures.Dispose();highres?.Dispose();}
}
