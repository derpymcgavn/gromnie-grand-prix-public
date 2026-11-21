using Godot;

namespace DerethSurvivors;

// Plays original DAT emitter records and original particle meshes/materials.
// CPU simulation lets Compatibility rendering run the same effects headlessly.
public sealed partial class AcEffects(AcAssets assets, Node3D world, bool reuseParticleVisuals = true)
{
    private sealed class SystemState(AcFormats.Emitter definition, Node3D anchor, Transform3D offset, float limit, uint emitterId)
    {
        public AcFormats.Emitter Definition = definition;
        public Node3D Anchor = anchor;
        public Transform3D Offset = offset, LastTransform;
        public float Age, Budget, Limit = limit;
        public uint EmitterId = emitterId;
        public uint Layers = 1;
        public int Emitted, Active;
        public bool Stopped;
    }
    private sealed class Particle(uint meshId, MeshInstance3D node)
    {
        public readonly uint MeshId=meshId;
        public readonly MeshInstance3D Node=node;
        public readonly AcParticleOpacity Opacity=new(node);
        public SystemState System=null!;
        public Transform3D Origin;
        public Vector3 Offset, A, B, C;
        public float Life, Age, ScaleNoise, AlphaNoise;
        public uint FacingMode;
        public Vector3 FacingX=Vector3.Right,FacingY=Vector3.Up;
    }
    private sealed record Scheduled(Node3D Anchor, Transform3D Offset, AcFormats.TimedHook Event, float At, float Limit, uint Layers);
    private readonly List<SystemState> systems = new();
    private readonly List<Particle> particles = new();
    // Retain a bounded number of native mesh/material instances. Every visual
    // keeps its own alpha; repeated births reuse their already prepared state.
    private readonly Dictionary<uint,Stack<Particle>> idleParticles=new();
    private int retainedParticles;
    private bool ticking,hasCamera;
    private Vector3 cameraPosition;
    private Basis cameraBasis;
    public int CreatedParticleVisuals{get;private set;}
    public int ReusedParticleVisuals{get;private set;}
    public int RetainedParticleVisuals=>retainedParticles;
    private readonly List<Scheduled> scheduled = new();
    private readonly List<(Node3D Node, float Until)> anchors = new();
    private readonly RandomNumberGenerator rng = new() { Seed = 44017 };
    private readonly HashSet<string> warnings = new();
    private float clock;
    public int TotalParticles { get; private set; }
    public int PlayedScripts { get; private set; }
    public int ActiveParticles => particles.Count;
    public int Unsupported => warnings.Count;
    public int MaxObservedParticles { get; private set; }
    private const int ParticleLimit = 1400;
    public Action<AcFormats.Hook,Node3D>? OnSound;

    private void Warn(string message) { if (warnings.Add(message)) GD.PushWarning(message); }
    public void PlayAt(uint table, uint effect, Vector3 position, uint layers = 1)
    {
        var anchor = new Node3D { Position = position }; world.AddChild(anchor);
        anchors.Add((anchor, clock + 16)); PlayEffect(table, effect, anchor, layers: layers);
    }
    public void PlayEffect(uint table, uint effect, Node3D anchor, float limit = 3, uint layers = 1)
    {
        if (effect == 0) return;
        try { uint id = assets.EffectScript(table, effect); if (id != 0) PlayScript(id, anchor, Transform3D.Identity, limit, layers); }
        catch (Exception ex) { Warn($"AC effect {table:X8}/{effect}: {ex.Message}"); }
    }
    public void PlayScript(uint id, Node3D anchor, Transform3D offset, float limit = 3, uint layers = 1)
    {
        try
        {
            var events = assets.Script(id); PlayedScripts++;
            foreach (var e in events)
                if (e.Hook.Type is 1 or 2 or 21 or 13 or 26 or 19 or 14 or 15)
                    scheduled.Add(new(anchor, offset, e, clock + (float)e.Time, limit, layers));
        }
        catch (Exception ex) { Warn($"AC script {id:X8}: {ex.Message}"); }
    }
    public void PlayHook(AcFormats.Hook hook, Node3D anchor) => Execute(hook, anchor, Transform3D.Identity, 2, 0, 1);
    private void Execute(AcFormats.Hook hook, Node3D anchor, Transform3D offset, float limit, int depth, uint layers)
    {
        if (depth > 8 || !GodotObject.IsInstanceValid(anchor) || !anchor.IsInsideTree()) return;
        try
        {
            if(hook.Type is 1 or 2 or 21){OnSound?.Invoke(hook,anchor);return;}
            if (hook.Type is 13 or 26)
            {
                if (hook.Part >= 0)
                {
                    var actor = anchor as AcActor ?? anchor.GetChildren().OfType<AcActor>().FirstOrDefault();
                    if (actor is not null) anchor = actor.Part(hook.Part);
                }
                if(!ticking)SnapshotCamera();
                var definition = assets.Emitter(hook.Asset);
                if (systems.Count >= 180) return;
                var transform = offset * AcAssets.Convert(hook.Offset);
                var state = new SystemState(definition, anchor, transform, limit, hook.EmitterId)
                    { LastTransform = anchor.GlobalTransform * transform, Layers = layers };
                systems.Add(state);
                int initial = Math.Clamp(definition.Initial, 0, Math.Min(definition.Max, 100));
                for (int i = 0; i < initial; i++) Emit(state);
            }
            else if (hook.Type == 19)
            {
                uint id = hook.Asset;
                if ((id >> 24) == 0x33) PlayScript(id, anchor, offset, limit, layers);
                else Warn($"Unsupported AC nested PES {id:X8}");
            }
            else if (hook.Type is 14 or 15)
                foreach(var system in systems)if(system.Anchor==anchor&&system.EmitterId==hook.EmitterId)system.Stopped=true;
        }
        catch (Exception ex) { Warn($"AC particle hook {hook.Asset:X8}: {ex.Message}"); }
    }
    private Vector3 RandomVector() => new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(-1, 1), rng.RandfRange(-1, 1)).Normalized();
    private void Emit(SystemState state)
    {
        var e = state.Definition;
        if (particles.Count >= ParticleLimit || state.Active >= Math.Min(e.Max, 160) || (e.Total > 0 && state.Emitted >= e.Total)) return;
        uint meshId = e.HardwareMesh != 0 ? e.HardwareMesh : e.Mesh;
        if (meshId == 0) return;
        var mesh = assets.Mesh(meshId, true);
        if (mesh.GetSurfaceCount() == 0) return;
        Vector3 offsetDirection = AcAssets.Convert(e.Offset).Normalized();
        Vector3 offset = RandomVector();
        if (offsetDirection.LengthSquared() > 0.01f) offset -= offsetDirection * offset.Dot(offsetDirection);
        offset = offset.Normalized() * rng.RandfRange(e.MinOffset, e.MaxOffset);
        Vector3 a = AcAssets.Convert(e.A) * rng.RandfRange(e.MinA, e.MaxA);
        Vector3 b = AcAssets.Convert(e.B) * rng.RandfRange(e.MinB, e.MaxB);
        Vector3 c = AcAssets.Convert(e.C) * rng.RandfRange(e.MinC, e.MaxC);
        var origin = state.LastTransform;
        // Store trajectories in world coordinates; local emitters follow their attachment.
        offset = origin.Basis * offset;
        if (e.ParticleType is 2 or 5 or 8 or 9) a = origin.Basis * a;
        if (e.ParticleType is 3 or 4 or 8 or 9) b = origin.Basis * b;
        if (e.ParticleType is 4 or 9) c = origin.Basis * c;
        if (e.ParticleType == 6) c = RandomVector() * c.Length();
        if (e.ParticleType == 7) c *= offset;
        float life = Math.Clamp((float)e.Life + rng.RandfRange(-(float)e.LifeRandom, (float)e.LifeRandom), 0.02f, 15);
        var particle=RentParticle(meshId,mesh);
        particle.Node.Layers = state.Layers;
        particle.System=state;particle.Origin=origin;particle.Offset=offset;particle.A=a;particle.B=b;particle.C=c;
        particle.Life=life;particle.Age=0;particle.ScaleNoise=rng.RandfRange(-e.ScaleRandom,e.ScaleRandom);
        particle.AlphaNoise=rng.RandfRange(-e.TransparencyRandom,e.TransparencyRandom);
        particle.FacingMode=0;particle.FacingX=Vector3.Right;particle.FacingY=Vector3.Up;
        var facing=assets.ParticleFacing(meshId);
        if(facing.Mode is >=2 and <=5){particle.FacingMode=facing.Mode.Value;particle.FacingX=facing.AxisX;particle.FacingY=facing.AxisY;}
        particles.Add(particle); state.Emitted++; state.Active++; TotalParticles++;
        Update(particle); MaxObservedParticles = Math.Max(MaxObservedParticles, particles.Count);
    }
    private Particle RentParticle(uint meshId,Mesh mesh)
    {
        if(reuseParticleVisuals&&idleParticles.TryGetValue(meshId,out var idle)&&idle.Count>0)
        {
            var reused=idle.Pop();
            ReusedParticleVisuals++;return reused;
        }
        // Active + idle visuals never exceed the original simultaneous limit.
        // A new mesh replaces one idle visual without reducing particle births.
        if(reuseParticleVisuals&&retainedParticles>=ParticleLimit)
        {
            foreach(var entry in idleParticles)
            {
                if(entry.Value.Count==0)continue;
                var evicted=entry.Value.Pop();evicted.Node.QueueFree();retainedParticles--;
                break;
            }
        }
        var node=new MeshInstance3D{Mesh=mesh,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
        world.AddChild(node);retainedParticles++;CreatedParticleVisuals++;
        return new(meshId,node);
    }
    private void ReturnParticle(Particle particle)
    {
        if(!reuseParticleVisuals){particle.Node.QueueFree();retainedParticles--;return;}
        particle.Opacity.HideForPool();particle.System=null!;
        if(!idleParticles.TryGetValue(particle.MeshId,out var idle))idleParticles.Add(particle.MeshId,idle=new());
        idle.Push(particle);
    }
    private void SnapshotCamera()
    {
        var camera=world.GetViewport().GetCamera3D();hasCamera=camera is not null;
        if(camera is not null){cameraPosition=camera.GlobalPosition;cameraBasis=camera.GlobalBasis;}
    }
    private void Update(Particle p)
    {
        var e = p.System.Definition; float t = p.Age;
        var basis = p.Origin.Basis;
        var position = p.Origin.Origin;
        if (e.Local) position = p.System.LastTransform.Origin;
        Vector3 displacement = e.ParticleType switch {
            1 => Vector3.Zero,
            2 or 12 => p.A * t,
            3 or 4 or 8 or 9 or 10 or 11 => p.A * t + p.B * (0.5f * t * t),
            5 => p.A * t + new Vector3(Mathf.Cos(p.B.X * t) * p.C.X, Mathf.Cos(p.B.Y * t) * p.C.Y, Mathf.Sin(p.B.Z * t) * p.C.Z),
            6 => p.C * p.A.X * t + p.B * t * t,
            7 => p.C * Mathf.Cos(p.A.X * t) + p.B * t * t,
            _ => p.A * t
        };
        if (e.ParticleType is 4 or 9 or 11)
        {
            Vector3 rotation = p.C * t;
            basis = new Basis(Quaternion.FromEuler(rotation)) * basis;
        }
        float amount = Mathf.Clamp(t / p.Life, 0, 1);
        float scale = Math.Max(0.001f, Mathf.Lerp(e.StartScale, e.EndScale, amount) + p.ScaleNoise * (1 - amount));
        var worldPosition=position+p.Offset+displacement;
        if(p.FacingMode!=0&&hasCamera)
            basis=ParticleFacingBasis(basis,worldPosition,cameraPosition,cameraBasis,p.FacingMode,p.FacingX,p.FacingY);
        p.Node.GlobalTransform = new(basis.Orthonormalized().Scaled(Vector3.One * scale), worldPosition);
        p.Opacity.SetTransparency(Mathf.Lerp(e.StartTransparency, e.EndTransparency, amount) + p.AlphaNoise * (1 - amount));
    }
    // Native degrade modes: 2 faces the viewer freely; 3/4/5 retain the
    // particle's AC X/Y/Z axis respectively. Other modes preserve geometry.
    // This is DerethClient RetailParticleFacing expressed in Godot axes.
    public static Basis ParticleFacingBasis(Basis orientation,Vector3 position,Vector3 eye,Basis cameraBasis,uint mode,Vector3 axisX,Vector3 axisY)
    {
        if(mode is <2 or >5)return orientation;
        var toViewer=eye-position;if(toViewer.LengthSquared()<1e-8f||Math.Abs(orientation.Determinant())<1e-8f)return orientation;
        toViewer=toViewer.Normalized();var rotation=orientation.Orthonormalized();
        var x=rotation*axisX;var y=rotation*axisY;
        if(mode==2)
        {
            x=toViewer.Cross(Vector3.Up);
            if(x.LengthSquared()<1e-8f){x=cameraBasis.X;y=cameraBasis.Y;}
            else{x=x.Normalized();y=x.Cross(toViewer);}
        }
        else
        {
            var axis=rotation*(mode==3?Vector3.Right:mode==4?Vector3.Forward:Vector3.Up);
            var normal=x.Cross(y);if(normal.LengthSquared()<1e-10f)return orientation;normal=normal.Normalized();
            var target=toViewer-axis*toViewer.Dot(axis);var current=normal-axis*normal.Dot(axis);
            if(target.LengthSquared()<1e-8f||current.LengthSquared()<1e-8f)return orientation;
            target=target.Normalized();current=current.Normalized();
            float angle=Mathf.Atan2(current.Cross(target).Dot(axis),Math.Clamp(current.Dot(target),-1,1));
            var spin=new Basis(axis,angle);x=spin*x;y=spin*y;
        }
        var source=new Basis(axisX,axisY,axisX.Cross(axisY));
        return new Basis(x,y,x.Cross(y))*source.Transposed()*Basis.FromScale(orientation.Scale);
    }
    public void Tick(float dt)
    {
        SnapshotCamera();ticking=true;
        clock += dt;
        for (int i = scheduled.Count - 1; i >= 0; i--)
        {
            var next = scheduled[i]; if (next.At > clock) continue;
            scheduled.RemoveAt(i);
            Execute(next.Event.Hook, next.Anchor, next.Offset, next.Limit, 0, next.Layers);
        }
        foreach (var state in systems)
        {
            state.Age += dt;
            bool valid = GodotObject.IsInstanceValid(state.Anchor) && state.Anchor.IsInsideTree() && !state.Anchor.IsQueuedForDeletion();
            var current = valid ? state.Anchor.GlobalTransform * state.Offset : state.LastTransform;
            float distance = current.Origin.DistanceTo(state.LastTransform.Origin);
            state.LastTransform = current;
            var e = state.Definition;
            state.Stopped |= !valid || state.Age > state.Limit || (e.Seconds > 0 && state.Age > e.Seconds) || (e.Total > 0 && state.Emitted >= e.Total);
            if (state.Stopped) continue;
            float interval = Math.Max(0.002f, (float)e.Birthrate);
            state.Budget += (e.Type == 2 ? distance : dt) / interval;
            int count = Math.Min(40, (int)state.Budget); state.Budget -= count;
            for (int i = 0; i < count; i++) Emit(state);
        }
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var particle = particles[i]; particle.Age += dt;
            if (particle.Age >= particle.Life) { particle.System.Active--;ReturnParticle(particle);particles.RemoveAt(i); }
            else Update(particle);
        }
        systems.RemoveAll(s => s.Stopped && s.Active == 0);
        for (int i = anchors.Count - 1; i >= 0; i--)
            if (anchors[i].Until <= clock) { anchors[i].Node.QueueFree(); anchors.RemoveAt(i); }
        ticking=false;
    }
    public void Clear()
    {
        foreach (var p in particles) p.Node.QueueFree();particles.Clear();
        foreach(var idle in idleParticles.Values)foreach(var p in idle)p.Node.QueueFree();
        idleParticles.Clear();retainedParticles=0;systems.Clear();scheduled.Clear();
        foreach (var a in anchors) a.Node.QueueFree(); anchors.Clear();
    }
    // Dedicated validation inspects actual renderer state without exporting or
    // allocating per-particle snapshots during ordinary gameplay.
    internal bool MatchesParticleState(AcEffects other)
    {
        if(particles.Count!=other.particles.Count||systems.Count!=other.systems.Count||rng.State!=other.rng.State||TotalParticles!=other.TotalParticles)return false;
        for(int i=0;i<particles.Count;i++)
        {
            var a=particles[i];var b=other.particles[i];
            if(a.MeshId!=b.MeshId||a.Age!=b.Age||a.Life!=b.Life||a.Node.Visible!=b.Node.Visible||!a.Node.GlobalTransform.IsEqualApprox(b.Node.GlobalTransform))return false;
            for(int surface=0;surface<a.Node.Mesh!.GetSurfaceCount();surface++)
                if(a.Node.GetSurfaceOverrideMaterial(surface) is StandardMaterial3D am&&b.Node.GetSurfaceOverrideMaterial(surface) is StandardMaterial3D bm&&am.AlbedoColor!=bm.AlbedoColor)return false;
        }
        return true;
    }
    internal bool IdleVisualsHidden=>idleParticles.Values.All(idle=>idle.All(p=>!p.Node.Visible));
}
