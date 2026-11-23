using Godot;

namespace DerethSurvivors;

public sealed partial class AcAssets
{
    private static readonly uint[] PreparationCommands={0x41000003,0x44000007,0x45000005,0x40000011,
        0x10000063,0x10000062,0x10000064,0x10000058,0x1000005c,0x100000d0,0x100000d1,0x100000d2,
        0x4000002b,0x40000032,0x400000d3,0x40000031,0x4000002d,0x4000001e,0x40000016,
        0x2500003b,0x1000004b,0x1000004a,0x40000015};
    private static readonly uint[] PreparationSourceStates={0x41000003,0x44000007,0x4000001e,0x40000016};
    public AcAssetPreparation PrepareActorAssets(uint setup,uint motion,RareCatalog.Creature? appearance=null,
        IReadOnlyList<CombatCatalog.Weapon>? equipment=null,IReadOnlyList<NativeSpellPhysics.Profile>? projectiles=null,
        IReadOnlyList<uint>? casterEffects=null)
        =>new(this,PrepareActorCore(setup,motion,appearance,equipment,projectiles,casterEffects,new HashSet<uint>()).GetEnumerator());
    public AcAssetPreparation PrepareEffectAssets(uint script)
        =>new(this,PrepareEffectCore(script,new HashSet<uint>(),0).GetEnumerator());
    public AcAssetPreparation PrepareSpellAssets(uint effectsTable,IReadOnlyList<uint> effects,IReadOnlyList<NativeSpellPhysics.Profile>? projectiles=null)
        =>new(this,PrepareSpellCore(effectsTable,effects,projectiles,new HashSet<uint>()).GetEnumerator());
    private IEnumerable<string> PrepareSpellCore(uint table,IReadOnlyList<uint> effects,IReadOnlyList<NativeSpellPhysics.Profile>? projectiles,HashSet<uint> scriptsSeen)
    {
        if(table!=0)foreach(uint effect in effects.Distinct())
        {
            if(effect==0)continue;uint script=EffectScript(table,effect);yield return "effect-table";
            if(script!=0)foreach(string work in PrepareEffectCore(script,scriptsSeen,0))yield return work;
        }
        if(projectiles is not null)foreach(var profile in projectiles)
            foreach(string work in PrepareProjectileCore(profile,scriptsSeen))yield return work;
    }
    private IEnumerable<string> PrepareProjectileCore(NativeSpellPhysics.Profile profile,HashSet<uint> scriptsSeen)
    {
        foreach(string work in PrepareActorCore(profile.Setup,0,null,null,null,null,scriptsSeen))yield return work;
        foreach(uint effect in new uint[]{4,5})
        {
            uint script=EffectScript(profile.Effects,effect);yield return "effect-table";
            if(script!=0)foreach(string work in PrepareEffectCore(script,scriptsSeen,0))yield return work;
        }
        NativeLaunchScale(profile.Effects,profile.Scale);yield return "launch-scale";
    }

    private IEnumerable<string> PrepareActorCore(uint setupId,uint motionId,RareCatalog.Creature? appearance,
        IReadOnlyList<CombatCatalog.Weapon>? equipment,IReadOnlyList<NativeSpellPhysics.Profile>? projectiles,
        IReadOnlyList<uint>? casterEffects,HashSet<uint> scriptsSeen)
    {
        var setup=Setup(setupId);yield return "setup";
        foreach(uint model in setup.Parts.Distinct())if(model!=0)
            foreach(string work in PrepareMeshCore(model,false))yield return work;
        uint motion=motionId!=0?motionId:setup.MotionTable;
        CombatCatalog.Weapon? held=null;
        if(equipment is not null)foreach(var item in equipment)
            if(PreparationEquipmentAvailable(item)&&item.Kind is "sword" or "mace" or "axe" or "dagger" or "bow" or "crossbow" or "thrown" or "caster")
            {held=item;break;}
        if(motion!=0)
        {
            var table=Motion(motion);yield return "motion";
            var styles=new HashSet<uint>{table.Style};
            uint creatureStyle=table.StyleDefaults.ContainsKey(0x8000003c)?0x8000003cu:table.Style;styles.Add(creatureStyle);
            if(motion==0x09000001)styles.Add(AcActor.MagicStyle);
            if(held is not null)
            {
                uint wanted=PreparationWeaponStyle(held);
                styles.Add(table.StyleDefaults.ContainsKey(wanted)?wanted:creatureStyle);
            }
            var animationIds=new HashSet<uint>();
            if(setup.DefaultAnimation!=0)animationIds.Add(setup.DefaultAnimation);
            foreach(uint style in styles)foreach(uint command in PreparationCommands)foreach(uint from in PreparationSourceStates)
            foreach(bool once in new[]{false,true})foreach(var clip in Clips(motion,command,once,style,from))animationIds.Add(clip.Animation);
            foreach(uint animationId in animationIds)
            {
                var animation=Animation(animationId);yield return "animation";
                foreach(var frame in animation.Frames)foreach(var hook in frame.Hooks)
                    foreach(string work in PrepareHookCore(hook,scriptsSeen,0))yield return work;
            }
        }
        else if(setup.DefaultAnimation!=0){Animation(setup.DefaultAnimation);yield return "animation";}
        if(appearance is not null)
        {
            foreach(string work in PrepareAppearancePaletteCore(appearance))yield return work;
            var colors=NativePreparationColors(appearance);var parts=NativePreparationParts(setup,appearance);
            string signature="native/"+ColorSignature(colors);
            foreach(var part in parts)if(part.Model!=0)
                foreach(string work in PrepareWearMeshCore(part,colors,signature,appearance.Palette))yield return work;
            if(equipment is not null)foreach(var item in equipment)
            {
                if(item.Clothing==0||!PreparationEquipmentAvailable(item))continue;
                var worn=PreparationWeaponAppearance(item);
                foreach(string work in PrepareAppearancePaletteCore(worn))yield return work;
                var clothing=Clothing(item.Clothing);
                if(!clothing.Setups.TryGetValue(setup.Id,out var changes))continue;
                var wornColors=new Dictionary<int,uint>(colors);
                foreach(var color in ClothingColors(clothing,item.PaletteTemplate,item.Shade))wornColors[color.Key]=color.Value;
                string wornSignature="native-equipment/"+ColorSignature(wornColors);
                foreach(var change in changes)if(change.Part>=0&&change.Part<setup.Parts.Length&&change.Model!=0)
                    foreach(string work in PrepareWearMeshCore(change,wornColors,wornSignature,item.Palette!=0?item.Palette:appearance.Palette))yield return work;
            }
        }
        if(held is not null)
        {
            foreach(string work in PrepareActorCore(held.Setup,0,PreparationWeaponAppearance(held),null,null,null,scriptsSeen))yield return work;
            if(held.AmmoSetup!=0)foreach(string work in PrepareActorCore(held.AmmoSetup,0,null,null,null,null,scriptsSeen))yield return work;
        }
        if(setup.Script!=0)foreach(string work in PrepareEffectCore(setup.Script,scriptsSeen,0))yield return work;
        if(appearance is {Effects:>0})
        {
            uint hit=EffectScript(appearance.Effects,32);yield return "effect-table";
            if(hit!=0)foreach(string work in PrepareEffectCore(hit,scriptsSeen,0))yield return work;
            if(casterEffects is not null)foreach(uint effect in casterEffects)
            {
                uint script=EffectScript(appearance.Effects,effect);yield return "effect-table";
                if(script!=0)foreach(string work in PrepareEffectCore(script,scriptsSeen,0))yield return work;
            }
        }
        if(projectiles is not null)foreach(var profile in projectiles)
            foreach(string work in PrepareProjectileCore(profile,scriptsSeen))yield return work;
    }
    private bool PreparationEquipmentAvailable(CombatCatalog.Weapon item)=>Archive.Entries.ContainsKey(item.Setup)
        &&(item.Clothing==0||Archive.Entries.ContainsKey(item.Clothing))&&(item.Palette==0||Archive.Entries.ContainsKey(item.Palette));
    private static uint PreparationWeaponStyle(CombatCatalog.Weapon item)=>item.NativeCombatStyle==1?0x8000003cu
        :item.NativeCombatStyle==1024?0x8000013bu:!item.TwoHanded&&item.NativeCombatStyle==2?0x8000003eu
        :item.TwoHanded&&item.Kind is "axe" or "mace"?0x80000044u:item.Kind switch{
            "bow"=>0x8000003fu,"crossbow"=>0x80000041u,"thrown"=>0x80000047u,"axe"=>0x80000044u,"caster"=>AcActor.MagicStyle,_=>0x8000003eu};
    private static RareCatalog.Creature PreparationWeaponAppearance(CombatCatalog.Weapon item)=>new(){
        Id=item.Id,Setup=item.Setup,Clothing=item.Clothing,Palette=item.Palette,PaletteTemplate=item.PaletteTemplate,Shade=item.Shade};
    private IEnumerable<string> PrepareAppearancePaletteCore(RareCatalog.Creature appearance)
    {
        if(appearance.Clothing!=0)
        {
            var cloth=Clothing(appearance.Clothing);yield return "clothing";
            if(cloth.Colors.TryGetValue(appearance.PaletteTemplate,out var variant))foreach(var remap in variant.Palettes)
            {
                var set=Palette(remap.Set);yield return "palette-set";
                if(set.Length>0){Palette(set[Math.Clamp((int)(appearance.Shade*(set.Length-.000001f)),0,set.Length-1)]);yield return "palette";}
            }
        }
        if(appearance.Appearance.TryGetValue("weenie_properties_palette",out var ranges))foreach(var range in ranges)
        {Palette(uint.Parse(range[0],System.Globalization.CultureInfo.InvariantCulture));yield return "palette";}
    }
    private Dictionary<int,uint> NativePreparationColors(RareCatalog.Creature source)
    {
        var colors=source.Clothing!=0?ClothingColors(Clothing(source.Clothing),source.PaletteTemplate,source.Shade):new Dictionary<int,uint>();
        if(source.Appearance.TryGetValue("weenie_properties_palette",out var ranges))foreach(var range in ranges)
        {
            uint palette=uint.Parse(range[0],System.Globalization.CultureInfo.InvariantCulture);
            int offset=checked((int)uint.Parse(range[1],System.Globalization.CultureInfo.InvariantCulture)*8);
            uint raw=uint.Parse(range[2],System.Globalization.CultureInfo.InvariantCulture);int count=checked((raw==0?256:(int)raw)*8);
            foreach(var color in PaletteColors(palette,offset,count))colors[color.Key]=color.Value;
        }
        return colors;
    }
    private AcFormats.ClothingPart[] NativePreparationParts(AcFormats.Setup setup,RareCatalog.Creature source)
    {
        var changes=new Dictionary<int,AcFormats.ClothingPart>();
        if(source.Clothing!=0&&Clothing(source.Clothing).Setups.TryGetValue(setup.Id,out var clothingParts))
            foreach(var part in clothingParts)if(part.Part>=0&&part.Part<setup.Parts.Length)changes[part.Part]=part;
        var result=new AcFormats.ClothingPart[setup.Parts.Length];
        for(int i=0;i<result.Length;i++)
        {
            uint model=setup.Parts[i];var textures=new Dictionary<uint,uint>();
            if(changes.TryGetValue(i,out var change)){model=change.Model;foreach(var pair in change.Textures)textures[pair.Key]=pair.Value;}
            if(source.Appearance.TryGetValue("weenie_properties_anim_part",out var models))foreach(var row in models)
                if(uint.Parse(row[0],System.Globalization.CultureInfo.InvariantCulture)==i)model=uint.Parse(row[1],System.Globalization.CultureInfo.InvariantCulture);
            if(source.Appearance.TryGetValue("weenie_properties_texture_map",out var maps))foreach(var row in maps)
                if(uint.Parse(row[0],System.Globalization.CultureInfo.InvariantCulture)==i)textures[uint.Parse(row[1],System.Globalization.CultureInfo.InvariantCulture)]=uint.Parse(row[2],System.Globalization.CultureInfo.InvariantCulture);
            result[i]=new(i,model,textures);
        }
        return result;
    }
    private IEnumerable<string> PrepareEffectCore(uint scriptId,HashSet<uint> seen,int depth)
    {
        if(scriptId==0||depth>8||seen.Count>=256||!seen.Add(scriptId))yield break;
        var events=Script(scriptId);yield return "script";
        foreach(var entry in events)foreach(string work in PrepareHookCore(entry.Hook,seen,depth))yield return work;
    }
    private IEnumerable<string> PrepareHookCore(AcFormats.Hook hook,HashSet<uint> scriptsSeen,int depth)
    {
        if(hook.Type is 13 or 26)
        {
            var emitter=Emitter(hook.Asset);yield return "emitter";
            uint mesh=emitter.HardwareMesh!=0?emitter.HardwareMesh:emitter.Mesh;
            if(mesh!=0)foreach(string work in PrepareMeshCore(mesh,true))yield return work;
        }
        else if(hook.Type==19&&hook.Asset>>24==0x33)
            foreach(string work in PrepareEffectCore(hook.Asset,scriptsSeen,depth+1))yield return work;
    }
    private IEnumerable<string> PrepareMeshCore(uint id,bool particle)
    {
        var key=(id,particle);TouchAsset(AcAssetCacheKind.Mesh,key);
        if(particle){ParticleFacing(id);yield return "particle-facing";}
        if(meshes.ContainsKey(key)){yield return "mesh-cache";yield break;}
        var source=Geometry(id);yield return "geometry";
        bool billboard=particle&&ParticleFacing(id).Billboard;
        ArrayMesh? mesh=new();bool published=false;
        try
        {
            foreach(var group in source.Polygons.Where(p=>p.Surface>=0&&p.Surface<source.Surfaces.Length&&p.Vertices.Length>=3).GroupBy(p=>p.Surface))
            {
                var material=Surface(source.Surfaces[group.Key],particle,billboard);yield return "material/texture";
                int triangles=group.Sum(p=>p.Vertices.Length-2),cursor=0,sinceYield=0;
                var positions=new Vector3[checked(triangles*3)];var normals=new Vector3[positions.Length];var uvs=new Vector2[positions.Length];
                foreach(var polygon in group)for(int i=1;i<polygon.Vertices.Length-1;i++)
                {
                    for(int corner=0;corner<3;corner++)
                    {
                        int index=corner==0?0:corner==1?i:i+1;var vertex=source.Vertices[polygon.Vertices[index]];
                        positions[cursor]=Convert(vertex.Position);normals[cursor]=Convert(vertex.Normal);
                        var uv=vertex.Uvs.Length>polygon.Uvs[index]?vertex.Uvs[polygon.Uvs[index]]:System.Numerics.Vector2.Zero;
                        uvs[cursor++]=new(uv.X,uv.Y);
                    }
                    if(++sinceYield==256){sinceYield=0;yield return "mesh-vertices";}
                }
                var arrays=new Godot.Collections.Array();arrays.Resize((int)Godot.Mesh.ArrayType.Max);
                arrays[(int)Godot.Mesh.ArrayType.Vertex]=positions;arrays[(int)Godot.Mesh.ArrayType.Normal]=normals;arrays[(int)Godot.Mesh.ArrayType.TexUV]=uvs;
                mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles,arrays);mesh.SurfaceSetMaterial(mesh.GetSurfaceCount()-1,material);
                Triangles+=triangles;yield return "mesh-upload";
            }
            // Another urgent plan can finish this shared dependency while this
            // iterator is suspended. Keep that canonical resource identity.
            if(meshes.ContainsKey(key)){TouchAsset(AcAssetCacheKind.Mesh,key);yield return "mesh-cache";yield break;}
            meshes[key]=mesh;TouchAsset(AcAssetCacheKind.Mesh,key);MeshBuilds++;published=true;yield return "mesh-ready";
        }
        finally{if(!published)mesh.Dispose();}
    }
    private IEnumerable<string> PrepareWearMeshCore(AcFormats.ClothingPart part,Dictionary<int,uint> colors,string signature,uint fallback)
    {
        string key=WearMeshKey(part,signature,fallback);TouchAsset(AcAssetCacheKind.WornMesh,key);
        if(wornMeshes.ContainsKey(key)){yield return "appearance-cache";yield break;}
        foreach(string work in PrepareMeshCore(part.Model,false))yield return work;
        var ids=GeometrySurfaceIds(Geometry(part.Model));var mesh=(ArrayMesh)Mesh(part.Model).Duplicate();bool published=false;
        string canonical=CanonicalColorSignature(signature,colors);
        try
        {
            yield return "appearance-mesh";
            for(int i=0;i<mesh.GetSurfaceCount();i++)
            {ApplyWornSurface(mesh,i,ids[i],part,colors,canonical,fallback);yield return "appearance-surface";}
            if(wornMeshes.ContainsKey(key)){TouchAsset(AcAssetCacheKind.WornMesh,key);yield return "appearance-cache";yield break;}
            wornMeshes[key]=mesh;TouchAsset(AcAssetCacheKind.WornMesh,key);WornMeshBuilds++;published=true;yield return "appearance-ready";
        }
        finally{if(!published)mesh.Dispose();}
    }
}
