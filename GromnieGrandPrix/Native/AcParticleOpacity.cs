using Godot;

namespace DerethSurvivors;

// Compatibility ignores GeometryInstance3D.Transparency. Keep a particle's
// material alpha independent while sharing its mesh, textures and shader code.
public sealed class AcParticleOpacity
{
    private readonly MeshInstance3D node;
    private readonly List<(StandardMaterial3D Material,Color BaseColor)> materials=new();
    private float previous=float.NaN;
    private bool? previousVisibility;
    internal int VisibilityWrites{get;private set;}
    public AcParticleOpacity(MeshInstance3D target)
    {
        node=target;
        if(target.MaterialOverride is StandardMaterial3D all)
        {
            var copy=(StandardMaterial3D)all.Duplicate();target.MaterialOverride=copy;materials.Add((copy,all.AlbedoColor));
        }
        else if(target.Mesh is not null)
        {
            for(int i=0;i<target.Mesh.GetSurfaceCount();i++)
            {
                if((target.GetSurfaceOverrideMaterial(i)??target.Mesh.SurfaceGetMaterial(i)) is not StandardMaterial3D source)continue;
                var copy=(StandardMaterial3D)source.Duplicate();target.SetSurfaceOverrideMaterial(i,copy);materials.Add((copy,source.AlbedoColor));
            }
        }
    }
    internal void HideForPool()
    {
        if(previousVisibility!=false){node.Visible=false;previousVisibility=false;VisibilityWrites++;}
    }
    public void SetTransparency(float transparency)
    {
        float opacity=1-Mathf.Clamp(transparency,0,1);
        bool visible=opacity>0;
        if(previousVisibility!=visible){node.Visible=visible;previousVisibility=visible;VisibilityWrites++;}
        if(opacity==previous)return;previous=opacity;
        foreach(var entry in materials)
        {
            var color=entry.BaseColor;color.A*=opacity;entry.Material.AlbedoColor=color;
        }
    }
}
