using System.Numerics;

namespace DerethSurvivors;

public static class ProjectileCollision
{
    private const float Epsilon = 0.000001f;
    public readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C);
    public readonly record struct Hit(float Fraction, Vector3 Position, string Kind,Vector3 Normal=default);
    public static Vector3 ClosestPoint(Vector3 p,Triangle triangle)
    {
        var ab=triangle.B-triangle.A;var ac=triangle.C-triangle.A;var ap=p-triangle.A;
        float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);if(d1<=0&&d2<=0)return triangle.A;
        var bp=p-triangle.B;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return triangle.B;
        float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return triangle.A+ab*(d1/(d1-d3));
        var cp=p-triangle.C;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return triangle.C;
        float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return triangle.A+ac*(d2/(d2-d6));
        float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return triangle.B+(triangle.C-triangle.B)*((d4-d3)/((d4-d3)+(d5-d6)));
        float denominator=va+vb+vc;if(Math.Abs(denominator)<Epsilon)return triangle.A;
        return triangle.A+ab*(vb/denominator)+ac*(vc/denominator);
    }
    public static (float Distance,Vector3 Normal) CylinderDistance(Vector3 point,Vector3 origin,Vector3 axis,float height,float radius)
    {
        var relative=point-origin;float y=Vector3.Dot(relative,axis);var radial=relative-axis*y;float length=radial.Length();
        float side=length-radius,cap=Math.Max(-y,y-height);
        var radialNormal=length>Epsilon?radial/length:Vector3.UnitX;var capNormal=y<height*.5f?-axis:axis;
        float sx=Math.Max(side,0),sy=Math.Max(cap,0),outside=MathF.Sqrt(sx*sx+sy*sy);
        var normal=outside>Epsilon?(radialNormal*sx+capNormal*sy)/outside:side>cap?radialNormal:capNormal;
        return (outside+Math.Min(Math.Max(side,cap),0),normal);
    }
    public static (float Fraction,Vector3 Normal)? Cylinder(Vector3 start,Vector3 end,Vector3 origin,Vector3 axis,float height,float radius,float movingRadius)
    {
        var direction=end-start;float speed=direction.Length(),t=0;
        for(int i=0;i<96;i++)
        {
            var result=CylinderDistance(start+direction*t,origin,axis,height,radius);float separation=result.Distance-movingRadius;
            if(separation<=.00002f)return (t,result.Normal);
            if(speed<Epsilon)return null;
            t+=separation/speed;if(t>1)return null;
        }
        return null;
    }
    public static Vector3 InterceptVelocity(Vector3 origin, Vector3 target, Vector3 targetVelocity, float speed)
    {
        var offset = target - origin; float a = targetVelocity.LengthSquared() - speed * speed, b = 2 * Vector3.Dot(offset, targetVelocity), c = offset.LengthSquared();
        float time = -1;
        if (Math.Abs(a) < Epsilon) { if (Math.Abs(b) > Epsilon) time = -c / b; }
        else
        {
            float discriminant = b * b - 4 * a * c;
            if (discriminant >= 0)
            {
                float root = MathF.Sqrt(discriminant), t1 = (-b - root) / (2 * a), t2 = (-b + root) / (2 * a);
                time = t1 > 0 && t2 > 0 ? Math.Min(t1, t2) : Math.Max(t1, t2);
            }
        }
        var direction = time > 0 && time < 15 ? target + targetVelocity * time - origin : offset;
        return direction.LengthSquared() > Epsilon ? Vector3.Normalize(direction) * speed : Vector3.Zero;
    }
    // Arc spells author lateral speed. Solve the horizontal interception time,
    // then compensate the vertical component for constant downward gravity.
    public static bool TryArcVelocity(Vector3 origin,Vector3 target,Vector3 targetVelocity,float lateralSpeed,float gravity,out Vector3 velocity,out float flightTime)
    {
        velocity=Vector3.Zero;flightTime=0;
        if(!float.IsFinite(lateralSpeed)||lateralSpeed<=0||!float.IsFinite(gravity)||gravity<0)return false;
        var delta=target-origin;double a=targetVelocity.X*targetVelocity.X+targetVelocity.Z*targetVelocity.Z-lateralSpeed*lateralSpeed;
        double b=2*(delta.X*targetVelocity.X+delta.Z*targetVelocity.Z),c=delta.X*delta.X+delta.Z*delta.Z,t=-1;
        if(c<Epsilon)return false;
        if(Math.Abs(a)<Epsilon){if(Math.Abs(b)>Epsilon)t=-c/b;}
        else
        {
            double discriminant=b*b-4*a*c;if(discriminant<0)return false;
            double root=Math.Sqrt(discriminant),one=(-b-root)/(2*a),two=(-b+root)/(2*a);
            t=one>0&&two>0?Math.Min(one,two):Math.Max(one,two);
        }
        if(!double.IsFinite(t)||t<=.001||t>15)return false;
        flightTime=(float)t;velocity=delta/flightTime+targetVelocity+Vector3.UnitY*(gravity*flightTime*.5f);
        return float.IsFinite(velocity.X)&&float.IsFinite(velocity.Y)&&float.IsFinite(velocity.Z);
    }
    public static float? Sphere(Vector3 start, Vector3 end, Vector3 center, float radius)
    {
        Vector3 offset = start - center, direction = end - start;
        float c = offset.LengthSquared() - radius * radius;
        if (c <= 0) return 0;
        float a = direction.LengthSquared(); if (a < Epsilon) return null;
        float b = Vector3.Dot(offset, direction), d = b * b - a * c;
        if (d < 0) return null;
        float t = (-b - MathF.Sqrt(d)) / a; return t >= 0 && t <= 1 ? t : null;
    }
    public static float? Capsule(Vector3 start, Vector3 end, Vector3 a, Vector3 b, float radius)
    {
        Vector3 axis = b - a, relative = start - a, direction = end - start;
        float length = axis.LengthSquared(); if (length < Epsilon) return Sphere(start, end, a, radius);
        float projection = Math.Clamp(Vector3.Dot(relative, axis) / length, 0, 1);
        if ((relative - axis * projection).LengthSquared() <= radius * radius) return 0;
        float? nearest = Min(Sphere(start, end, a, radius), Sphere(start, end, b, radius));
        float axialDirection = Vector3.Dot(axis, direction), axialStart = Vector3.Dot(axis, relative);
        float qa = length * direction.LengthSquared() - axialDirection * axialDirection;
        float qb = length * Vector3.Dot(relative, direction) - axialStart * axialDirection;
        float qc = length * (relative.LengthSquared() - radius * radius) - axialStart * axialStart;
        float discriminant = qb * qb - qa * qc;
        if (Math.Abs(qa) > Epsilon && discriminant >= 0)
        {
            float t = (-qb - MathF.Sqrt(discriminant)) / qa, y = axialStart + t * axialDirection;
            if (t >= 0 && t <= 1 && y >= 0 && y <= length) nearest = Min(nearest, t);
        }
        return nearest;
    }
    private static bool InTriangle(Vector3 point, Triangle triangle)
    {
        Vector3 a = triangle.B - triangle.A, b = triangle.C - triangle.A, c = point - triangle.A;
        float aa = Vector3.Dot(a, a), ab = Vector3.Dot(a, b), bb = Vector3.Dot(b, b), ac = Vector3.Dot(a, c), bc = Vector3.Dot(b, c);
        float denominator = aa * bb - ab * ab; if (Math.Abs(denominator) < Epsilon) return false;
        float u = (bb * ac - ab * bc) / denominator, v = (aa * bc - ab * ac) / denominator;
        return u >= -0.00001f && v >= -0.00001f && u + v <= 1.00001f;
    }
    public static float? SweepTriangle(Vector3 start, Vector3 end, float radius, Triangle triangle)
    {
        Vector3 cross = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
        if (cross.LengthSquared() < Epsilon) return null;
        Vector3 normal = Vector3.Normalize(cross), direction = end - start;
        float distance = Vector3.Dot(start - triangle.A, normal), denominator = Vector3.Dot(direction, normal);
        float? nearest = null;
        if (Math.Abs(distance) <= radius && InTriangle(start - distance * normal, triangle)) return 0;
        if (Math.Abs(denominator) > Epsilon)
            for (int faceSide=0;faceSide<2;faceSide++)
            {
                float side=faceSide==0?-1f:1f;
                float t = (side * radius - distance) / denominator;
                if (t >= 0 && t <= 1 && InTriangle(start + t * direction - side * radius * normal, triangle)) nearest = Min(nearest, t);
            }
        nearest = Min(nearest, Capsule(start, end, triangle.A, triangle.B, radius));
        nearest = Min(nearest, Capsule(start, end, triangle.B, triangle.C, radius));
        return Min(nearest, Capsule(start, end, triangle.C, triangle.A, radius));
    }
    public static bool Box(Vector3 start, Vector3 end, Vector3 min, Vector3 max, float radius)
    {
        min -= new Vector3(radius); max += new Vector3(radius); Vector3 direction = end - start;
        float lo = 0, hi = 1;
        for (int axis = 0; axis < 3; axis++)
        {
            float p = axis == 0 ? start.X : axis == 1 ? start.Y : start.Z;
            float d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            float low = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float high = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (Math.Abs(d) < Epsilon) { if (p < low || p > high) return false; continue; }
            float a = (low - p) / d, b = (high - p) / d; if (a > b) (a, b) = (b, a);
            lo = Math.Max(lo, a); hi = Math.Min(hi, b); if (lo > hi) return false;
        }
        return true;
    }
    private static float? Min(float? a, float? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
}

public sealed class ProjectileWorld
{
    private sealed record Collider(Vector3 Min, Vector3 Max, ProjectileCollision.Triangle[] Triangles, string Kind,int Shape=0,Vector3 Origin=default,Vector3 Axis=default,float Height=0,float Radius=0);
    private readonly List<Collider> colliders = new();
    private readonly Dictionary<(int X,int Z),List<int>> cells=new();
    private readonly List<int> stamps=new();private int stamp;
    public int TriangleCount { get; private set; }
    public int PrimitiveCount {get;private set;}
    private void Register(Collider collider)
    {
        int index=colliders.Count;colliders.Add(collider);stamps.Add(0);
        for(int x=(int)MathF.Floor(collider.Min.X/8);x<=(int)MathF.Floor(collider.Max.X/8);x++)
        for(int z=(int)MathF.Floor(collider.Min.Z/8);z<=(int)MathF.Floor(collider.Max.Z/8);z++)
        {if(!cells.TryGetValue((x,z),out var list))cells[(x,z)]=list=new();list.Add(index);}
    }
    public void AddSphere(Vector3 center,float radius,string kind="scenery")
    {
        if(radius<=0||!float.IsFinite(radius+center.LengthSquared()))return;
        Register(new(center-new Vector3(radius),center+new Vector3(radius),Array.Empty<ProjectileCollision.Triangle>(),kind,1,center,Radius:radius));PrimitiveCount++;
    }
    public void AddCylinder(Vector3 origin,Vector3 axis,float height,float radius,string kind="scenery")
    {
        if(radius<=0||height<=0||!float.IsFinite(radius+height+origin.LengthSquared()+axis.LengthSquared())||axis.LengthSquared()<.001f)return;
        axis=Vector3.Normalize(axis);var end=origin+axis*height;
        var extent=new Vector3(MathF.Sqrt(Math.Max(0,1-axis.X*axis.X)),MathF.Sqrt(Math.Max(0,1-axis.Y*axis.Y)),MathF.Sqrt(Math.Max(0,1-axis.Z*axis.Z)))*radius;
        Register(new(Vector3.Min(origin,end)-extent,Vector3.Max(origin,end)+extent,Array.Empty<ProjectileCollision.Triangle>(),kind,2,origin,axis,height,radius));PrimitiveCount++;
    }
    public void Add(IEnumerable<ProjectileCollision.Triangle> triangles, string kind)
    {
        var faces = triangles.ToArray(); if (faces.Length == 0) return;
        var min = new Vector3(float.PositiveInfinity); var max = new Vector3(float.NegativeInfinity);
        foreach (var face in faces) foreach (var v in new[] {face.A, face.B, face.C}) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        Register(new(min, max, faces, kind)); TriangleCount += faces.Length;
    }
    public ProjectileCollision.Hit? Trace(Vector3 start, Vector3 end, float radius,bool includeTerrain=true,bool requireInward=false,float? stepTop=null)
    {
        float closest = float.PositiveInfinity; string kind = "";Vector3 normal=default,direction=end-start;
        var min=Vector3.Min(start,end)-new Vector3(radius);var max=Vector3.Max(start,end)+new Vector3(radius);
        if(++stamp==int.MaxValue){for(int i=0;i<stamps.Count;i++)stamps[i]=0;stamp=1;}
        for(int x=(int)MathF.Floor(min.X/8);x<=(int)MathF.Floor(max.X/8);x++)
        for(int z=(int)MathF.Floor(min.Z/8);z<=(int)MathF.Floor(max.Z/8);z++)
        {
            if(!cells.TryGetValue((x,z),out var list))continue;
            foreach(int index in list)
            {
            if(stamps[index]==stamp)continue;stamps[index]=stamp;var collider=colliders[index];
            if(!includeTerrain&&collider.Kind=="terrain"||stepTop is not null&&collider.Max.Y<stepTop)continue;
            if (!ProjectileCollision.Box(start, end, collider.Min, collider.Max, radius)) continue;
            if(collider.Shape==1)
            {
                float? hit=ProjectileCollision.Sphere(start,end,collider.Origin,radius+collider.Radius);
                if(hit is not null){var n=Vector3.Lerp(start,end,hit.Value)-collider.Origin;Consider(hit.Value,n.LengthSquared()<.000001f?Vector3.UnitX:n,collider.Kind);}
            }
            if(collider.Shape==2)
            {
                var hit=ProjectileCollision.Cylinder(start,end,collider.Origin,collider.Axis,collider.Height,collider.Radius,radius);
                if(hit is not null)Consider(hit.Value.Fraction,hit.Value.Normal,collider.Kind);
            }
            foreach (var face in collider.Triangles)
            {
                if(stepTop is not null&&Math.Max(face.A.Y,Math.Max(face.B.Y,face.C.Y))<stepTop)continue;
                if(!ProjectileCollision.Box(start,end,Vector3.Min(face.A,Vector3.Min(face.B,face.C)),Vector3.Max(face.A,Vector3.Max(face.B,face.C)),radius))continue;
                float? hit = ProjectileCollision.SweepTriangle(start, end, radius, face);
                if (hit is not null&&hit.Value<closest)
                {
                    var point=Vector3.Lerp(start,end,hit.Value);var n=point-ProjectileCollision.ClosestPoint(point,face);
                    if(n.LengthSquared()<.0000001f){n=Vector3.Cross(face.B-face.A,face.C-face.A);if(Vector3.Dot(n,direction)>0)n=-n;}
                    Consider(hit.Value,n,collider.Kind);
                }
            }
            }
        }
        return float.IsFinite(closest) ? new(closest, Vector3.Lerp(start, end, closest), kind,normal) : null;
        void Consider(float fraction,Vector3 n,string category)
        {
            if(fraction>=closest||n.LengthSquared()<.0000001f)return;
            n=Vector3.Normalize(n);var horizontal=new Vector3(n.X,0,n.Z);
            if(requireInward&&(horizontal.LengthSquared()<.00001f||Vector3.Dot(horizontal,direction)>=-.000001f))return;
            closest=fraction;kind=category;normal=n;
        }
    }
}
