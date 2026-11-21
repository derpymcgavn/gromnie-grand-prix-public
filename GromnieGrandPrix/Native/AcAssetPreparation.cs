using System.Collections;

namespace DerethSurvivors;

// Main-thread, pull-based preparation. The lease remains alive after completion
// so a bounded upcoming cohort can retain the exact cache entries it will use.
public sealed class AcAssetPreparation : IEnumerator<string>
{
    private readonly AcAssets assets;
    private IEnumerator<string>? work;
    private string current="";
    internal readonly HashSet<AcAssetCacheKey> Keys=new();
    internal LinkedListNode<AcAssetPreparation>? RetentionNode;
    private bool disposed;
    public bool RetentionValid{get;internal set;}=true;
    public bool Completed{get;private set;}
    public bool IsRetainedValid=>RetentionValid;
    public bool IsComplete=>Completed;
    public string Current=>current;
    object IEnumerator.Current=>Current;
    internal AcAssetPreparation(AcAssets owner,IEnumerator<string> steps)
    {assets=owner;work=steps;owner.RegisterPreparation(this);}
    public bool MoveNext()
    {
        if(disposed||Completed||!RetentionValid||work is null)return false;
        bool more;
        assets.BeginPreparationStep(this);
        try{more=work.MoveNext();if(more)current=work.Current;}
        catch{assets.InvalidatePreparation(this);throw;}
        finally{assets.EndPreparationStep(this);}
        if(!more){Completed=true;work.Dispose();work=null;assets.TrimCaches();}
        return more;
    }
    public void Reset()=>throw new NotSupportedException();
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try{work?.Dispose();work=null;}finally{assets.ReleasePreparation(this);}
    }
}

internal enum AcAssetCacheKind
{Geometry,Setup,Motion,Animation,Mesh,WornMesh,Material,Texture,Clothing,Palette,Emitter,Script,ScriptTable,LaunchScale,ParticleFacing,SurfaceDefinition,PortalTextureFormat}
internal readonly record struct AcAssetCacheKey(AcAssetCacheKind Kind,object Key);

public sealed partial class AcAssets
{
    private AcAssetPreparation? capturingPreparation;
    private readonly LinkedList<AcAssetPreparation> preparationLeases=new();
    private readonly Dictionary<AcAssetCacheKey,int> preparationPins=new();
    public int RetainedPreparationCount
    {get{int count=0;foreach(var plan in preparationLeases)if(plan.RetentionValid)count++;return count;}}
    public int RetainedPreparationKeys=>preparationPins.Count;
    public long PreparationInvalidations{get;private set;}
    public long CacheGeneration{get;private set;}
    internal void RegisterPreparation(AcAssetPreparation plan)
    {plan.RetentionNode=preparationLeases.AddLast(plan);}
    internal void BeginPreparationStep(AcAssetPreparation plan)
    {
        if(capturingPreparation is not null)throw new InvalidOperationException("Native preparation steps cannot be nested.");
        capturingPreparation=plan;
    }
    internal void EndPreparationStep(AcAssetPreparation plan)
    {if(ReferenceEquals(capturingPreparation,plan))capturingPreparation=null;}
    private void TouchAsset<TKey>(AcAssetCacheKind kind,TKey key) where TKey:notnull
    {
        var plan=capturingPreparation;if(plan is null||!plan.RetentionValid)return;
        // The queue bounds plans separately. Bound a malformed individual graph
        // too; it may still render through the ordinary native fallback.
        var identity=new AcAssetCacheKey(kind,key);
        if(plan.Keys.Contains(identity))return;
        if(plan.Keys.Count>=8192){InvalidatePreparation(plan);return;}
        if(plan.Keys.Add(identity))preparationPins[identity]=preparationPins.GetValueOrDefault(identity)+1;
    }
    internal void InvalidatePreparation(AcAssetPreparation plan)
    {
        if(!plan.RetentionValid)return;plan.RetentionValid=false;PreparationInvalidations++;
        ReleasePreparationKeys(plan);
    }
    private void ReleasePreparationKeys(AcAssetPreparation plan)
    {
        foreach(var key in plan.Keys)
        {
            int count=preparationPins.GetValueOrDefault(key);
            if(count<=1)preparationPins.Remove(key);else preparationPins[key]=count-1;
        }
        plan.Keys.Clear();
    }
    internal void ReleasePreparation(AcAssetPreparation plan)
    {
        ReleasePreparationKeys(plan);
        if(plan.RetentionNode is not null){preparationLeases.Remove(plan.RetentionNode);plan.RetentionNode=null;}
        plan.RetentionValid=false;
    }
    private void InvalidateAllPreparations()
    {
        foreach(var plan in preparationLeases)InvalidatePreparation(plan);
        CacheGeneration++;
    }
    private int PinnedAssetCount(AcAssetCacheKind kind)
    {
        int count=0;foreach(var key in preparationPins.Keys)if(key.Kind==kind)count++;return count;
    }
    private bool TrimAssetCache<TKey,TValue>(Dictionary<TKey,TValue> cache,int budget,AcAssetCacheKind kind,bool force) where TKey:notnull
    {
        if(force){bool any=cache.Count>0;cache.Clear();return any;}
        if(cache.Count<=budget)return false;
        // Release older plans if their combined dependency graph alone exceeds
        // the original cache budget; retention never expands those budgets.
        var cursor=preparationLeases.First;
        while(PinnedAssetCount(kind)>budget&&cursor is not null)
        {var next=cursor.Next;InvalidatePreparation(cursor.Value);cursor=next;}
        foreach(var key in cache.Keys.ToArray())
        {
            if(cache.Count<=budget)break;
            if(!preparationPins.ContainsKey(new(kind,key)))cache.Remove(key);
        }
        return true;
    }
}
