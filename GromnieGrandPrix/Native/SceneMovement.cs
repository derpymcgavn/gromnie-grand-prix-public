using System.Numerics;

namespace DerethSurvivors;

public static class SceneMovement
{
    public readonly record struct BodySphere(Vector3 Offset,float Radius);
    public static bool Clear(ProjectileWorld world,Vector3 position,IReadOnlyList<BodySphere> body,float stepHeight)
    {
        for(int i=0;i<body.Count;i++)
        {
            var sphere=body[i];
            if(world.Trace(position+sphere.Offset,position+sphere.Offset,sphere.Radius,false,stepTop:position.Y+stepHeight) is not null)return false;
        }
        return true;
    }
    public static Vector3 Move(ProjectileWorld world,Vector3 start,Vector3 target,IReadOnlyList<BodySphere> body,Func<Vector3,Vector3> ground,Func<Vector3,bool> valid,float stepHeight)
    {
        var position=start;int steps=Math.Clamp((int)MathF.Ceiling(Vector3.Distance(start,target)/.5f),1,256);var increment=(target-start)/steps;
        for(int step=0;step<steps;step++)
        {
            var desired=ground(position+increment);
            for(int slide=0;slide<4;slide++)
            {
                var direction=desired-position;if(direction.LengthSquared()<.0000001f)break;
                ProjectileCollision.Hit? closest=null;
                for(int i=0;i<body.Count;i++)
                {
                    var sphere=body[i];
                    var hit=world.Trace(position+sphere.Offset,desired+sphere.Offset,sphere.Radius,false,true,Math.Min(position.Y,desired.Y)+stepHeight);
                    if(hit is not null&&(closest is null||hit.Value.Fraction<closest.Value.Fraction))closest=hit;
                }
                if(closest is null){if(valid(desired))position=desired;break;}
                var contact=closest.Value;float fraction=Math.Max(0,contact.Fraction-.003f/Math.Max(.001f,direction.Length()));
                var safe=ground(Vector3.Lerp(position,desired,fraction));if(!valid(safe))break;position=safe;
                var normal=new Vector3(contact.Normal.X,0,contact.Normal.Z);if(normal.LengthSquared()<.000001f)break;normal=Vector3.Normalize(normal);
                var remaining=desired-position;remaining-=normal*Math.Min(0,Vector3.Dot(remaining,normal));
                desired=ground(position+remaining);
            }
        }
        return position;
    }
}
