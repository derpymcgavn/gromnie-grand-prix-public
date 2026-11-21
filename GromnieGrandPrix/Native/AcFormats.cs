using System.Numerics;
using System.Text;

namespace DerethSurvivors;

// Binary layout decoding only. Game simulation and rendering live elsewhere.
public static class AcFormats
{
    public record Pose(Vector3 Position, Quaternion Rotation);
    public record Vertex(Vector3 Position, Vector3 Normal, Vector2[] Uvs);
    public record Polygon(ushort[] Vertices, byte[] Uvs, short Surface, int Sides);
    public record Geometry(uint Id, uint[] Surfaces, Dictionary<ushort, Vertex> Vertices, Polygon[] Polygons, Polygon[] PhysicsPolygons)
    {
        public uint DegradeId { get; init; }
    }
    public record CollisionSphere(Vector3 Center,float Radius);
    public record CollisionCylinder(Vector3 Origin,float Radius,float Height);
    public record Holding(int Part,Pose Pose);
    public record Setup(uint Id, uint[] Parts, uint[] Parents, Vector3[] Scales, Pose[] Placement,
        float Height, uint DefaultAnimation, uint Script, uint MotionTable, uint ScriptTable, float Radius, float ProjectileRadius,
        CollisionCylinder[] Cylinders,CollisionSphere[] Spheres,float StepUpHeight,float StepDownHeight,uint SoundTable,Dictionary<uint,Holding> Holdings,Dictionary<uint,Pose[]> Placements);
    public record ClothingPart(int Part, uint Model, Dictionary<uint, uint> Textures);
    public record PaletteRange(int Offset, int Count);
    public record ClothingPalette(uint Set, PaletteRange[] Ranges);
    public record ClothingColor(uint Icon, ClothingPalette[] Palettes);
    public record Clothing(Dictionary<uint, ClothingPart[]> Setups, Dictionary<uint, ClothingColor> Colors);
    public record Hook(uint Type, uint Asset, int Part, Pose Offset, uint EmitterId, float U = 0, float V = 0,float SoundPriority=0,float SoundProbability=1,float SoundVolume=1,int Direction=0)
    {
        // Retail ScaleHook is type12 and stores End/Time. Its starting scale
        // is the current visual scale; there is no serialized Start member.
        public float? ScaleEnd { get; init; }
        public float? ScaleTime { get; init; }
    }
    public record Frame(Pose[] Parts, Hook[] Hooks);
    public record Animation(uint Id, Frame[] Frames, int PartCount);
    public record Clip(uint Animation, int Low, int High, float Fps);
    public record Motion(uint Style, Dictionary<uint, Clip[]> Cycles, Dictionary<uint, Dictionary<uint, Clip[]>> Links,Dictionary<uint,uint> StyleDefaults,Dictionary<uint,Clip[]> Modifiers);
    public record Surface(uint Flags, uint Texture, uint Palette, uint Color, float Transparency, float Luminosity, float Diffuse);
    public record TimedHook(double Time, Hook Hook);
    public record Emitter(uint Id, int Type, int ParticleType, uint Mesh, uint HardwareMesh, double Birthrate,
        int Max, int Initial, int Total, double Seconds, double Life, double LifeRandom,
        Vector3 Offset, float MinOffset, float MaxOffset, Vector3 A, float MinA, float MaxA,
        Vector3 B, float MinB, float MaxB, Vector3 C, float MinC, float MaxC,
        float StartScale, float EndScale, float ScaleRandom, float StartTransparency, float EndTransparency, float TransparencyRandom, bool Local);

    private static readonly Pose Identity = new(Vector3.Zero, Quaternion.Identity);
    public static BinaryReader Reader(byte[] bytes) => new(new MemoryStream(bytes, false));
    public static void Skip(BinaryReader r, long bytes)
    {
        if (bytes < 0 || bytes > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidDataException("Truncated AC record.");
        r.BaseStream.Position += bytes;
    }
    public static int Count(BinaryReader r, int max = 100000) => Check(r.ReadUInt32(), max);
    private static int Check(uint n, int max) => n <= max ? (int)n : throw new InvalidDataException($"AC count {n} exceeds {max}.");
    private static int Packed(BinaryReader r)
    {
        byte a = r.ReadByte();
        if ((a & 128) == 0) return a;
        byte b = r.ReadByte();
        return Check((a & 64) == 0 ? (uint)((a & 127) * 256 + b) : (uint)(((a & 63) * 256 + b) * 65536 + r.ReadUInt16()), 100000);
    }
    public static Vector3 Vector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    public static Pose ReadPose(BinaryReader r)
    {
        var p = Vector(r); float w = r.ReadSingle(); var xyz = Vector(r);
        var q = new Quaternion(xyz, w);
        if (!float.IsFinite(p.LengthSquared()) || !float.IsFinite(q.LengthSquared()) || q.LengthSquared() < 0.001f)
            throw new InvalidDataException("Invalid AC part transform.");
        return new(p, Quaternion.Normalize(q));
    }
    public static Hook ReadHook(BinaryReader r)
    {
        uint type = r.ReadUInt32();int direction=r.ReadInt32();return ReadHookPayload(r,type) with{Direction=direction};
    }
    private static Hook ReadHookPayload(BinaryReader r,uint type)
    {
        if(type is 1 or 2)return new(type,r.ReadUInt32(),-1,Identity,0);
        if(type==21)return new(type,r.ReadUInt32(),-1,Identity,0,SoundPriority:r.ReadSingle(),SoundProbability:r.ReadSingle(),SoundVolume:r.ReadSingle());
        if(type==12)return new(type,0,-1,Identity,0){ScaleEnd=r.ReadSingle(),ScaleTime=r.ReadSingle()};
        if (type is 13 or 26)
        {
            uint asset = r.ReadUInt32(); int part = r.ReadInt32(); var offset = ReadPose(r); uint id = r.ReadUInt32();
            return new(type, asset, part, offset, id);
        }
        if (type == 19) { uint asset = r.ReadUInt32(); r.ReadSingle(); return new(type, asset, -1, Identity, 0); }
        if (type is 14 or 15) return new(type, 0, -1, Identity, r.ReadUInt32());
        if (type is 23 or 24)
        {
            int part = type == 24 ? r.ReadInt32() : -1;
            return new(type, 0, part, Identity, 0, r.ReadSingle(), r.ReadSingle());
        }
        if (type == 5)
        {
            r.ReadUInt16(); ushort id = r.ReadUInt16(); if ((id & 0x8000) != 0) r.ReadUInt16();
        }
        else
        {
            int bytes = type switch {
                0 or 4 or 17 => 0,
                1 or 2 or 6 or 14 or 15 or 16 or 18 or 25 => 4,
                12 or 23 => 8,
                8 or 10 or 20 or 22 or 24 => 12,
                7 or 9 or 11 or 21 => 16,
                3 => 28,
                _ => throw new NotSupportedException($"Unsupported AC hook {type}.")
            };
            Skip(r, bytes);
        }
        return new(type, 0, -1, Identity, 0);
    }
    private static Frame ReadFrame(BinaryReader r, int parts)
    {
        var poses = new Pose[parts]; for (int i = 0; i < parts; i++) poses[i] = ReadPose(r);
        var hooks = new Hook[Count(r, 4096)]; for (int i = 0; i < hooks.Length; i++) hooks[i] = ReadHook(r);
        return new(poses, hooks);
    }
    public static Setup ReadSetup(byte[] data)
    {
        using var r = Reader(data); uint id = r.ReadUInt32(), flags = r.ReadUInt32();
        int count = Count(r, 256); var parts = new uint[count];
        for (int i = 0; i < count; i++) parts[i] = r.ReadUInt32();
        var parents = Enumerable.Repeat(uint.MaxValue, count).ToArray();
        if ((flags & 1) != 0) for (int i = 0; i < count; i++) parents[i] = r.ReadUInt32();
        var scales = Enumerable.Repeat(Vector3.One, count).ToArray();
        if ((flags & 2) != 0) for (int i = 0; i < count; i++) scales[i] = Vector(r);
        var holdings=new Dictionary<uint,Holding>();int holdingCount=Count(r,1000);
        for(int i=0;i<holdingCount;i++){uint key=r.ReadUInt32();int part=r.ReadInt32();holdings[key]=new(part,ReadPose(r));}
        Skip(r, Count(r, 1000) * 36L); // connection points
        int placements = Count(r, 1000);
        var placementFrames=new Dictionary<uint,Pose[]>();
        Pose[] placement = Enumerable.Repeat(Identity, count).ToArray();
        for (int i = 0; i < placements; i++)
        {
            int key = r.ReadInt32(); var frame = ReadFrame(r, count);
            placementFrames[(uint)key]=frame.Parts;
            if (i == 0 || key == 0) placement = frame.Parts;
        }
        var cylinders=new CollisionCylinder[Count(r,1000)];
        for(int i=0;i<cylinders.Length;i++)cylinders[i]=new(Vector(r),r.ReadSingle(),r.ReadSingle());
        int sphereCount = Count(r, 1000); float projectileRadius = 0;
        var spheres=new CollisionSphere[sphereCount];
        for (int i = 0; i < sphereCount; i++) { var center=Vector(r);float radius=r.ReadSingle();spheres[i]=new(center,radius);if (i == 0) projectileRadius = radius; }
        float height = r.ReadSingle(), bodyRadius = r.ReadSingle(),stepUp=r.ReadSingle(),stepDown=r.ReadSingle();Skip(r,32);
        Skip(r, Count(r, 1000) * 48L); // light key plus pose and light properties
        uint animation = r.ReadUInt32(), script = r.ReadUInt32(), motion = r.ReadUInt32(),sound=r.ReadUInt32();uint scriptTable = r.ReadUInt32();
        if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException($"Setup {id:X8} layout mismatch.");
        return new(id, parts, parents, scales, placement, height, animation, script, motion, scriptTable, bodyRadius, projectileRadius,cylinders,spheres,stepUp,stepDown,sound,holdings,placementFrames);
    }
    public static Clothing ReadClothing(byte[] bytes)
    {
        using var r = Reader(bytes); r.ReadUInt32(); int setups = r.ReadUInt16(); r.ReadUInt16();
        var bases = new Dictionary<uint, ClothingPart[]>();
        for (int i = 0; i < setups; i++)
        {
            uint setup = r.ReadUInt32(); var parts = new ClothingPart[Count(r, 256)];
            for (int j = 0; j < parts.Length; j++)
            {
                int part = r.ReadInt32(); uint model = r.ReadUInt32(); int count = Count(r, 1024);
                var textures = new Dictionary<uint, uint>();
                for (int k = 0; k < count; k++)
                {
                    uint oldTexture=r.ReadUInt32(),newTexture=r.ReadUInt32();textures[oldTexture]=newTexture;
                }
                parts[j] = new(part, model, textures);
            }
            bases[setup] = parts;
        }
        int colors = r.ReadUInt16(); r.ReadUInt16(); var colorMap = new Dictionary<uint, ClothingColor>();
        for (int i = 0; i < colors; i++)
        {
            uint key = r.ReadUInt32(), icon = r.ReadUInt32(); var palettes = new ClothingPalette[Count(r, 256)];
            for (int j = 0; j < palettes.Length; j++)
            {
                var ranges = new PaletteRange[Count(r, 256)];
                for (int k = 0; k < ranges.Length; k++) ranges[k] = new(r.ReadInt32(), r.ReadInt32());
                palettes[j] = new(r.ReadUInt32(), ranges);
            }
            colorMap[key] = new(icon, palettes);
        }
        if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("Clothing table layout mismatch.");
        return new(bases, colorMap);
    }
    private static Polygon ReadPolygon(BinaryReader r)
    {
        int points = r.ReadByte(); byte stipple = r.ReadByte(); int sides = r.ReadInt32();
        short front = r.ReadInt16(); r.ReadInt16();
        if (points > 64) throw new InvalidDataException("Invalid AC polygon.");
        var ids = new ushort[points]; for (int i = 0; i < points; i++) ids[i] = r.ReadUInt16();
        var uv = new byte[points]; if ((stipple & 4) == 0) for (int i = 0; i < points; i++) uv[i] = r.ReadByte();
        if (sides == 2 && (stipple & 8) == 0) Skip(r, points);
        return new(ids, uv, front, sides);
    }
    private static Polygon[] ReadPolygons(BinaryReader r)
    {
        var result = new Polygon[Packed(r)];
        for (int i = 0; i < result.Length; i++) { r.ReadUInt16(); result[i] = ReadPolygon(r); }
        return result;
    }
    private static void SkipPhysicsTree(BinaryReader r, int depth = 0)
    {
        if (depth > 256) throw new InvalidDataException("Excessive AC BSP depth.");
        uint kind = r.ReadUInt32();
        if (kind == 0x4c454146) { Skip(r, 4 + 4 + 16); Skip(r, Count(r, 65536) * 2L); return; }
        Skip(r, 16);
        switch (kind)
        {
            case 0x42506e6e: case 0x4250496e: case 0x4270494e: case 0x42706e4e:
                SkipPhysicsTree(r, depth + 1); break;
            case 0x4250494e: case 0x42506e4e: case 0x504f5254:
                SkipPhysicsTree(r, depth + 1); SkipPhysicsTree(r, depth + 1); break;
            default: throw new InvalidDataException($"Unknown AC BSP tag {kind:X8}.");
        }
        if (kind != 0x504f5254) Skip(r, 16);
    }
    public static Geometry ReadGeometry(byte[] data)
    {
        using var r = Reader(data); uint id = r.ReadUInt32(), flags = r.ReadUInt32();
        var surfaces = new uint[Packed(r)]; for (int i = 0; i < surfaces.Length; i++) surfaces[i] = r.ReadUInt32();
        if (r.ReadInt32() != 1) throw new NotSupportedException("Unsupported AC vertex layout.");
        int count = Count(r, 65536); var vertices = new Dictionary<ushort, Vertex>(count);
        for (int i = 0; i < count; i++)
        {
            ushort key = r.ReadUInt16(); int uvCount = r.ReadUInt16();
            if (uvCount > 256) throw new InvalidDataException("Invalid AC UV count.");
            var p = Vector(r); var n = Vector(r); var uvs = new Vector2[uvCount];
            for (int j = 0; j < uvCount; j++) uvs[j] = new(r.ReadSingle(), r.ReadSingle());
            vertices.Add(key, new(p, n, uvs));
        }
        Polygon[] physics = Array.Empty<Polygon>();
        if ((flags & 1) != 0) { physics = ReadPolygons(r); SkipPhysicsTree(r); }
        Skip(r, 12);
        var drawing = (flags & 2) != 0 ? ReadPolygons(r) : Array.Empty<Polygon>();
        // DIDDegrade is the final uint after the drawing BSP, and is present
        // only with HasDIDDegrade (0x8). Keep the existing BSP-independent mesh
        // reader while retaining this authored particle-facing reference.
        uint degrade = (flags & 8) != 0 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4)) : 0;
        if (degrade != 0 && degrade >> 24 != 0x11) throw new InvalidDataException($"Invalid gfx degrade reference {id:X8}.");
        return new(id, surfaces, vertices, drawing, physics) { DegradeId = degrade };
    }
    public static uint? ReadDegradeFacing(byte[] data)
    {
        using var r = Reader(data);
        if (r.ReadUInt32() >> 24 != 0x11) throw new InvalidDataException("Invalid gfx degrade record.");
        int count = Count(r, 10000);
        if (r.BaseStream.Length - r.BaseStream.Position != count * 20L) throw new InvalidDataException("Invalid gfx degrade levels.");
        if (count == 0) return null;
        r.ReadUInt32(); // First (closest) model, followed by native facing mode.
        return r.ReadUInt32();
    }
    public static Animation ReadAnimation(byte[] data)
    {
        using var r = Reader(data); uint id = r.ReadUInt32(), flags = r.ReadUInt32();
        int parts = Count(r, 256), count = Count(r, 20000);
        if ((long)parts * count > 2000000) throw new InvalidDataException("Excessive AC animation size.");
        if ((flags & 1) != 0) Skip(r, count * 28L); // Root movement belongs to the arena simulation.
        var frames = new Frame[count]; for (int i = 0; i < count; i++) frames[i] = ReadFrame(r, parts);
        if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException($"Animation {id:X8} layout mismatch.");
        return new(id, frames, parts);
    }
    private static Clip[] ReadClips(BinaryReader r)
    {
        int count = r.ReadByte(); r.ReadByte(); byte flags = r.ReadByte();
        r.BaseStream.Position = (r.BaseStream.Position + 3) & ~3L;
        var clips = new Clip[count];
        for (int i = 0; i < count; i++) clips[i] = new(r.ReadUInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadSingle());
        if ((flags & 1) != 0) Skip(r, 12); if ((flags & 2) != 0) Skip(r, 12);
        return clips;
    }
    private static Dictionary<uint, Clip[]> ClipMap(BinaryReader r)
    {
        int count = Count(r, 65536); var map = new Dictionary<uint, Clip[]>();
        for (int i = 0; i < count; i++) map[r.ReadUInt32()] = ReadClips(r);
        return map;
    }
    public static Motion ReadMotion(byte[] data)
    {
        using var r = Reader(data); r.ReadUInt32(); uint style = r.ReadUInt32();
        var defaults=new Dictionary<uint,uint>();int styles=Count(r,1000);for(int i=0;i<styles;i++)defaults[r.ReadUInt32()]=r.ReadUInt32();
        var cycles = ClipMap(r);var modifiers=ClipMap(r);
        var links = new Dictionary<uint, Dictionary<uint, Clip[]>>();
        int count = Count(r, 65536); for (int i = 0; i < count; i++) links[r.ReadUInt32()] = ClipMap(r);
        return new(style, cycles, links,defaults,modifiers);
    }
    public static Surface ReadSurface(byte[] data)
    {
        using var r = Reader(data); uint flags = r.ReadUInt32(), tex = 0, palette = 0, color = 0xffffffff;
        if ((flags & 6) != 0) { tex = r.ReadUInt32(); palette = r.ReadUInt32(); } else color = r.ReadUInt32();
        return new(flags, tex, palette, color, r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }
    public static Emitter ReadEmitter(byte[] data)
    {
        using var r = Reader(data); uint id = r.ReadUInt32(); r.ReadUInt32();
        var e = new Emitter(id, r.ReadInt32(), r.ReadInt32(), r.ReadUInt32(), r.ReadUInt32(), r.ReadDouble(),
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(),
            Vector(r), r.ReadSingle(), r.ReadSingle(), Vector(r), r.ReadSingle(), r.ReadSingle(),
            Vector(r), r.ReadSingle(), r.ReadSingle(), Vector(r), r.ReadSingle(), r.ReadSingle(),
            r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadInt32() != 0);
        if (r.BaseStream.Position != r.BaseStream.Length || !double.IsFinite(e.Life) || e.Life <= 0 || e.Max < 0 || e.Max > 100000)
            throw new InvalidDataException($"Invalid AC emitter {id:X8}.");
        return e;
    }
    public static TimedHook[] ReadScript(byte[] data)
    {
        using var r = Reader(data); r.ReadUInt32(); var hooks = new TimedHook[Count(r, 10000)];
        for (int i = 0; i < hooks.Length; i++) hooks[i] = new(r.ReadDouble(), ReadHook(r));
        return hooks;
    }
    public static Dictionary<uint, uint> ReadScriptTable(byte[] data)
    {
        using var r = Reader(data); r.ReadUInt32(); int count = Count(r, 10000);
        var result = new Dictionary<uint, uint>();
        for (int i = 0; i < count; i++)
        {
            uint key = r.ReadUInt32(); int variants = Count(r, 1000);
            for (int j = 0; j < variants; j++)
            {
                r.ReadSingle(); uint script = r.ReadUInt32(); if (j == 0) result[key] = script;
            }
        }
        return result;
    }
}
