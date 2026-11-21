using Godot;

namespace DerethSurvivors;

public partial class AcActor
{
    private AcActor? shield;
    private (uint Setup,int Id,uint Palette,uint Template,float Shade,float Scale) shieldKey;
    public uint ShieldSetup=>shield?.Setup.Id??0;
    public uint ShieldPlacement=>shield?.PlacementUsed??0;
    public bool ShieldVisible=>shield?.Visible==true;
    internal AcActor? ShieldActor=>shield;
    internal uint NativeMotionTableId=>motionTable;
    private uint checkedShieldStyle;
    private string? checkedShieldKind;
    private bool checkedShieldSupport;
    public bool SupportsNativeWeaponStyle(uint style,string kind)
    {
        if(checkedShieldKind==kind&&checkedShieldStyle==style)return checkedShieldSupport;
        checkedShieldKind=kind;checkedShieldStyle=style;
        return checkedShieldSupport=NativeWeaponStyleAuthored(style,kind);
    }
    private bool NativeWeaponStyleAuthored(uint style,string kind)
    {
        if(motionTable==0||!assets.Motion(motionTable).StyleDefaults.ContainsKey(style)
            ||assets.Clips(motionTable,0x41000003,false,style).Length==0
            ||assets.Clips(motionTable,0x44000007,false,style).Length==0&&assets.Clips(motionTable,0x45000005,false,style).Length==0)return false;
        if(kind=="thrown")return assets.Clips(motionTable,0x4000001e,true,style).Length>0
            &&assets.Clips(motionTable,0x40000016,true,style,0x4000001e).Length>0;
        if(kind is not("sword" or "mace" or "dagger" or "axe" or "spear"))return false;
        // Use the same authored command order as AttackWeapon, without changing
        // the actor's clip/pose or asking another combat style for a substitute.
        uint first=kind=="dagger"?0x10000058u:0x1000005cu,second=kind=="dagger"?0x1000005cu:0x10000058u;
        return assets.Clips(motionTable,first,true,style).Length>0||assets.Clips(motionTable,second,true,style).Length>0;
    }
    public bool HasVisibleShieldMesh=>shield is not null&&shield.Visible&&shield.parts.Any(part=>part.GetChildren().OfType<MeshInstance3D>().Any(mesh=>mesh.Visible&&mesh.Mesh?.GetSurfaceCount()>0));
    public void HoldShield(RareCatalog.Creature? look,float scale=1)
    {
        var key=(look?.Setup??0,look?.Id??0,look?.Palette??0,look?.PaletteTemplate??0,look?.Shade??0,scale);
        if(shieldKey==key&&shield is not null)return;
        shieldKey=key;
        if(shield is not null){shield.GetParent()?.RemoveChild(shield);shield.QueueFree();shield=null;}
        if(look is null||look.Setup==0||!Setup.Holdings.TryGetValue(3,out var native))return;
        shield=assets.CreateActor(look.Setup,0);Part(native.Part).AddChild(shield);
        shield.Transform=AcAssets.Convert(native.Pose).ScaledLocal(Vector3.One*Math.Clamp(scale,.05f,5));
        shield.ApplyNativeAppearance(look);
        uint placement=shield.Setup.Placements.ContainsKey(6)?6u:0;
        if(shield.Setup.Placements.ContainsKey(placement))shield.Place(placement);
        else for(int i=0;i<shield.parts.Length;i++){shield.parts[i].Transform=Transform3D.Identity;shield.bodyPoses[i]=Transform3D.Identity;}
        shield.SetInheritedOpacity(EffectiveOpacity);shield.OnHook=(hook,part)=>OnHook?.Invoke(hook,part);
    }
    public void SetShieldVisible(bool visible){if(shield is not null&&shield.Visible!=visible)shield.Visible=visible;}
    public bool ShieldAttachmentMatchesNative()
    {
        if(shield is null||!Setup.Holdings.TryGetValue(3,out var native)||shield.GetParent()!=Part(native.Part))return false;
        var expected=AcAssets.Convert(native.Pose).ScaledLocal(Vector3.One*Math.Clamp(shieldKey.Scale,.05f,5));
        if(!shield.Transform.IsEqualApprox(expected))return false;
        // Retail shield setups commonly omit placement 6. The original
        // EquippedChildAttachment solver uses Default (0), then identity when
        // neither frame exists. Never substitute an arbitrary numbered pose.
        uint placement=shield.Setup.Placements.ContainsKey(6)?6u:0;
        if(shield.PlacementUsed!=placement)return false;
        shield.Setup.Placements.TryGetValue(placement,out var poses);
        for(int i=0;i<shield.parts.Length;i++)
        {
            var pose=poses is not null&&i<poses.Length?AcAssets.Convert(poses[i]):Transform3D.Identity;
            if(!shield.parts[i].Transform.IsEqualApprox(pose))return false;
        }
        return true;
    }
}
