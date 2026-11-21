using System.Numerics;
using System.Text;

namespace DerethSurvivors;

public static class AcWorldData
{
    public record Terrain(string Name, uint Color, uint[] SceneTypes, uint Texture = 0, uint Tiling = 1);
    public record AlphaMap(uint Code,uint Texture);
    public record TerrainTexture(uint Type,uint Texture,uint Tiling,uint DetailTexture,uint DetailTiling);
    public record TerrainBlend(uint BaseTextureSize,AlphaMap[] Corner,AlphaMap[] Side,AlphaMap[] Road,TerrainTexture[] Textures);
    public record Region(float[] Heights, Terrain[] Terrains, uint[][] Scenes,TerrainBlend? Blend=null);
    public record Block(uint Id, ushort[] Terrain, byte[] Heights, bool HasObjects);
    public record Placement(uint Model, AcFormats.Pose Pose, bool IsBuilding = false);
    public record SceneObject(uint Model, AcFormats.Pose Pose, float Frequency, float Dx, float Dy, float MinScale, float MaxScale, float Rotation);
    private static void Align(BinaryReader r) => r.BaseStream.Position = (r.BaseStream.Position + 3) & ~3L;
    private static string Text(BinaryReader r)
    {
        int size = r.ReadUInt16(); var bytes = r.ReadBytes(size);
        if (bytes.Length != size) throw new EndOfStreamException();
        Align(r); return Encoding.Latin1.GetString(bytes);
    }
    private static uint[] Ids(BinaryReader r)
    {
        var ids = new uint[AcFormats.Count(r, 10000)]; for (int i = 0; i < ids.Length; i++) ids[i] = r.ReadUInt32(); return ids;
    }
    public static Region ReadRegion(byte[] bytes)
    {
        using var r = AcFormats.Reader(bytes);
        if (r.ReadUInt32() != 0x13000000) throw new InvalidDataException("Not an AC region record.");
        AcFormats.Skip(r, 8); Text(r); AcFormats.Skip(r, 32);
        var heights = new float[256]; for (int i = 0; i < 256; i++) heights[i] = r.ReadSingle();
        AcFormats.Skip(r, 20); Text(r);
        int count = AcFormats.Count(r, 1000);
        for (int i = 0; i < count; i++) { AcFormats.Skip(r, 8); Text(r); }
        count = AcFormats.Count(r, 1000); for (int i = 0; i < count; i++) Text(r);
        count = AcFormats.Count(r, 1000); for (int i = 0; i < count; i++) { AcFormats.Skip(r, 4); Text(r); }
        uint mask = r.ReadUInt32();
        if ((mask & 0x10) != 0)
        {
            AcFormats.Skip(r, 16); Align(r); count = AcFormats.Count(r, 1000);
            for (int i = 0; i < count; i++)
            {
                AcFormats.Skip(r, 4); Text(r); AcFormats.Skip(r, AcFormats.Count(r, 1000) * 36L);
                int times = AcFormats.Count(r, 1000);
                for (int j = 0; j < times; j++) { AcFormats.Skip(r, 44); Align(r); AcFormats.Skip(r, AcFormats.Count(r, 1000) * 24L); }
            }
        }
        if ((mask & 1) != 0)
        {
            count = AcFormats.Count(r, 10000);
            for (int i = 0; i < count; i++) { AcFormats.Skip(r, 4); AcFormats.Skip(r, AcFormats.Count(r, 10000) * 20L); }
        }
        uint[][] scenes = Array.Empty<uint[]>();
        if ((mask & 2) != 0)
        {
            scenes = new uint[AcFormats.Count(r, 10000)][];
            for (int i = 0; i < scenes.Length; i++) { r.ReadUInt32(); scenes[i] = Ids(r); }
        }
        var terrains = new Terrain[AcFormats.Count(r, 256)];
        for (int i = 0; i < terrains.Length; i++) { string name = Text(r); uint color = r.ReadUInt32(); terrains[i] = new(name, color, Ids(r)); }
        if (r.ReadUInt32() != 0) throw new NotSupportedException("Unsupported AC terrain surface layout.");
        uint textureSize=r.ReadUInt32();
        AlphaMap[] Maps(){var maps=new AlphaMap[AcFormats.Count(r,10000)];for(int i=0;i<maps.Length;i++)maps[i]=new(r.ReadUInt32(),r.ReadUInt32());return maps;}
        var cornerMaps=Maps();var sideMaps=Maps();var roadMaps=Maps();
        count = AcFormats.Count(r, 256);
        var textureDescriptors=new TerrainTexture[count];
        for (int i = 0; i < count; i++)
        {
            uint type = r.ReadUInt32(), tex = r.ReadUInt32(), tiling = r.ReadUInt32(); AcFormats.Skip(r, 24);
            uint detailTiling=r.ReadUInt32(),detailTexture=r.ReadUInt32();textureDescriptors[i]=new(type,tex,tiling,detailTexture,detailTiling);
            if (type < terrains.Length) terrains[type] = terrains[type] with { Texture = tex, Tiling = tiling };
        }
        if ((mask & 0x200) != 0) AcFormats.Skip(r, 24);
        if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("AC region layout mismatch.");
        return new(heights, terrains, scenes,new(textureSize,cornerMaps,sideMaps,roadMaps,textureDescriptors));
    }
    public static Block ReadBlock(byte[] bytes)
    {
        using var r = AcFormats.Reader(bytes); uint id = r.ReadUInt32(); bool objects = r.ReadUInt32() != 0;
        if ((id & 0xffff) != 0xffff) throw new InvalidDataException("Not a landblock terrain record.");
        var terrain = new ushort[81]; for (int i = 0; i < 81; i++) terrain[i] = r.ReadUInt16();
        var heights = r.ReadBytes(81); if (heights.Length != 81) throw new EndOfStreamException();
        return new(id, terrain, heights, objects);
    }
    public static Placement[] ReadPlacements(byte[] bytes)
    {
        using var r = AcFormats.Reader(bytes); r.ReadUInt32(); r.ReadUInt32();
        int count = AcFormats.Count(r, 10000); var placements = new List<Placement>();
        for (int i = 0; i < count; i++) placements.Add(new(r.ReadUInt32(), AcFormats.ReadPose(r)));
        int buildings = r.ReadUInt16(); r.ReadUInt16();
        for (int i = 0; i < buildings; i++)
        {
            placements.Add(new(r.ReadUInt32(), AcFormats.ReadPose(r), IsBuilding: true)); r.ReadUInt32();
            int portals = AcFormats.Count(r, 10000);
            for (int j = 0; j < portals; j++) { AcFormats.Skip(r, 6); int stabs = r.ReadUInt16(); AcFormats.Skip(r, stabs * 2L); Align(r); }
        }
        return placements.ToArray();
    }
    public static SceneObject[] ReadScene(byte[] bytes)
    {
        using var r = AcFormats.Reader(bytes); r.ReadUInt32(); var result = new SceneObject[AcFormats.Count(r, 10000)];
        for (int i = 0; i < result.Length; i++)
        {
            uint id = r.ReadUInt32(); var pose = AcFormats.ReadPose(r);
            result[i] = new(id, pose, r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            AcFormats.Skip(r, 20);
        }
        return result;
    }
    public static float SampleHeight(Block block, Region region, float x, float y)
    {
        x = Math.Clamp(x / 24, 0, 8); y = Math.Clamp(y / 24, 0, 8);
        int ix = Math.Min(7,(int)x), iy = Math.Min(7,(int)y); float fx = x - ix, fy = y - iy;
        float H(int a, int b) => region.Heights[block.Heights[a * 9 + b]];
        // The same native split drives rendering and manual terrain collision.
        if(NorthEastCut(block.Id,ix,iy))return fy <= fx ? H(ix, iy) * (1 - fx) + H(ix + 1, iy) * (fx - fy) + H(ix + 1, iy + 1) * fy
            : H(ix, iy) * (1 - fy) + H(ix, iy + 1) * (fy - fx) + H(ix + 1, iy + 1) * fx;
        return fx+fy<=1?H(ix,iy)*(1-fx-fy)+H(ix+1,iy)*fx+H(ix,iy+1)*fy
            :H(ix+1,iy)*(1-fy)+H(ix+1,iy+1)*(fx+fy-1)+H(ix,iy+1)*(1-fx);
    }
    public static bool NorthEastCut(uint block,int cellX,int cellY)
    {
        uint x=(block>>24)*8+(uint)cellX,y=((block>>16)&255)*8+(uint)cellY;
        uint value=unchecked(x*y*0x0CCAC033u-x*0x421BE3BDu+y*0x6C1AC587u-0x519B8F25u);
        return (value&0x80000000u)!=0;
    }
    public static int TerrainAt(Block block, float x, float y) => (block.Terrain[Math.Clamp((int)(x / 24), 0, 8) * 9 + Math.Clamp((int)(y / 24), 0, 8)] >> 2) & 31;
}

