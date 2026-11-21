using System.Text.Json.Serialization;

namespace DerethSurvivors;

// Persist only bounded native option indices. Setup/model/texture/palette IDs,
// body scale and animation tables are resolved from the host's read-only DATs.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CharacterAppearance
{
    public uint NativeHeritageId{get;set;}=1;
    public int GenderId{get;set;}=1;
    public int HairStyle{get;set;}
    public int HairColor{get;set;}
    public int EyeColor{get;set;}
    public int EyeStrip{get;set;}
    public int NoseStrip{get;set;}
    public int MouthStrip{get;set;}
    public float SkinShade{get;set;}=.5f;
    public float HairShade{get;set;}=.5f;
    public int HeadgearStyle{get;set;}
    public int ShirtStyle{get;set;}
    public int TrousersStyle{get;set;}
    public int FootwearStyle{get;set;}
    public int HeadgearColor{get;set;}
    public int ShirtColor{get;set;}
    public int TrousersColor{get;set;}
    public int FootwearColor{get;set;}
    public float HeadgearShade{get;set;}=.5f;
    public float ShirtShade{get;set;}=.5f;
    public float TrousersShade{get;set;}=.5f;
    public float FootwearShade{get;set;}=.5f;
    [JsonIgnore] public bool IsValid=>NativeHeritageId is >=1 and <=11&&GenderId is >=1 and <=2
        &&Index(HairStyle)&&Index(HairColor)&&Index(EyeColor)&&Index(EyeStrip)&&Index(NoseStrip)&&Index(MouthStrip)
        &&Index(HeadgearStyle)&&Index(ShirtStyle)&&Index(TrousersStyle)&&Index(FootwearStyle)
        &&Index(HeadgearColor)&&Index(ShirtColor)&&Index(TrousersColor)&&Index(FootwearColor)
        &&Shade(SkinShade)&&Shade(HairShade)&&Shade(HeadgearShade)&&Shade(ShirtShade)&&Shade(TrousersShade)&&Shade(FootwearShade);
    private static bool Index(int value)=>value is >=0 and <=255;
    private static bool Shade(float value)=>float.IsFinite(value)&&value is >=0 and <=1;
    public CharacterAppearance Copy()=>(CharacterAppearance)MemberwiseClone();
}

public readonly record struct CharacterPaletteChange(uint Palette,int Offset,int Count);
public readonly record struct CharacterTextureChange(int Part,uint OldTexture,uint NewTexture);
public readonly record struct CharacterPartChange(int Part,uint Model);
public sealed record CharacterObjectDescription(uint Palette,IReadOnlyList<CharacterPaletteChange> Palettes,IReadOnlyList<CharacterTextureChange> Textures,IReadOnlyList<CharacterPartChange> Parts)
{
    public static CharacterObjectDescription Empty{get;}=new(0,Array.Empty<CharacterPaletteChange>(),Array.Empty<CharacterTextureChange>(),Array.Empty<CharacterPartChange>());
}
public sealed record CharacterLookDefinition(uint NativeHeritageId,int GenderId,uint Setup,uint Motion,uint Sound,float Scale,uint BasePalette,CharacterObjectDescription Description,IReadOnlyList<string> Warnings)
{
    // Existing AcActor native look application accepts these packed ObjDesc
    // ranges. Keep conversion at the integration boundary, not in the save.
    public RareCatalog.Creature ToNativeAppearance()
    {
        var result=new RareCatalog.Creature{Id=0,Setup=Setup,Motion=Motion,Sound=Sound,Scale=Scale,Palette=BasePalette};
        static string N(uint value)=>value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        static string I(int value)=>value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result.Appearance["weenie_properties_palette"]=Description.Palettes.Select(p=>new[]{N(p.Palette),I(p.Offset/8),I(p.Count==2048?0:p.Count/8)}).ToArray();
        result.Appearance["weenie_properties_texture_map"]=Description.Textures.Select(p=>new[]{I(p.Part),N(p.OldTexture),N(p.NewTexture)}).ToArray();
        result.Appearance["weenie_properties_anim_part"]=Description.Parts.Select(p=>new[]{I(p.Part),N(p.Model)}).ToArray();
        return result;
    }
}
