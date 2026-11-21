namespace DerethSurvivors;

// Native retail palette-code recipes, matched to the read-only DerethClient
// OpenAC TerrainBlending reference. Masks carry inverse overlay alpha.
// Adapted OpenAC portions are MIT licensed; see THIRD_PARTY_NOTICES.txt.
public static class AcTerrainBlend
{
    public readonly record struct Surface(byte BaseLayer,
        byte Ovl0Layer,byte Ovl0AlphaLayer,byte Ovl0Rotation,
        byte Ovl1Layer,byte Ovl1AlphaLayer,byte Ovl1Rotation,
        byte Ovl2Layer,byte Ovl2AlphaLayer,byte Ovl2Rotation,
        byte RoadLayer,byte Road0AlphaLayer,byte Road0Rotation,byte Road1AlphaLayer,byte Road1Rotation)
    {
        public const byte None=255;
        public static Surface BaseOnly(byte layer)=>new(layer,None,None,0,None,None,0,None,None,0,None,None,0,None,0);
    }
    public static uint GetPalCode(int r1,int r2,int r3,int r4,int t1,int t2,int t3,int t4)=>
        (1u<<28)|(uint)((r1<<26)|(r2<<24)|(r3<<22)|(r4<<20)|(t1<<15)|(t2<<10)|(t3<<5)|t4);
    public static Surface SurfaceFor(AcWorldData.Block block,AcWorldData.Region region,int x,int y)
    {
        var maps=region.Blend??throw new InvalidDataException("The native region has no terrain blend descriptors.");
        int a=block.Terrain[x*9+y],b=block.Terrain[(x+1)*9+y],c=block.Terrain[(x+1)*9+y+1],d=block.Terrain[x*9+y+1];
        return BuildSurface(GetPalCode(a&3,b&3,c&3,d&3,(a>>2)&31,(b>>2)&31,(c>>2)&31,(d>>2)&31),maps);
    }
    public static int PseudoRandomIndex(uint code,int count)
    {
        if(count<=0)return 0;
        int index=(int)Math.Floor(unchecked(1379576222u*code-1372186442u)*2.3283064e-10*count);
        return index>=count?0:index;
    }
    public static uint RotateCode(uint code){code*=2;return code>=16?code-15:code;}
    public static bool OnRoad(AcWorldData.Block block,float east,float north)
    {
        // DerethClient/OpenAC TerrainUtils.OnRoad: native five-metre scenery
        // exclusion around road corner/side/diagonal paths, not an entire tile.
        if(east<0||north<0||east>=192||north>=192)return false;
        int x=(int)(east/24),y=(int)(north/24);float dx=east-x*24,dy=north-y*24;
        int mask=0;if((block.Terrain[x*9+y]&3)!=0)mask|=1;if((block.Terrain[(x+1)*9+y]&3)!=0)mask|=2;
        if((block.Terrain[(x+1)*9+y+1]&3)!=0)mask|=4;if((block.Terrain[x*9+y+1]&3)!=0)mask|=8;
        return mask switch
        {
            0=>false,1=>dx+dy<5,2=>24-dx+dy<5,4=>48-dx-dy<5,8=>24+dx-dy<5,
            3=>dy<5,6=>dx>19,12=>dy>19,9=>dx<5,5=>Math.Abs(dx-dy)<5,10=>Math.Abs(dx+dy-24)<5,
            7=>dx>19||dy<5,11=>dx<5||dy<5,13=>dx<5||dy>19,14=>dx>19||dy>19,15=>true,_=>false
        };
    }
    public static (uint First,uint Second,bool AllRoad) RoadCodes(uint code)
    {
        uint mask=0;if((code&0x0C000000)!=0)mask|=1;if((code&0x03000000)!=0)mask|=2;if((code&0x00C00000)!=0)mask|=4;if((code&0x00300000)!=0)mask|=8;
        return mask switch{15=>(0,0,true),14=>(6,12,false),13=>(9,12,false),11=>(9,3,false),7=>(3,6,false),_=>(mask,0,false)};
    }
    private static (byte Mask,byte Rotation) FindAlpha(uint palette,uint code,AcWorldData.TerrainBlend blend,bool road)
    {
        bool corner=code is 1 or 2 or 4 or 8;
        var maps=road?blend.Road:corner?blend.Corner:blend.Side;
        int offset=road?blend.Corner.Length+blend.Side.Length:corner?0:blend.Corner.Length;
        if(maps.Length==0)throw new InvalidDataException("Missing native terrain alpha maps.");
        int first=PseudoRandomIndex(palette,maps.Length);
        for(int search=0;search<(road?maps.Length:1);search++)
        {
            int index=(first+search)%maps.Length;uint current=maps[index].Code;
            for(byte rotation=0;rotation<4;rotation++)
            {if(current==code)return(checked((byte)(offset+index)),rotation);current=RotateCode(current);}
        }
        throw new InvalidDataException($"No native {(road?"road":"terrain")} mask for corner code {code:X}.");
    }
    public static Surface BuildSurface(uint palette,AcWorldData.TerrainBlend blend)
    {
        byte Layer(uint type)=>blend.Textures.Any(t=>t.Type==type)?checked((byte)type):throw new InvalidDataException("Missing native terrain texture descriptor "+type);
        var roads=RoadCodes(palette);if(roads.AllRoad)return Surface.BaseOnly(Layer(32));
        uint[] types={(palette>>15)&31,(palette>>10)&31,(palette>>5)&31,palette&31};
        var layers=new[]{Surface.None,Surface.None,Surface.None,Surface.None};var codes=new uint[3];
        int duplicate=-1;
        for(int i=0;i<4&&duplicate<0;i++)for(int j=i+1;j<4;j++)if(types[i]==types[j]){duplicate=i;break;}
        if(duplicate<0)
        {for(int i=0;i<4;i++)layers[i]=Layer(types[i]);codes[0]=2;codes[1]=4;codes[2]=8;}
        else
        {
            uint primary=types[duplicate],secondary=0;bool found=false;layers[0]=Layer(primary);
            for(int k=0;k<4;k++)
            {
                if(types[k]==primary)continue;
                if(!found){found=true;secondary=types[k];layers[1]=Layer(secondary);codes[0]=1u<<k;}
                else{if(types[k]==secondary&&codes[0]==(1u<<(k-1)))codes[0]+=1u<<k;else{layers[2]=Layer(types[k]);codes[1]=1u<<k;}break;}
            }
        }
        var alpha=new[]{Surface.None,Surface.None,Surface.None};var rotation=new byte[3];
        for(int i=0;i<3;i++)if(codes[i]!=0){var match=FindAlpha(palette,codes[i],blend,false);alpha[i]=match.Mask;rotation[i]=match.Rotation;}
        byte roadLayer=Surface.None,road0=Surface.None,road1=Surface.None,turn0=0,turn1=0;
        if(roads.First!=0)
        {
            roadLayer=Layer(32);var match=FindAlpha(palette,roads.First,blend,true);road0=match.Mask;turn0=match.Rotation;
            if(roads.Second!=0){match=FindAlpha(palette,roads.Second,blend,true);road1=match.Mask;turn1=match.Rotation;}
        }
        return new(layers[0],layers[1],alpha[0],rotation[0],layers[2],alpha[1],rotation[1],layers[3],alpha[2],rotation[2],roadLayer,road0,turn0,road1,turn1);
    }
}
