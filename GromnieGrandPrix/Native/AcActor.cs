using Godot;

namespace DerethSurvivors;

public partial class AcActor : Node3D
{
    private AcAssets assets = null!;
    public AcFormats.Setup Setup { get; private set; } = null!;
    public IReadOnlyList<SceneMovement.BodySphere> MovementBody{get;private set;}=Array.Empty<SceneMovement.BodySphere>();
    private RareCatalog.Creature? characterAppearance;
    public void SetCharacterAppearance(CharacterLookDefinition look)
    {
        if(look.Setup!=Setup.Id)throw new InvalidDataException("Character appearance does not match the native setup.");
        characterAppearance=look.ToNativeAppearance();SoundTable=look.Sound;
        ApplyNativeAppearance(characterAppearance);
        CacheMovementBody();
    }
    public void CacheMovementBody()
    {
        float scale=Scale.Y;
        MovementBody=Setup.Spheres.Select(s=>new SceneMovement.BodySphere(new(s.Center.X*scale,s.Center.Z*scale,-s.Center.Y*scale),s.Radius*scale)).ToArray();
        if(MovementBody.Count==0)MovementBody=new[]{new SceneMovement.BodySphere(new(0,Setup.Height*scale*.25f,0),Setup.Radius*scale),new SceneMovement.BodySphere(new(0,Setup.Height*scale*.75f,0),Setup.Radius*scale)};
    }
    private Node3D[] parts = Array.Empty<Node3D>();
    private Transform3D[] bodyPoses=Array.Empty<Transform3D>();
    public IEnumerable<MeshInstance3D> BodyMeshes=>parts.SelectMany(part=>part.GetChildren().OfType<MeshInstance3D>());
    public event Action? AppearanceChanging;
    public event Action? BodyRenderStateChanged;
    public IEnumerable<(MeshInstance3D Mesh,int PartIndex)> BodyMeshBindings
    {
        get{for(int i=0;i<parts.Length;i++)foreach(var mesh in parts[i].GetChildren().OfType<MeshInstance3D>())yield return(mesh,i);}
    }
    public Transform3D BodyPartTransform(int index)=>index>=0&&index<bodyPoses.Length?bodyPoses[index]:Transform3D.Identity;
    public void RefreshBodyPoseCache()
    {
        for(int i=0;i<parts.Length;i++)bodyPoses[i]=parts[i].Transform;
        BodyRenderStateChanged?.Invoke();
    }
    public Aabb BodyPoseBounds(float meshRadius)
    {
        var extent=Vector3.Zero;float stretch=1;
        foreach(var pose in bodyPoses)
        {
            var offset=pose.Origin.Abs();extent=new(Math.Max(extent.X,offset.X),Math.Max(extent.Y,offset.Y),Math.Max(extent.Z,offset.Z));
            var basis=pose.Basis;stretch=Math.Max(stretch,MathF.Sqrt(basis.X.LengthSquared()+basis.Y.LengthSquared()+basis.Z.LengthSquared()));
        }
        extent+=Vector3.One*(meshRadius*stretch);return new(-extent,extent*2);
    }
    private uint motionTable;
    private uint motionStyle;
    private uint activeStyle;
    public const uint MagicStyle=0x80000049;
    public uint CombatStyle=>motionStyle==0?(motionTable==0?0:assets.Motion(motionTable).Style):motionStyle;
    public uint ActiveStyle=>activeStyle;
    public uint CurrentCommand=>currentCommand;
    public string LastActionKind{get;private set;}="";
    public uint LastActionStyle{get;private set;}
    public uint[] LastActionAnimations{get;private set;}=Array.Empty<uint>();
    public float LastActionDuration{get;private set;}
    public float AttackImpactDelay{get;private set;}
    public bool ActionBusy=>action;
    public float ActionRemaining=>action?Math.Max(0,clipDuration-time):0;
    public int HooksDispatched{get;private set;}
    private readonly List<(float At,AcFormats.Hook Hook)> hookEvents=new();
    private int hookCursor;
    public void SetCombatStyle(uint style)
    {
        if(style==motionStyle)return;motionStyle=style;currentCommand=0;action=false;clips=Array.Empty<AcFormats.Clip>();clipDuration=0;Play(0x41000003,false);
    }
    private AcFormats.Clip[] clips = Array.Empty<AcFormats.Clip>();
    private AcFormats.Clip[]? scaledPlaybackSource;
    private AcFormats.Clip[] scaledPlayback=Array.Empty<AcFormats.Clip>();
    private float scaledPlaybackSpeed;
    internal int ScaledPlaybackBuilds{get;private set;}
    internal int ScaledPlaybackHits{get;private set;}
    private AcFormats.Clip[]? lastActionAnimationSource;
    private uint[] lastActionAnimationIds=Array.Empty<uint>();
    internal bool ReusePlaybackClips{get;set;}=true;
    private float clipDuration;
    internal float CurrentMotionDuration => clipDuration;
    private readonly Dictionary<uint,AcFormats.Animation> activeAnimations=new();
    private AcFormats.Animation ActiveAnimation(uint id)=>activeAnimations.TryGetValue(id,out var animation)?animation:activeAnimations[id]=assets.Animation(id);
    private float time;
    private uint currentCommand;
    private bool action;
    public int FramesApplied { get; private set; }
    public int AnimationChanges { get; private set; }
    public int PoseChanges { get; private set; }
    public bool HoldFinalPose { get; set; }
    public Action<AcFormats.Hook, Node3D>? OnHook;
    public uint SoundTable {get;set;}
    private AcActor? held;
    public uint HeldSetup=>held?.Setup.Id??0;
    public uint HeldPlacement=>held?.PlacementUsed??0;
    public int HeldAppearanceId=>held?.NativeAppearanceId??0;
    public bool HasVisibleHeldMesh=>held is not null&&held.Visible&&held.parts.Any(p=>p.GetChildren().OfType<MeshInstance3D>().Any(m=>m.Visible&&m.Mesh?.GetSurfaceCount()>0));
    internal void SetHeldVisible(bool visible) { if (held is not null) held.Visible = visible; }
    public void SetShadowCasting(bool enabled)
    {
        var mode=enabled?GeometryInstance3D.ShadowCastingSetting.On:GeometryInstance3D.ShadowCastingSetting.Off;
        var pending=new Stack<Node>();pending.Push(this);
        while(pending.TryPop(out var node))
        {
            if(node is MeshInstance3D mesh)mesh.CastShadow=mode;
            foreach(var child in node.GetChildren())pending.Push(child);
        }
        BodyRenderStateChanged?.Invoke();
    }
    private readonly record struct HeldModelKey(uint Setup,uint Holding,uint Placement,float Scale,int Appearance);
    private Dictionary<HeldModelKey,(AcActor Actor,long Used)>? heldModels;
    private HeldModelKey heldKey;
    private bool hasHeldKey;
    private long heldUse;
    // Enabled only for automatically alternating player loadouts. Ordinary
    // creatures keep the original one-held-model lifetime and memory footprint.
    private int heldModelCacheCapacity;
    public int HeldModelCacheCapacity
    {
        get=>heldModelCacheCapacity;
        set
        {
            int capacity=Math.Clamp(value,0,16);if(capacity==heldModelCacheCapacity)return;
            heldModelCacheCapacity=capacity;
            if(capacity==0&&heldModels is not null)
            {
                foreach(var entry in heldModels.Values)if(entry.Actor!=held)
                {entry.Actor.GetParent()?.RemoveChild(entry.Actor);entry.Actor.QueueFree();}
                heldModels.Clear();heldModels=null;
            }
        }
    }
    public int HeldCachedModelCount=>heldModels?.Count??0;
    public uint PlacementUsed{get;private set;}
    public void Place(uint key)
    {
        if(!Setup.Placements.TryGetValue(key,out var poses))return;
        PlacementUsed=key;clips=Array.Empty<AcFormats.Clip>();clipDuration=0;
        for(int i=0;i<Math.Min(parts.Length,poses.Length);i++){var pose=AcAssets.Convert(poses[i]);bodyPoses[i]=pose;parts[i].Transform=pose;}
    }
    public void Hold(uint setup,uint holding,uint placement=0,float scale=1,RareCatalog.Creature? appearance=null)
    {
        var key=new HeldModelKey(setup,holding,placement,scale,appearance?.Id??0);if(hasHeldKey&&key==heldKey)return;
        var previousKey=heldKey;
        heldKey=key;hasHeldKey=true;
        int capacity=Math.Clamp(HeldModelCacheCapacity,0,16);
        var models=capacity>0?heldModels??=new():null;
        if(held is not null)
        {
            if(models?.ContainsKey(previousKey)==true)held.Visible=false;
            else {held.GetParent()?.RemoveChild(held);held.QueueFree();}
            held=null;
        }
        if(setup==0||!Setup.Holdings.TryGetValue(holding,out var location))return;
        if(models is not null&&models.TryGetValue(key,out var cached))
        {
            held=cached.Actor;held.Visible=true;models[key]=(held,++heldUse);held.SetInheritedOpacity(EffectiveOpacity);return;
        }
        while(models is not null&&models.Count>=capacity)
        {
            HeldModelKey oldest=default;long oldestUse=long.MaxValue;
            foreach(var entry in models)if(entry.Value.Used<oldestUse){oldest=entry.Key;oldestUse=entry.Value.Used;}
            var retired=models[oldest].Actor;retired.GetParent()?.RemoveChild(retired);retired.QueueFree();models.Remove(oldest);
        }
        held=assets.CreateActor(setup,0);Part(location.Part).AddChild(held);held.Transform=AcAssets.Convert(location.Pose).ScaledLocal(Vector3.One*Math.Clamp(scale,.05f,5));
        if(appearance is not null)held.ApplyNativeAppearance(appearance);
        uint wanted=placement!=0?placement:holding==2?3u:1u;
        // Missing combat placement falls back to native Default, never another
        // numbered pose: Tetsubo/Nodachi's frame 104 inverts the weapon's axis.
        if(!held.Setup.Placements.ContainsKey(wanted))wanted=0;
        if(held.Setup.Placements.ContainsKey(wanted))held.Place(wanted);
        else for(int i=0;i<held.parts.Length;i++){held.parts[i].Transform=Transform3D.Identity;held.bodyPoses[i]=Transform3D.Identity;}
        held.OnHook=(hook,part)=>OnHook?.Invoke(hook,part);
        held.SetInheritedOpacity(EffectiveOpacity);
        if(models is not null)models[key]=(held,++heldUse);
    }
    public bool HeldAttachmentMatchesNative(uint holding)
    {
        if(held is null||!Setup.Holdings.TryGetValue(holding,out var location)||held.GetParent()!=Part(location.Part))return false;
        return held.Position.IsEqualApprox(AcAssets.Convert(location.Pose).Origin)&&held.Setup.Placements.ContainsKey(held.PlacementUsed);
    }
    public Node3D Part(int index) => index >= 0 && index < parts.Length ? parts[index] : this;

    public void Initialize(AcAssets source, AcFormats.Setup setup, uint motion)
    {
        assets = source; Setup = setup; motionTable = motion != 0 ? motion : setup.MotionTable;checkedShieldKind=null;
        SoundTable=setup.SoundTable;
        Name = $"AC_{setup.Id:X8}";
        // DAT animation poses are already in setup space, including limb hierarchy.
        // Applying ParentIndex again would double the transforms and separate limbs.
        parts = new Node3D[setup.Parts.Length];
        bodyPoses=new Transform3D[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = new Node3D { Name = $"Part_{i}" }; AddChild(parts[i]);
            if (setup.Parts[i] != 0)
            {
                var mesh = new MeshInstance3D { Mesh = assets.Mesh(setup.Parts[i]), Scale = AcAssets.Convert(setup.Scales[i]).Abs() };
                mesh.SetMeta("ac_model", (long)setup.Parts[i]);
                parts[i].AddChild(mesh);
            }
        }
        for (int i = 0; i < parts.Length; i++)
        {
            var pose=AcAssets.Convert(setup.Placement[i]);bodyPoses[i]=pose;parts[i].Transform=pose;
        }
        if (!Play(0x41000003, false) && setup.DefaultAnimation != 0)SetClips(new[] { new AcFormats.Clip(setup.DefaultAnimation, 0, -1, 30) },0x41000003,false,motionTable==0?0:assets.Motion(motionTable).Style);
        ApplyPose(0);
        CacheMovementBody();
    }
    public int ArmorParts { get; private set; }
    public int NativeAppearanceId{get;private set;}
    public int NativeEquipmentCount{get;private set;}
    private Dictionary<int,uint> nativeBaseColors=new();
    private uint nativePaletteFallback;
    public int NativePaletteColorCount=>nativeBaseColors.Count;
    public int NativeModelSwapCount{get;private set;}
    public int NativeTextureSwapCount{get;private set;}
    public float NativeTranslucency{get;private set;}
    private float transientOpacity=1;
    private float inheritedOpacity=1;
    public float EffectiveOpacity=>(1-NativeTranslucency)*transientOpacity*inheritedOpacity;
    private readonly Dictionary<ulong,(StandardMaterial3D Material,Color Base)> nativeOpacityMaterials=new();
    public void SetAlpha(float opacity)
    {
        if(!float.IsFinite(opacity))return;opacity=Math.Clamp(opacity,0,1);if(opacity==transientOpacity)return;
        AppearanceChanging?.Invoke();transientOpacity=opacity;
        RefreshNativeOpacity();BodyRenderStateChanged?.Invoke();
    }
    private void RefreshNativeOpacity()
    {
        for(int part=0;part<parts.Length;part++)for(int child=0;child<parts[part].GetChildCount();child++)
            if(parts[part].GetChild(child) is MeshInstance3D mesh)ApplyNativeOpacity(mesh);
        held?.SetInheritedOpacity(EffectiveOpacity);shield?.SetInheritedOpacity(EffectiveOpacity);
    }
    private void SetInheritedOpacity(float opacity)
    {
        opacity=float.IsFinite(opacity)?Math.Clamp(opacity,0,1):1;if(opacity==inheritedOpacity)return;
        AppearanceChanging?.Invoke();inheritedOpacity=opacity;RefreshNativeOpacity();BodyRenderStateChanged?.Invoke();
    }
    private void ApplyNativeOpacity(MeshInstance3D mesh)
    {
        if(mesh.Mesh is null)return;float factor=EffectiveOpacity;
        for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
        {
            if(factor>=1){mesh.SetSurfaceOverrideMaterial(surface,null);continue;}
            if(mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D source)continue;
            ulong key=source.GetInstanceId();
            if(!nativeOpacityMaterials.TryGetValue(key,out var entry))
            {
                var copy=(StandardMaterial3D)source.Duplicate();copy.Transparency=BaseMaterial3D.TransparencyEnum.Alpha;
                entry=(copy,source.AlbedoColor);nativeOpacityMaterials[key]=entry;
            }
            var color=entry.Base;color.A*=factor;entry.Material.AlbedoColor=color;
            mesh.SetSurfaceOverrideMaterial(surface,entry.Material);
        }
    }
    private readonly List<string> appearanceWarnings=new();
    public IReadOnlyList<string> AppearanceWarnings=>appearanceWarnings;
    public void ApplyNativeAppearance(RareCatalog.Creature source)
    {
        AppearanceChanging?.Invoke();
        appearanceWarnings.Clear();
        NativeEquipmentCount=0;ArmorParts=0;
        NativeTranslucency=float.IsFinite(source.NativeTranslucency)?Math.Clamp(source.NativeTranslucency,0,1):0;
        // ObjDesc's base palette is a fallback for textures without their own
        // palette, not a replacement for every authored texture palette entry.
        var colors=new Dictionary<int,uint>();var changes=new Dictionary<int,AcFormats.ClothingPart>();
        if(source.Clothing!=0)
        {
            var clothing=assets.Clothing(source.Clothing);
            // Native clothing color effects are independent of its geometry
            // bindings. Shadow clothing can be palette-only, and custom body
            // setups must retain that palette even without a model-swap entry.
            foreach(var p in assets.ClothingColors(clothing,source.PaletteTemplate,source.Shade))colors[p.Key]=p.Value;
            if(clothing.Setups.TryGetValue(Setup.Id,out var clothingParts))
            {
                foreach(var part in clothingParts)
                    if(part.Part>=0&&part.Part<parts.Length)changes[part.Part]=part;
                    else appearanceWarnings.Add($"Clothing {source.Clothing:X8} refers to absent part {part.Part}.");
            }
            else appearanceWarnings.Add($"Clothing {source.Clothing:X8} has no setup {Setup.Id:X8}; keeping the authored base model.");
        }
        uint N(string s)=>uint.Parse(s,System.Globalization.CultureInfo.InvariantCulture);
        if(source.Appearance.TryGetValue("weenie_properties_palette",out var palettes))foreach(var p in palettes)
        {
            // Native object-description subpalette ranges are in eight-color
            // blocks; clothing table ranges above are already full indices.
            int offset=checked((int)N(p[1])*8),count=checked((N(p[2])==0?256:(int)N(p[2]))*8);
            foreach(var pair in assets.PaletteColors(N(p[0]),offset,count))colors[pair.Key]=pair.Value;
        }
        string signature="native/"+AcAssets.ColorSignature(colors);
        nativeBaseColors=colors;nativePaletteFallback=source.Palette;NativeModelSwapCount=NativeTextureSwapCount=0;
        for(int i=0;i<parts.Length;i++)
        {
            uint model=Setup.Parts[i];var textures=new Dictionary<uint,uint>();
            if(changes.TryGetValue(i,out var change)){model=change.Model;foreach(var p in change.Textures)textures[p.Key]=p.Value;}
            if(source.Appearance.TryGetValue("weenie_properties_anim_part",out var models))foreach(var p in models)if(N(p[0])==i)model=N(p[1]);
            if(source.Appearance.TryGetValue("weenie_properties_texture_map",out var maps))foreach(var p in maps)if(N(p[0])==i)textures[N(p[1])]=N(p[2]);
            if(model!=Setup.Parts[i])NativeModelSwapCount++;NativeTextureSwapCount+=textures.Count;
            var mesh=parts[i].GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
            if(model==0){if(mesh is not null)mesh.Visible=false;continue;}
            if(mesh is null){mesh=new MeshInstance3D{Scale=AcAssets.Convert(Setup.Scales[i]).Abs()};parts[i].AddChild(mesh);}
            mesh.Visible=true;mesh.SetMeta("ac_model",(long)model);
            mesh.Mesh=assets.WearMesh(new(i,model,textures),colors,signature,source.Palette);ApplyNativeOpacity(mesh);
        }
        NativeAppearanceId=source.Id;
        held?.SetInheritedOpacity(EffectiveOpacity);shield?.SetInheritedOpacity(EffectiveOpacity);
        AppearanceRevision++;BodyRenderStateChanged?.Invoke();
    }
    public void WearNativeEquipment(IEnumerable<RareCatalog.Creature> items)
    {
        AppearanceChanging?.Invoke();
        var dressed=new HashSet<int>();NativeEquipmentCount=0;
        uint N(string s)=>uint.Parse(s,System.Globalization.CultureInfo.InvariantCulture);
        foreach(var item in items)
        {
            if(item.Clothing==0)continue;
            var clothing=assets.Clothing(item.Clothing);
            if(!clothing.Setups.TryGetValue(Setup.Id,out var changes))
            {appearanceWarnings.Add($"Equipped clothing {item.Clothing:X8} has no setup {Setup.Id:X8}; leaving the body intact.");continue;}
            // Clothing textures often expose skin or other indices that their
            // own dye ranges do not replace. Preserve the actor's native body
            // palette first, then apply the worn item's authored overrides.
            var colors=new Dictionary<int,uint>(nativeBaseColors);
            foreach(var color in assets.ClothingColors(clothing,item.PaletteTemplate,item.Shade))colors[color.Key]=color.Value;
            if(item.Appearance.TryGetValue("weenie_properties_palette",out var palettes))foreach(var range in palettes)
            {
                int offset=checked((int)N(range[1])*8),count=checked((N(range[2])==0?256:(int)N(range[2]))*8);
                foreach(var color in assets.PaletteColors(N(range[0]),offset,count))colors[color.Key]=color.Value;
            }
            string signature="native-equipment/"+AcAssets.ColorSignature(colors);bool applied=false;
            foreach(var change in changes)
            {
                if(change.Part<0||change.Part>=parts.Length)
                {appearanceWarnings.Add($"Equipped clothing {item.Clothing:X8} refers to absent part {change.Part}.");continue;}
                uint model=change.Model;var textures=new Dictionary<uint,uint>(change.Textures);
                // Item object descriptions normally refer to their separate
                // ground model. Only a body-setup description uses body indices.
                if(item.Setup==Setup.Id)
                {
                    if(item.Appearance.TryGetValue("weenie_properties_anim_part",out var models))foreach(var row in models)if(N(row[0])==change.Part)model=N(row[1]);
                    if(item.Appearance.TryGetValue("weenie_properties_texture_map",out var maps))foreach(var row in maps)if(N(row[0])==change.Part)textures[N(row[1])]=N(row[2]);
                }
                var mesh=parts[change.Part].GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
                if(model==0){if(mesh is not null)mesh.Visible=false;dressed.Add(change.Part);applied=true;continue;}
                if(mesh is null){mesh=new MeshInstance3D{Scale=AcAssets.Convert(Setup.Scales[change.Part]).Abs()};parts[change.Part].AddChild(mesh);}
                mesh.Visible=true;mesh.SetMeta("ac_model",(long)model);mesh.Mesh=assets.WearMesh(new(change.Part,model,textures),colors,signature,item.Palette!=0?item.Palette:nativePaletteFallback);ApplyNativeOpacity(mesh);dressed.Add(change.Part);applied=true;
            }
            if(applied)NativeEquipmentCount++;
        }
        ArmorParts=dressed.Count;AppearanceRevision++;BodyRenderStateChanged?.Invoke();
    }
    public int AppearanceRevision { get; private set; }
    public string OutfitSignature { get; private set; } = "";
    public ulong PartTextureFingerprint(int part)
    {
        ulong hash=14695981039346656037;
        if(part<0||part>=parts.Length)return 0;
        foreach(var node in parts[part].GetChildren().OfType<MeshInstance3D>())
        for(int i=0;i<(node.Mesh?.GetSurfaceCount()??0);i++)
        {
            if(node.Mesh!.SurfaceGetMaterial(i) is not StandardMaterial3D {AlbedoTexture:not null} material)continue;
            var image=material.AlbedoTexture.GetImage();if(image.IsCompressed())image.Decompress();
            foreach(byte value in image.GetData()){hash^=value;hash*=1099511628211;}
        }
        return hash;
    }
    public void Dress(IEnumerable<ArmorCatalog.Item> items)
    {
        AppearanceChanging?.Invoke();
        var outfit=items.OrderBy(i=>i.Priority).ToArray();
        OutfitSignature=string.Join("|",outfit.Select(i=>$"{i.Clothing:X8}:{i.Palette}:{i.Shade:F3}"));
        var dressed = new HashSet<int>();
        if(characterAppearance is not null)ApplyNativeAppearance(characterAppearance);
        else for (int i = 0; i < parts.Length; i++)
            foreach (var mesh in parts[i].GetChildren().OfType<MeshInstance3D>())
            {mesh.Visible=Setup.Parts[i]!=0;if(mesh.Visible)mesh.Mesh=assets.Mesh(Setup.Parts[i]);}
        foreach (var item in outfit)
        {
            var clothing = assets.Clothing(item.Clothing);
            if (!clothing.Setups.TryGetValue(Setup.Id, out var changes)) continue;
            var colors=new Dictionary<int,uint>(nativeBaseColors);
            foreach(var color in assets.ClothingColors(clothing,item.Palette,item.Shade))colors[color.Key]=color.Value;
            string colorSignature="clothing/"+AcAssets.ColorSignature(colors);
            foreach (var change in changes)
            {
                if (change.Part < 0 || change.Part >= parts.Length) continue;
                var mesh = parts[change.Part].GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
                if(change.Model==0){if(mesh is not null)mesh.Visible=false;dressed.Add(change.Part);continue;}
                if(mesh is null){mesh=new MeshInstance3D{Scale=AcAssets.Convert(Setup.Scales[change.Part]).Abs()};parts[change.Part].AddChild(mesh);}
                mesh.Visible=true;mesh.SetMeta("ac_model",(long)change.Model);mesh.Mesh=assets.WearMesh(change,colors,colorSignature,nativePaletteFallback);ApplyNativeOpacity(mesh);
                dressed.Add(change.Part);
            }
        }
        RefreshNativeOpacity();
        ArmorParts = dressed.Count; AppearanceRevision++;BodyRenderStateChanged?.Invoke();
    }
    public bool Play(uint command, bool once)
    {
        if(currentCommand==command&&clips.Length>0&&action==once)return true;
        uint style=motionStyle==0?(motionTable==0?0:assets.Motion(motionTable).Style):motionStyle;
        var selected=assets.Clips(motionTable,command,once,style,activeStyle==style?currentCommand:0x41000003);
        if(selected.Length==0)return false;
        SetClips(selected,command,once,style);return true;
    }
    private void SetClips(AcFormats.Clip[] selected,uint command,bool once,uint style,float playbackSpeed=1)
    {
        float speed=Math.Clamp(playbackSpeed,.2f,4);
        activeAnimations.Clear();foreach(var clip in selected)activeAnimations.TryAdd(clip.Animation,assets.Animation(clip.Animation));
        clips=PlaybackClips(selected,speed);currentCommand=command;activeStyle=style;action=once;time=0;hookCursor=0;AnimationChanges++;
        // Playback clips and their native frame ranges are immutable until the
        // next SetClips. Preserve the exact Sum arithmetic once per transition.
        clipDuration=clips.Sum(Duration);
        hookEvents.Clear();float at=0;
        foreach(var clip in clips)
        {
            var animation=ActiveAnimation(clip.Animation);var range=Range(clip);float fps=Math.Abs(clip.Fps);
            if(animation.Frames.Length==0){at+=Duration(clip);continue;}
            for(int frame=range.Low;frame<=range.High;frame++)
            {
                float when=at+(fps<.001f?0:(clip.Fps<0?range.High-frame:frame-range.Low)/fps);
                foreach(var hook in animation.Frames[frame].Hooks)
                    if(hook.Direction is 0 or -2||hook.Direction==(clip.Fps<0?-1:1))hookEvents.Add((when,hook));
                if(fps<.001f)break;
            }
            at+=Duration(clip);
        }
        hookEvents.Sort((a,b)=>a.At.CompareTo(b.At));
    }
    private AcFormats.Clip[] PlaybackClips(AcFormats.Clip[] selected,float speed)
    {
        if(!ReusePlaybackClips)return CopyPlaybackClips(selected,speed);
        // Native clip arrays/records are immutable. Normal-speed locomotion and
        // creature actions can use them directly. Keep only the last scaled
        // sequence so repeated casting avoids copies without an expanding cache.
        if(speed==1)return selected;
        if(ReferenceEquals(scaledPlaybackSource,selected)&&scaledPlaybackSpeed==speed)
        {ScaledPlaybackHits++;return scaledPlayback;}
        var playback=CopyPlaybackClips(selected,speed);
        scaledPlaybackSource=selected;scaledPlaybackSpeed=speed;scaledPlayback=playback;ScaledPlaybackBuilds++;return playback;
    }
    private static AcFormats.Clip[] CopyPlaybackClips(AcFormats.Clip[] selected,float speed)
    {
        var playback=new AcFormats.Clip[selected.Length];
        for(int i=0;i<selected.Length;i++)playback[i]=selected[i] with{Fps=selected[i].Fps*speed};
        return playback;
    }
    private bool BeginAction(uint command,uint style,string kind,AcFormats.Clip[] selected,float speed=1,float? release=null)
    {
        if(action||selected.Length==0)return false;
        SetClips(selected,command,true,style,speed);LastActionKind=kind;LastActionStyle=style;
        if(!ReusePlaybackClips||!ReferenceEquals(lastActionAnimationSource,selected))
        {
            var ids=new uint[selected.Length];for(int i=0;i<selected.Length;i++)ids[i]=selected[i].Animation;
            lastActionAnimationSource=selected;lastActionAnimationIds=ids;
        }
        LastActionAnimations=lastActionAnimationIds;LastActionDuration=clipDuration;
        var attack=hookEvents.Where(e=>e.Hook.Type==3).Select(e=>e.At).DefaultIfEmpty(-1).First();
        AttackImpactDelay=Math.Clamp(release is not null?release.Value/Math.Clamp(speed,.2f,4):attack>=0?attack:LastActionDuration*.45f,.015f,Math.Max(.015f,LastActionDuration));
        return true;
    }
    public bool Cast(bool self,float playbackSpeed=1)
    {
        if(action)return false;
        bool humanoid=characterAppearance is not null||motionTable==0x09000001;
        uint style=humanoid?MagicStyle:CreatureCombatStyle;
        foreach(uint command in self?new uint[]{0x40000031,0x4000002d}:new uint[]{0x4000002b,0x40000032,0x400000d3})
        {
            var selected=assets.Clips(motionTable,command,true,style);
            if(selected.Length>0)return BeginAction(command,style,self?"self-cast":"projectile-cast",selected,playbackSpeed);
        }
        return false;
    }
    public bool Drink(float playbackSpeed=1)
    {
        uint style=CombatStyle;return BeginAction(0x4000001b,style,"drink",assets.Clips(motionTable,0x4000001b,true,style),playbackSpeed);
    }
    public bool AttackWeapon(string kind,float playbackSpeed=1)
    {
        if(action)return false;
        uint style=CombatStyle;
        if(kind=="caster")return Cast(false,playbackSpeed);
        if(kind is "bow" or "crossbow" or "thrown")
        {
            // Retail humanoids author missile action through AimLevel/Reload paths,
            // rather than the creature-only MissileAttack command family.
            var aim=assets.Clips(motionTable,0x4000001e,true,style);
            var fire=kind=="thrown"?Array.Empty<AcFormats.Clip>():assets.Clips(motionTable,0x41000003,true,style,0x4000001e);
            var release=assets.Clips(motionTable,0x40000016,true,style,kind=="thrown"?0x4000001eu:0x41000003u);
            var recover=assets.Clips(motionTable,0x41000003,true,style,0x40000016);
            if(aim.Length==0||release.Length==0)return false;
            float when=aim.Sum(Duration)+(kind=="thrown"?release.Sum(Duration)*.35f:0);
            return BeginAction(0x40000016,style,"missile-"+kind,aim.Concat(fire).Concat(release).Concat(recover).ToArray(),playbackSpeed,when);
        }
        foreach(uint command in (style&0xffff)==0x3c?new uint[]{0x10000063,0x10000062,0x10000064}:kind=="dagger"?new uint[]{0x10000058,0x1000005c}:new uint[]{0x1000005c,0x10000058})
        {
            var selected=assets.Clips(motionTable,command,true,style);
            if(selected.Length>0)return BeginAction(command,style,"melee-"+kind,selected,playbackSpeed);
        }
        return false;
    }
    public bool Attack(bool missile=false)
    {
        if(characterAppearance is not null||motionTable==0x09000001)
            return AttackWeapon(missile?(CombatStyle&0xffff)==0x41?"crossbow":(CombatStyle&0xffff)==0x47?"thrown":"bow":(CombatStyle&0xffff)==0x44?"axe":"sword");
        foreach(uint command in new uint[]{0x10000063,0x10000062,0x10000064})if(AttackCreature(command))return true;
        return false;
    }
    public uint CreatureCombatStyle=>motionTable!=0&&assets.Motion(motionTable).StyleDefaults.ContainsKey(0x8000003c)?0x8000003c:CombatStyle;
    public bool AttackCreature(uint command,float playbackSpeed=1)
    {
        uint style=CreatureCombatStyle;var selected=assets.Clips(motionTable,command,true,style);
        return BeginAction(command,style,"creature-attack",selected,playbackSpeed);
    }
    private (int Low,int High) Range(AcFormats.Clip clip)
    {
        int count=ActiveAnimation(clip.Animation).Frames.Length;
        int low=Math.Clamp(clip.Low,0,Math.Max(0,count-1)),high=clip.High<0?count-1:Math.Clamp(clip.High,0,Math.Max(0,count-1));
        return (low,Math.Max(low,high));
    }
    private float Duration(AcFormats.Clip clip)
    {
        var range=Range(clip);return Math.Abs(clip.Fps)<.001f?1f/30:(range.High-range.Low+1)/Math.Abs(clip.Fps);
    }
    private void EmitUntil(float until)
    {
        while(hookCursor<hookEvents.Count&&hookEvents[hookCursor].At<=until)
        {
            var hook=hookEvents[hookCursor++].Hook;HooksDispatched++;
            if(hook.Type is 23 or 24)
            {
                for(int i=0;i<Setup.Parts.Length;i++)if(hook.Part==-1||hook.Part==i)
                    foreach(var mesh in parts[i].GetChildren().OfType<MeshInstance3D>())if(mesh.Mesh is not null)assets.ScrollMesh(mesh.Mesh,hook.U,hook.V);
            }
            else if(hook.Asset!=0||hook.Type is 14 or 15)OnHook?.Invoke(hook,Part(hook.Part));
        }
    }
    public void Tick(float dt, bool moving, Vector3 direction)
    {
        if(!action)Face(direction);
        if(!action)
        {
            uint command=moving?0x44000007u:0x41000003u;
            if(currentCommand!=command&&!Play(command,false)&&moving)Play(0x45000005,false);
        }
        TickCurrentMotion(dt, moving);
    }
    internal void TickCurrentMotion(float dt, bool moving = false)
    {
        if(clips.Length==0)return;
        float length=clipDuration;time+=Math.Max(0,dt);
        if(action&&time>=length)
        {
            ApplyPose(Math.Max(0,length-.0001f));EmitUntil(length);
            if(HoldFinalPose){time=Math.Max(0,length-.0001f);return;}
            action=false;currentCommand=0;Play(moving?0x44000007u:0x41000003u,false);ApplyPose(0);return;
        }
        for(int loop=0;time>=length&&loop<8;loop++){EmitUntil(length);time-=length;hookCursor=0;}
        if(time>=length)time%=Math.Max(.001f,length);
        ApplyPose(time);EmitUntil(time);
    }
    public void Face(Vector3 direction)
    {
        if (direction.LengthSquared() > 0.01f) Rotation = new Vector3(0, Mathf.Atan2(-direction.X, -direction.Z), 0);
    }
    internal static Transform3D InterpolateNativePose(AcFormats.Pose a,AcFormats.Pose b,float blend)
    {
        var position=System.Numerics.Vector3.Lerp(a.Position,b.Position,blend);
        var rotation=System.Numerics.Quaternion.Slerp(a.Rotation,b.Rotation,blend);
        // The same DAT-to-Godot conversion as AcAssets.Convert, without a
        // temporary reference-type Pose allocation for every animated limb.
        return new(new Basis(new Quaternion(rotation.X,rotation.Z,-rotation.Y,rotation.W)),AcAssets.Convert(position));
    }
    private void ApplyPose(float cursor)
    {
        if(clips.Length==0)return;
        var clip=clips[^1];
        foreach(var candidate in clips){clip=candidate;float duration=Duration(candidate);if(cursor<duration)break;cursor-=duration;}
        var animation=ActiveAnimation(clip.Animation);if(animation.Frames.Length==0)return;
        var range=Range(clip);float offset=Math.Clamp(cursor*Math.Abs(clip.Fps),0,range.High-range.Low+.9999f);
        float frame=clip.Fps<0?range.High-offset:range.Low+offset;
        frame=Math.Clamp(frame,range.Low,range.High);int first=(int)MathF.Floor(frame),next=Math.Min(first+1,range.High);float blend=frame-first;
        for(int i=0;i<Math.Min(animation.PartCount,parts.Length);i++)
        {
            var a=animation.Frames[first].Parts[i];var b=animation.Frames[next].Parts[i];
            var transform=InterpolateNativePose(a,b,blend);
            if(!bodyPoses[i].IsEqualApprox(transform)){PoseChanges++;parts[i].Transform=transform;bodyPoses[i]=transform;}
        }
        FramesApplied++;
    }
}

