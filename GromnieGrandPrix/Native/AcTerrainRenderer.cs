using Godot;

namespace DerethSurvivors;

// One opaque pass per landblock. Native albedos and inverse-alpha maps are
// shared by the entire 3x3 grid; no transparent road geometry is layered above it.
public sealed class AcTerrainRenderer
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AcAssets,AcTerrainRenderer> shared=new();
    public static AcTerrainRenderer For(AcAssets assets,AcWorldData.Region region)=>shared.GetValue(assets,key=>new(key,region));
    public readonly record struct CloudShadowState(Texture2D? Texture,Vector2 OffsetMetres,float ScaleMetres,float Strength);
    private static CloudShadowState cloudShadow=new(null,Vector2.Zero,256,0);
    private static readonly List<WeakReference<ShaderMaterial>> cloudMaterials=new();
    public static CloudShadowState CaptureCloudShadow()=>cloudShadow;
    public static void RestoreCloudShadow(CloudShadowState state)=>SetCloudShadow(state.Texture,state.OffsetMetres,state.ScaleMetres,state.Strength);
    public static void DisableCloudShadow()=>SetCloudShadow(cloudShadow.Texture,cloudShadow.OffsetMetres,cloudShadow.ScaleMetres,0);
    public static void SetCloudShadow(Texture2D? texture,Vector2 offsetMetres,float worldScaleMetres,float strength)
    {
        if(!float.IsFinite(offsetMetres.X)||!float.IsFinite(offsetMetres.Y))offsetMetres=Vector2.Zero;
        worldScaleMetres=float.IsFinite(worldScaleMetres)?Math.Clamp(worldScaleMetres,16,2048):256;
        strength=texture is null||!float.IsFinite(strength)?0:Math.Clamp(strength,0,.35f);
        var previous=cloudShadow;cloudShadow=new(texture,offsetMetres,worldScaleMetres,strength);
        for(int i=cloudMaterials.Count-1;i>=0;i--)
        {
            if(!cloudMaterials[i].TryGetTarget(out var material)||!GodotObject.IsInstanceValid(material)){cloudMaterials.RemoveAt(i);continue;}
            if(!ReferenceEquals(previous.Texture,texture))material.SetShaderParameter("cloud_shadow_map",texture is null?default(Variant):Variant.From(texture));
            if(previous.OffsetMetres!=offsetMetres)material.SetShaderParameter("cloud_shadow_offset",offsetMetres);
            if(previous.ScaleMetres!=worldScaleMetres)material.SetShaderParameter("cloud_shadow_scale",worldScaleMetres);
            if(previous.Strength!=strength)material.SetShaderParameter("cloud_shadow_strength",strength);
        }
    }
    internal static void InitializeCloudShadow(ShaderMaterial material)
    {
        cloudMaterials.Add(new(material));
        material.SetShaderParameter("cloud_shadow_map",cloudShadow.Texture is null?default(Variant):Variant.From(cloudShadow.Texture));
        material.SetShaderParameter("cloud_shadow_offset",cloudShadow.OffsetMetres);
        material.SetShaderParameter("cloud_shadow_scale",cloudShadow.ScaleMetres);
        material.SetShaderParameter("cloud_shadow_strength",cloudShadow.Strength);
    }
    public ShaderMaterial Material{get;}
    public int AlbedoLayers{get;}
    public int MaskLayers{get;}
    public long ArrayBytes{get;private set;}
    private const string ShaderCode="""
        shader_type spatial;
        render_mode cull_disabled, specular_disabled, diffuse_lambert;
        #include "res://Shaders/SurfaceRelief.gdshaderinc"
        #include "res://Shaders/TrackWater.gdshaderinc"
        uniform sampler2DArray terrain_albedos : source_color, filter_linear_mipmap, repeat_enable;
        uniform sampler2DArray terrain_masks : filter_linear_mipmap, repeat_disable;
        uniform sampler2D surface_grain : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform bool terrain_detail = true;
        uniform float terrain_albedo_scale : hint_range(0.0,1.0)=0.62;
        uniform sampler2D course_mask : hint_default_black, filter_linear_mipmap, repeat_disable;
        uniform vec2 course_origin = vec2(-288.0);
        uniform vec2 course_extent = vec2(576.0);
        uniform bool bridge_reflection_active = false;
        uniform sampler2D bridge_reflection : hint_default_black, filter_linear, repeat_disable;
        uniform float bridge_water_level;
        uniform vec2 bridge_reflection_center;
        uniform sampler2D cloud_shadow_map : hint_default_black, filter_linear_mipmap, repeat_enable;
        uniform vec2 cloud_shadow_offset=vec2(0.0);
        uniform float cloud_shadow_scale=256.0;
        uniform float cloud_shadow_strength : hint_range(0.0,0.35)=0.0;
        uniform float terrain_tiling[33];
        uniform vec2 terrain_uv_scale[33];
        uniform vec4 mask_uv_data[64];
        varying flat vec4 layer_ids;
        varying flat vec4 mask_ids;
        varying flat vec4 mask_turns;
        varying flat vec4 road_info;
        varying vec3 terrain_world;
        void vertex(){layer_ids=CUSTOM0;mask_ids=CUSTOM1;mask_turns=CUSTOM2;road_info=CUSTOM3;terrain_world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
        float water_layer(float layer) {
            return float((layer>=16.0&&layer<=20.0)||layer==22.0||layer==23.0);
        }
        vec2 rotate_mask(vec2 p,float r){
            if(r<0.5)return p;
            if(r<1.5)return vec2(1.0-p.y,p.x);
            if(r<2.5)return vec2(1.0-p.x,1.0-p.y);
            return vec2(p.y,1.0-p.x);
        }
        float native_alpha(vec2 p,float layer,float rotation){
            if(layer<0.0)return 1.0;
            int i=int(layer);vec4 data=mask_uv_data[i];
            vec2 uv=clamp(rotate_mask(p,rotation),data.zw*0.5,vec2(1.0)-data.zw*0.5)*data.xy;
            return texture(terrain_masks,vec3(uv,layer)).a;
        }
        vec3 native_color(vec2 uv,float layer){
            int i=int(layer);
            return texture(terrain_albedos,vec3(uv*terrain_tiling[i]*terrain_uv_scale[i],layer)).rgb;
        }
        void fragment(){
            vec2 cloud_world_xz=terrain_world.xz;
            vec3 eye=normalize(mat3(INV_VIEW_MATRIX)*VIEW);
            vec2 uv=vec2(UV.x,-UV.y);
            vec3 a=vec3(native_alpha(UV2,mask_ids.x,mask_turns.x),native_alpha(UV2,mask_ids.y,mask_turns.y),native_alpha(UV2,mask_ids.z,mask_turns.z));
            vec4 w=vec4(a.x*a.y*a.z,1.0-a.x,a.x*(1.0-a.y),a.y*(1.0-a.z));
            vec3 color=native_color(uv,layer_ids.x)*w.x;
            if(w.y>0.0)color+=native_color(uv,layer_ids.y)*w.y;
            if(w.z>0.0)color+=native_color(uv,layer_ids.z)*w.z;
            if(w.w>0.0)color+=native_color(uv,layer_ids.w)*w.w;
            float original_road=0.0;
            if(road_info.x>=0.0){
                float road=1.0-native_alpha(UV2,mask_ids.w,mask_turns.w)*native_alpha(UV2,road_info.y,road_info.z);
                original_road=road;
                color=mix(color,native_color(uv,road_info.x),road);
            }
            original_road=max(original_road, 1.0-step(0.1,abs(layer_ids.x-32.0)));
            vec4 course=texture(course_mask,(cloud_world_xz-course_origin)/course_extent);
            float native_water=clamp(dot(w,vec4(water_layer(layer_ids.x),water_layer(layer_ids.y),
                water_layer(layer_ids.z),water_layer(layer_ids.w))),0.0,1.0);
            course.rb*=1.0-smoothstep(.22,.84,native_water);
            if(course.b>0.0){
                vec3 soil=native_color(uv*1.10,7.0)*vec3(1.16,.98,.80);
                vec3 stone=native_color(uv*1.25,32.0)*vec3(.84,.81,.74);
                color=mix(color,soil,course.b*.26);
                color=mix(color,mix(soil,stone,.24),course.r*.91);
            }
            vec3 ground_up=normalize(mat3(INV_VIEW_MATRIX)*NORMAL);
            float detail=1.0;
            if(terrain_detail){
                float grain=texture(surface_grain,cloud_world_xz/1.04).r;
                float height=dot(color,vec3(.2126,.7152,.0722))*.20+(grain-.5)*.056;
                float close_detail=1.0-smoothstep(36.0,145.0,length(VERTEX));
                NORMAL=normalize(mix(NORMAL,surface_relief(VERTEX,NORMAL,height,.44),close_detail));
                detail+=(grain-.5)*.18*close_detail;
            }
            ALBEDO=clamp(color*terrain_albedo_scale*detail,vec3(0.0),vec3(1.0));
            if(cloud_shadow_strength>0.0){
                float coverage=texture(cloud_shadow_map,(cloud_world_xz+cloud_shadow_offset)/cloud_shadow_scale).r;
                ALBEDO*=1.0-cloud_shadow_strength*coverage;
            }
            vec3 reflection;
            vec3 base=ALBEDO;
            track_water(cloud_world_xz,course.r,course.a,ground_up,eye,TIME,base,reflection);
            float river=smoothstep(.35,.92,native_water)*(1.0-max(original_road,course.r));
            river*=smoothstep(.72,.97,ground_up.y);
            if(river>.008){
                vec3 normal=river_normal(cloud_world_xz,TIME);
                vec3 ray=reflect(-eye,normal);
                float fresnel=0.10+0.82*pow(1.0-clamp(dot(eye,normal),0.0,1.0),3.7);
                vec3 sky=water_sky(ray,TIME);
                if(bridge_reflection_active){
                    vec2 mirror_uv=vec2(SCREEN_UV.x,1.0-SCREEN_UV.y)+normal.xz*.24;
                    vec2 edge=min(mirror_uv,1.0-mirror_uv);
                    float available=smoothstep(0.0,.095,min(edge.x,edge.y));
                    available*=1.0-smoothstep(210.0,445.0,length(cloud_world_xz-bridge_reflection_center));
                    available*=1.0-smoothstep(.15,.72,abs(terrain_world.y-bridge_water_level));
                    sky=mix(sky,texture(bridge_reflection,clamp(mirror_uv,vec2(.001),vec2(.999))).rgb,available*.92);
                }
                float glint=pow(max(0.0,dot(ray,dereth_light)),120.0);
                base=mix(base,base*vec3(.42,.62,.69)+vec3(.015,.035,.042),river);
                reflection+=river*(sky*fresnel*.88+glint*dereth_horizon*.38);
                NORMAL=normalize(mix(NORMAL,mat3(VIEW_MATRIX)*normal,river*.80));
            }
            ALBEDO=base;
            EMISSION=reflection;
            ROUGHNESS=.99;SPECULAR=0.0;
        }
        """;
    public AcTerrainRenderer(AcAssets assets,AcWorldData.Region region)
    {
        var blend=region.Blend??throw new InvalidDataException("Native terrain texture-merge data is unavailable.");
        var descriptors=blend.Textures.OrderBy(t=>t.Type).ToArray();
        if(descriptors.Length!=33||descriptors.Where((t,i)=>t.Type!=i).Any())throw new InvalidDataException("Expected native terrain layers 0 through32, including the road descriptor.");
        var masks=blend.Corner.Concat(blend.Side).Concat(blend.Road).ToArray();
        if(masks.Length==0||masks.Length>64)throw new InvalidDataException("Native terrain alpha array is absent or exceeds64layers.");
        var albedoIds=descriptors.Select(d=>d.Texture).ToArray();var maskIds=masks.Select(m=>m.Texture).ToArray();
        var albedo=BuildArray(assets,albedoIds,"albedo",out var albedoScale,out var albedoSizes);
        var alpha=BuildArray(assets,maskIds,"mask",out var maskScale,out var maskSizes);
        AlbedoLayers=albedoIds.Length;MaskLayers=maskIds.Length;
        var tiling=descriptors.Select(d=>(float)d.Tiling).ToArray();
        if(tiling.Any(t=>t<=0||t>1024))throw new InvalidDataException("Invalid native terrain texture repeat count.");
        var maskData=new Vector4[64];for(int i=0;i<masks.Length;i++)maskData[i]=new(maskScale[i].X,maskScale[i].Y,1f/maskSizes[i].X,1f/maskSizes[i].Y);
        Material=new ShaderMaterial{Shader=new Shader{Code=ShaderCode}};
        Material.SetShaderParameter("terrain_albedos",albedo);Material.SetShaderParameter("terrain_masks",alpha);
        Material.SetShaderParameter("terrain_detail", assets.UseSurfaceRelief);
        if (assets.UseSurfaceRelief && assets.Texture(descriptors[7].DetailTexture) is { } grain)
            Material.SetShaderParameter("surface_grain", grain);
        Material.SetShaderParameter("terrain_tiling",tiling);Material.SetShaderParameter("terrain_uv_scale",albedoScale);Material.SetShaderParameter("mask_uv_data",maskData);
        InitializeCloudShadow(Material);
        GD.Print($"NATIVE TERRAIN ARRAYS: albedos={AlbedoLayers} masks={MaskLayers} albedo={albedo.GetWidth()}x{albedo.GetHeight()} alpha={alpha.GetWidth()}x{alpha.GetHeight()} mip-bytes={ArrayBytes} native-pixels=repeated-not-resized opaque-passes-per-block=1");
    }
    private Texture2DArray BuildArray(AcAssets assets,uint[] ids,string kind,out Vector2[] scale,out Vector2I[] sourceSizes)
    {
        var originals=new List<Image>();var layers=new Godot.Collections.Array<Image>();
        try
        {
            foreach(uint id in ids)
            {
                var texture=assets.Texture(id)??throw new InvalidDataException($"Missing native terrain {kind} texture {id:X8}.");
                var image=texture.GetImage();if(image.IsCompressed()&&image.Decompress()!=Error.Ok)throw new InvalidDataException($"Cannot decompress native terrain {kind} {id:X8}.");
                image.Convert(Image.Format.Rgba8);originals.Add(image);
            }
            int width=originals.Max(i=>i.GetWidth()),height=originals.Max(i=>i.GetHeight());
            scale=new Vector2[ids.Length];sourceSizes=new Vector2I[ids.Length];
            for(int i=0;i<originals.Count;i++)
            {
                var image=originals[i];int sw=image.GetWidth(),sh=image.GetHeight();
                if(width%sw!=0||height%sh!=0)throw new InvalidDataException($"Native terrain {kind} {ids[i]:X8} dimensions {sw}x{sh} cannot repeat losslessly in {width}x{height}.");
                var layer=Image.CreateEmpty(width,height,false,Image.Format.Rgba8);
                for(int x=0;x<width;x+=sw)for(int y=0;y<height;y+=sh)layer.BlitRect(image,new Rect2I(0,0,sw,sh),new Vector2I(x,y));
                layer.GenerateMipmaps();layers.Add(layer);ArrayBytes+=layer.GetData().LongLength;
                scale[i]=new((float)sw/width,(float)sh/height);sourceSizes[i]=new(sw,sh);
            }
            var array=new Texture2DArray();if(array.CreateFromImages(layers)!=Error.Ok){array.Dispose();throw new InvalidDataException("Native terrain "+kind+" array upload failed.");}return array;
        }
        finally{foreach(var image in originals)image.Dispose();foreach(var image in layers)image.Dispose();}
    }
    private static float Mask(byte value)=>value==AcTerrainBlend.Surface.None?-1:value;
    public static void SetRecipe(SurfaceTool tool,AcTerrainBlend.Surface recipe)
    {
        tool.SetCustom(0,new(recipe.BaseLayer,Mask(recipe.Ovl0Layer),Mask(recipe.Ovl1Layer),Mask(recipe.Ovl2Layer)));
        tool.SetCustom(1,new(Mask(recipe.Ovl0AlphaLayer),Mask(recipe.Ovl1AlphaLayer),Mask(recipe.Ovl2AlphaLayer),Mask(recipe.Road0AlphaLayer)));
        tool.SetCustom(2,new(recipe.Ovl0Rotation,recipe.Ovl1Rotation,recipe.Ovl2Rotation,recipe.Road0Rotation));
        tool.SetCustom(3,new(Mask(recipe.RoadLayer),Mask(recipe.Road1AlphaLayer),recipe.Road1Rotation,0));
    }
}


