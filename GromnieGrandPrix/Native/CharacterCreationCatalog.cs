using System.Text;

namespace DerethSurvivors;

// Native 0E000002 CharGen layout and composition follow DerethClient/OpenAC's
// ChargenTableReader + ChargenAppearanceFactory. No archive writes or external
// parser dependency: the existing read-only archive owns the underlying bytes.
public sealed class CharacterCreationCatalog
{
    public const uint TableId=0x0E000002;
    internal const uint OlthoiSpitterHeritageId=13;
    public sealed record HairStyle(uint Icon,bool Bald,uint AlternateSetup,CharacterObjectDescription Description);
    public sealed record EyeStrip(uint Icon,uint BaldIcon,CharacterObjectDescription Description,CharacterObjectDescription BaldDescription);
    public sealed record FaceStrip(uint Icon,CharacterObjectDescription Description);
    public sealed record GearOption(string Name,uint ClothingTable,uint WeenieDefault);
    public sealed record SkillCost(uint Id,int NormalCost,int SpecializedCost);
    public sealed record Template(string Name,uint Icon,uint Title,IReadOnlyDictionary<string,int> Attributes,IReadOnlyList<uint> TrainedSkills,IReadOnlyList<uint> SpecializedSkills);
    public sealed record StartingArea(string Name,int LocationCount);
    public sealed class Gender
    {
        public int Id{get;init;}public string Name{get;init;}="";public float Scale{get;init;}=1;
        public uint Setup{get;init;}public uint Motion{get;init;}public uint Sound{get;init;}public uint Icon{get;init;}public uint BasePalette{get;init;}public uint SkinPalSet{get;init;}
        public CharacterObjectDescription Description{get;init;}=CharacterObjectDescription.Empty;
        public IReadOnlyList<uint> HairColors{get;init;}=Array.Empty<uint>();public IReadOnlyList<HairStyle> HairStyles{get;init;}=Array.Empty<HairStyle>();
        public IReadOnlyList<uint> EyeColors{get;init;}=Array.Empty<uint>();public IReadOnlyList<EyeStrip> EyeStrips{get;init;}=Array.Empty<EyeStrip>();
        public IReadOnlyList<FaceStrip> NoseStrips{get;init;}=Array.Empty<FaceStrip>();public IReadOnlyList<FaceStrip> MouthStrips{get;init;}=Array.Empty<FaceStrip>();
        public IReadOnlyList<GearOption> Headgears{get;init;}=Array.Empty<GearOption>();public IReadOnlyList<GearOption> Shirts{get;init;}=Array.Empty<GearOption>();
        public IReadOnlyList<GearOption> Pants{get;init;}=Array.Empty<GearOption>();public IReadOnlyList<GearOption> Footwear{get;init;}=Array.Empty<GearOption>();
        public IReadOnlyList<uint> ClothingColors{get;init;}=Array.Empty<uint>();
    }
    public sealed class Heritage
    {
        public uint Id{get;init;}public string Name{get;init;}="";public uint Icon{get;init;}public uint Setup{get;init;}public uint EnvironmentSetup{get;init;}
        public uint AttributeCredits{get;init;}public uint SkillCredits{get;init;}
        public IReadOnlyList<int> PrimaryStartAreas{get;init;}=Array.Empty<int>();public IReadOnlyList<int> SecondaryStartAreas{get;init;}=Array.Empty<int>();
        public IReadOnlyList<SkillCost> SkillCosts{get;init;}=Array.Empty<SkillCost>();public IReadOnlyList<Template> Templates{get;init;}=Array.Empty<Template>();
        public IReadOnlyList<Gender> Genders{get;init;}=Array.Empty<Gender>();
    }
    private readonly DatArchive archive;
    private readonly Dictionary<uint,uint[]> palettes=new();
    private readonly Dictionary<uint,AcFormats.Clothing> clothing=new();
    private readonly Dictionary<uint,Heritage> byId;
    public static CharacterCreationCatalog? Loaded{get;private set;}
    public IReadOnlyList<Heritage> Rows{get;}
    public IReadOnlyList<StartingArea> StartingAreas{get;}
    public int NativeHeritageCount{get;}
    internal Gender? OlthoiSpitter{get;}
    private CharacterCreationCatalog(DatArchive source,Heritage[] rows,StartingArea[] startingAreas,int nativeCount,Gender? spitter)
    {archive=source;Rows=Array.AsReadOnly(rows);StartingAreas=Array.AsReadOnly(startingAreas);NativeHeritageCount=nativeCount;OlthoiSpitter=spitter;byId=rows.ToDictionary(r=>r.Id);}
    public Heritage? Find(uint id)=>byId.GetValueOrDefault(id);
    public Gender? FindGender(CharacterAppearance appearance)=>Find(appearance.NativeHeritageId)?.Genders.FirstOrDefault(g=>g.Id==appearance.GenderId);
    public static CharacterCreationCatalog Load(DatArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);using var r=new BinaryReader(new MemoryStream(archive.Read(TableId),false),Encoding.UTF8);
        if(r.ReadUInt32()!=TableId)throw new InvalidDataException("Native character-generation table identity mismatch.");
        r.ReadUInt32();var areas=ReadArray(r,reader=>{string name=Text(reader);int count=Count(reader,256);Skip(reader,count*32L);return new StartingArea(name,count);},256);
        r.ReadByte();int heritageCount=Count(r,64);var rows=new List<Heritage>(heritageCount);Gender? spitter=null;
        for(int i=0;i<heritageCount;i++)
        {
            uint id=r.ReadUInt32();var row=ReadHeritage(r,id);
            if(id==OlthoiSpitterHeritageId)spitter=row.Genders.FirstOrDefault();
            if(id is 12 or OlthoiSpitterHeritageId||row.Name.Contains("Olthoi",StringComparison.OrdinalIgnoreCase))continue;
            if(id is <1 or >11||row.Genders.Count==0)throw new InvalidDataException("Unsupported native playable heritage row.");
            rows.Add(row);
        }
        if(rows.Count==0||rows.Select(row=>row.Id).Distinct().Count()!=rows.Count)throw new InvalidDataException("Native character catalog contains missing or duplicate heritages.");
        if(r.BaseStream.Position>r.BaseStream.Length)throw new InvalidDataException("Truncated native character-generation table.");
        var result=new CharacterCreationCatalog(archive,rows.OrderBy(row=>row.Id).ToArray(),areas,heritageCount,spitter);Loaded=result;return result;
    }
    private static Heritage ReadHeritage(BinaryReader r,uint id)
    {
        string name=Text(r);uint icon=r.ReadUInt32(),setup=r.ReadUInt32(),environment=r.ReadUInt32(),attributes=r.ReadUInt32(),skills=r.ReadUInt32();
        var primary=ReadArray(r,q=>q.ReadInt32(),256);var secondary=ReadArray(r,q=>q.ReadInt32(),256);
        var skillCosts=ReadArray(r,q=>new SkillCost(q.ReadUInt32(),q.ReadInt32(),q.ReadInt32()),256);
        var templates=ReadArray(r,ReadTemplate,256);r.ReadByte();int count=Count(r,4);var genders=new Gender[count];
        for(int i=0;i<count;i++)genders[i]=ReadGender(r,r.ReadInt32());
        return new(){Id=id,Name=name,Icon=icon,Setup=setup,EnvironmentSetup=environment,AttributeCredits=attributes,SkillCredits=skills,
            PrimaryStartAreas=primary,SecondaryStartAreas=secondary,SkillCosts=skillCosts,Templates=templates,Genders=Array.AsReadOnly(genders.OrderBy(g=>g.Id).ToArray())};
    }
    private static Template ReadTemplate(BinaryReader r)
    {
        string name=Text(r);uint icon=r.ReadUInt32(),title=r.ReadUInt32();var attributes=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(string attribute in new[]{"Strength","Endurance","Coordination","Quickness","Focus","Self"})attributes[attribute]=checked((int)r.ReadUInt32());
        return new(name,icon,title,attributes,ReadArray(r,q=>q.ReadUInt32(),256),ReadArray(r,q=>q.ReadUInt32(),256));
    }
    private static Gender ReadGender(BinaryReader r,int id)
    {
        string name=Text(r);uint scale=r.ReadUInt32(),setup=r.ReadUInt32(),sound=r.ReadUInt32(),icon=r.ReadUInt32(),palette=r.ReadUInt32(),skin=r.ReadUInt32();
        r.ReadUInt32();uint motion=r.ReadUInt32();r.ReadUInt32();var description=ReadDescription(r);
        var hairColors=ReadArray(r,q=>q.ReadUInt32(),256);
        var hairStyles=ReadArray(r,q=>new HairStyle(q.ReadUInt32(),q.ReadByte()==1,q.ReadUInt32(),ReadDescription(q)),256);
        var eyeColors=ReadArray(r,q=>q.ReadUInt32(),256);
        var eyeStrips=ReadArray(r,q=>new EyeStrip(q.ReadUInt32(),q.ReadUInt32(),ReadDescription(q),ReadDescription(q)),256);
        var nose=ReadArray(r,q=>new FaceStrip(q.ReadUInt32(),ReadDescription(q)),256);var mouth=ReadArray(r,q=>new FaceStrip(q.ReadUInt32(),ReadDescription(q)),256);
        GearOption Gear(BinaryReader q)=>new(Text(q),q.ReadUInt32(),q.ReadUInt32());
        var headgears=ReadArray(r,Gear,256);var shirts=ReadArray(r,Gear,256);var pants=ReadArray(r,Gear,256);var footwear=ReadArray(r,Gear,256);var colors=ReadArray(r,q=>q.ReadUInt32(),256);
        if(id is <1 or >2||setup>>24!=2||motion>>24!=9||scale is <10 or >500)throw new InvalidDataException("Invalid native character body row.");
        return new(){Id=id,Name=name,Scale=scale/100f,Setup=setup,Motion=motion,Sound=sound,Icon=icon,BasePalette=palette,SkinPalSet=skin,Description=description,
            HairColors=hairColors,HairStyles=hairStyles,EyeColors=eyeColors,EyeStrips=eyeStrips,NoseStrips=nose,MouthStrips=mouth,Headgears=headgears,Shirts=shirts,Pants=pants,Footwear=footwear,ClothingColors=colors};
    }
    private static CharacterObjectDescription ReadDescription(BinaryReader r)
    {
        Align(r);r.ReadByte();int paletteCount=r.ReadByte(),textureCount=r.ReadByte(),partCount=r.ReadByte();uint palette=paletteCount>0?DataId(r,0x04000000):0;
        var palettes=new CharacterPaletteChange[paletteCount];for(int i=0;i<paletteCount;i++){uint value=DataId(r,0x04000000);int offset=r.ReadByte()*8,count=r.ReadByte();palettes[i]=new(value,offset,(count==0?256:count)*8);}
        var textures=new CharacterTextureChange[textureCount];for(int i=0;i<textureCount;i++)textures[i]=new(r.ReadByte(),DataId(r,0x05000000),DataId(r,0x05000000));
        var parts=new CharacterPartChange[partCount];for(int i=0;i<partCount;i++)parts[i]=new(r.ReadByte(),DataId(r,0x01000000));
        Align(r);return new(palette,Array.AsReadOnly(palettes),Array.AsReadOnly(textures),Array.AsReadOnly(parts));
    }
    private static uint DataId(BinaryReader r,uint type)
    {uint value=r.ReadUInt16();return type+((value&0x8000)!=0?((value&0x3fff)<<16)|r.ReadUInt16():value);}
    private static int Count(BinaryReader r,int maximum)
    {
        uint first=r.ReadByte(),value=first;
        if((first&0x80)!=0){uint second=r.ReadByte();value=(first&0x40)==0?((first&0x7f)<<8)|second:((((first&0x3f)<<8)|second)<<16)|r.ReadUInt16();}
        if(value>maximum)throw new InvalidDataException("Native character table array exceeds its bound.");return(int)value;
    }
    private static T[] ReadArray<T>(BinaryReader r,Func<BinaryReader,T> read,int maximum)
    {int count=Count(r,maximum);var rows=new T[count];for(int i=0;i<count;i++)rows[i]=read(r);return rows;}
    private static string Text(BinaryReader r)
    {int count=r.Read7BitEncodedInt();if(count is <0 or >4096)throw new InvalidDataException("Invalid native character text length.");byte[] bytes=r.ReadBytes(count);if(bytes.Length!=count)throw new EndOfStreamException();return Encoding.UTF8.GetString(bytes);}
    private static void Align(BinaryReader r)=>Skip(r,((r.BaseStream.Position+3)&~3L)-r.BaseStream.Position);
    private static void Skip(BinaryReader r,long count)
    {if(count<0||count>r.BaseStream.Length-r.BaseStream.Position)throw new EndOfStreamException();r.BaseStream.Position+=count;}
    private static bool Choice(int index,int count)=>count==0?index==0:index>=0&&index<count;
    public bool Validate(CharacterAppearance? appearance,out string reason)
    {
        if(appearance is null||!appearance.IsValid){reason="Invalid native appearance values.";return false;}
        var gender=FindGender(appearance);if(gender is null){reason="That native heritage/body combination is unavailable.";return false;}
        bool valid=Choice(appearance.HairStyle,gender.HairStyles.Count)&&Choice(appearance.HairColor,gender.HairColors.Count)&&Choice(appearance.EyeColor,gender.EyeColors.Count)
            &&Choice(appearance.EyeStrip,gender.EyeStrips.Count)&&Choice(appearance.NoseStrip,gender.NoseStrips.Count)&&Choice(appearance.MouthStrip,gender.MouthStrips.Count)
            &&Choice(appearance.HeadgearStyle,gender.Headgears.Count)&&Choice(appearance.ShirtStyle,gender.Shirts.Count)&&Choice(appearance.TrousersStyle,gender.Pants.Count)&&Choice(appearance.FootwearStyle,gender.Footwear.Count)
            &&Choice(appearance.HeadgearColor,gender.ClothingColors.Count)&&Choice(appearance.ShirtColor,gender.ClothingColors.Count)&&Choice(appearance.TrousersColor,gender.ClothingColors.Count)&&Choice(appearance.FootwearColor,gender.ClothingColors.Count);
        reason=valid?"":"Appearance option is outside the selected native body's catalog.";return valid;
    }
    public CharacterLookDefinition Resolve(CharacterAppearance appearance)
    {
        if(!Validate(appearance,out string reason))throw new InvalidDataException(reason);var gender=FindGender(appearance)!;
        var palettes=new List<CharacterPaletteChange>();var textures=new List<CharacterTextureChange>();var parts=new List<CharacterPartChange>();var warnings=new List<string>();
        void Append(CharacterObjectDescription value){palettes.AddRange(value.Palettes);textures.AddRange(value.Textures);parts.AddRange(value.Parts);}
        Append(gender.Description);HairStyle? hair=gender.HairStyles.Count>0?gender.HairStyles[appearance.HairStyle]:null;uint setup=gender.Setup;
        if(hair is not null){if(hair.AlternateSetup is not(0 or uint.MaxValue))setup=hair.AlternateSetup;Append(hair.Description);}
        void Clothing(IReadOnlyList<GearOption> choices,int style,int color,float shade)
        {
            if(choices.Count==0||choices[style].ClothingTable==0)return;uint id=choices[style].ClothingTable;
            if(!clothing.TryGetValue(id,out var table))clothing[id]=table=AcFormats.ReadClothing(archive.Read(id));
            if(table.Setups.TryGetValue(setup,out var objects))foreach(var obj in objects)
            {parts.Add(new(obj.Part,obj.Model));foreach(var pair in obj.Textures)textures.Add(new(obj.Part,pair.Key,pair.Value));}
            else warnings.Add($"Native clothing {id:X8} has no binding for body {setup:X8}.");
            if(gender.ClothingColors.Count==0||!table.Colors.TryGetValue(gender.ClothingColors[color],out var template))return;
            foreach(var choice in template.Palettes)
            {
                uint palette=SelectPalette(choice.Set,shade);if(palette==0)continue;
                foreach(var range in choice.Ranges)palettes.Add(new(palette,range.Offset,range.Count));
            }
        }
        Clothing(gender.Headgears,appearance.HeadgearStyle,appearance.HeadgearColor,appearance.HeadgearShade);
        Clothing(gender.Pants,appearance.TrousersStyle,appearance.TrousersColor,appearance.TrousersShade);
        Clothing(gender.Shirts,appearance.ShirtStyle,appearance.ShirtColor,appearance.ShirtShade);
        Clothing(gender.Footwear,appearance.FootwearStyle,appearance.FootwearColor,appearance.FootwearShade);
        if(gender.EyeStrips.Count>0){var strip=gender.EyeStrips[appearance.EyeStrip];Append(hair?.Bald==true?strip.BaldDescription:strip.Description);}
        if(gender.NoseStrips.Count>0)Append(gender.NoseStrips[appearance.NoseStrip].Description);
        if(gender.MouthStrips.Count>0)Append(gender.MouthStrips[appearance.MouthStrip].Description);
        uint skin=SelectPalette(gender.SkinPalSet,appearance.SkinShade);if(skin!=0)palettes.Add(new(skin,0,192));
        if(gender.HairColors.Count>0){uint hairPalette=SelectPalette(gender.HairColors[appearance.HairColor],appearance.HairShade);if(hairPalette!=0)palettes.Add(new(hairPalette,192,64));}
        if(gender.EyeColors.Count>0)palettes.Add(new(gender.EyeColors[appearance.EyeColor],256,64));
        if(!archive.Entries.ContainsKey(setup))throw new InvalidDataException($"Native character body {setup:X8} is missing.");
        return new(appearance.NativeHeritageId,appearance.GenderId,setup,gender.Motion,gender.Sound,gender.Scale,gender.BasePalette,
            new(gender.BasePalette,palettes.AsReadOnly(),textures.AsReadOnly(),parts.AsReadOnly()),warnings.AsReadOnly());
    }
    private uint SelectPalette(uint id,float shade)
    {
        if(id==0||id==uint.MaxValue)return 0;if(id>>24==4)return id;
        if(!palettes.TryGetValue(id,out var values))
        {
            using var r=new BinaryReader(new MemoryStream(archive.Read(id),false));if(r.ReadUInt32()!=id)throw new InvalidDataException("Native palette-set identity mismatch.");
            uint count=r.ReadUInt32();if(count>4096)throw new InvalidDataException("Native palette set exceeds its bound.");values=new uint[count];for(int i=0;i<values.Length;i++)values[i]=r.ReadUInt32();palettes[id]=values;
        }
        return values.Length==0?0:values[Math.Clamp((int)((values.Length-.000001)*shade),0,values.Length-1)];
    }
}
